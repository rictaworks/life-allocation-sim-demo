require "rails_helper"
require "ripper"
require "pathname"

# 文字列リテラルの分離の検査。Ruby のトークン（Ripper）で、"..." '...' %w[...] の文字列を調べる（正規表現は対象外）。
#
# 対象：app/・config/initializers/・lib/ の .rb。
#   日本語（ひらがな・カタカナ・漢字）の文字列は、config/・lib/ を含めてどこにも置かない（下の 1 つ目のテスト）。
#   英字・数字を含む文字列は、app/・config/initializers/・lib/ で次のどれかに当たるものだけを許す（下の 2 つ目のテスト）。
#
# 対象外（許す文字列）と、その理由：
#   a. 記号・空白・改行の区切り（例：", "・"\n"・"[api]" の枠）。英字や数字を含まない。
#   b. require のパス（Ruby の標準ライブラリと gem の名前）。言語の仕様上の固定語。
#   c. ハッシュのキー（"k" =>・"k":）、添字（x["k"]）、KEY_CALLS の引数、fetch の第 1 引数、%w の一覧の英字の語。
#      JSON の項目名（再計算ホストの Protocol.md と、API の応答・要求の項目名）。データの構造の名前であり、
#      値ではないため設定へ移さない。英字だけの語（識別子の形）に限る。
#      応答の値・ヘッダー名・環境変数名（Rack の env のキー）は、値として設定へ移す（config/api.yml・config/messages.yml）。
#   d. EXEMPTIONS に理由を書いたもの。Ruby・OS・Rails・DB の仕様上の固定語（シグナル名・ダイジェスト名・
#      レイアウト名）と、SQL の断片。文言や値ではない。
# 上のどれにも当たらない英字・数字の文字列（応答の値・ヘッダー名・ログ文・内部の例外の文言など）は、
# config/api.yml か config/messages.yml に置き、コードは設定経由で読む。
module HardcodedStringScanner
  # 識別子の形（ハッシュのキー・項目名）。ドット・ハイフンを含むもの（Content-Type など）は、ここには当たらない。
  IDENTIFIER = /\A[A-Za-z_][A-Za-z0-9_]*\z/
  JAPANESE = /[\p{Hiragana}\p{Katakana}\p{Han}]/
  # 引数のすべてが JSON の項目名になるメソッド（json_params・dig・integer_field!）。
  KEY_CALLS = %w[json_params dig integer_field!].freeze
  # 第 1 引数だけが項目名になるメソッド。fetch の第 2 引数は既定値なので、英字の語は値として違反にする。
  # 環境変数名（get_header・env の参照）は値として扱い、含めない。
  FIRST_ARGUMENT_KEY_CALLS = %w[fetch].freeze
  # 空白・改行・記号だけの区切り（エスケープを外してから見る）。
  SEPARATOR_ONLY = /\A[^A-Za-z0-9\p{Hiragana}\p{Katakana}\p{Han}]*\z/
  # 対象外 d の一覧（ファイルの相対パス → 文字列 → 理由）。ここに無い英字の文字列は違反にする。
  EXEMPTIONS = {
    "app/mailers/application_mailer.rb" => {
      "mailer" => "Rails の既定のレイアウト名（layouts/mailer）。Rails の規約の固定語。",
      "from@example.com" => "未使用の既定の送信元（Rails の生成物。このアプリはメールを送らない）。"
    },
    "app/models/session.rb" => {
      "expires_at > ?" => "SQL の断片（列名と演算子）。文言ではない。"
    },
    "app/services/request_rate_limit.rb" => {
      "SHA256" => "OpenSSL のダイジェスト名（HMAC の固定の指定）。",
      "created_at > ?" => "SQL の断片（列名と演算子）。文言ではない。",
      "created_at <= ?" => "SQL の断片（列名と演算子）。文言ではない。"
    },
    "app/services/core_host_process.rb" => {
      "KILL" => "OS のシグナル名（プロセスグループを止める固定の指定）。"
    }
  }.freeze
  SKIPPED_TOKENS = %i[on_sp on_ignored_nl on_nl on_comment on_embdoc_beg on_embdoc on_embdoc_end].freeze

  Offense = Data.define(:path, :line, :text, :reason)

  class << self
    # ソースの中の、検査に当たる文字列（違反）を返す。path は Rails.root からの相対パス（対象外の表の参照に使う）。
    def offenses(source, path)
      tokens = Ripper.lex(source).reject { |(_, type, _)| SKIPPED_TOKENS.include?(type) }
      raise ArgumentError, "heredoc is not supported by the scanner" if tokens.any? { |(_, type, _)| type == :on_heredoc_beg }

      offenses = []
      stack = []
      index = 0
      while index < tokens.size
        _position, type, _text = tokens[index]
        case type
        when :on_lparen
          stack.push([ :call, previous_name(tokens, index) ])
        when :on_lbracket
          stack.push(index_bracket?(tokens, index) ? [ :index ] : [ :array ])
        when :on_rparen, :on_rbracket
          stack.pop
        when :on_tstring_beg, :on_qwords_beg
          literal = read_literal(tokens, index)
          offenses.concat(check_literal(literal, path, tokens, index, stack))
        end
        index += 1
      end
      offenses
    end

    private

    # 文字列の 1 つ分を読む。pieces は直下の文字列の内容（補間の中の文字列は含めない）。
    def read_literal(tokens, start)
      depth = 0
      pieces = []
      list = tokens[start][1] == :on_qwords_beg
      finish = start
      (start...tokens.size).each do |j|
        _position, type, text = tokens[j]
        if %i[on_tstring_beg on_qwords_beg].include?(type)
          depth += 1
        elsif %i[on_tstring_end on_label_end].include?(type)
          depth -= 1
        elsif type == :on_tstring_content && depth == 1
          pieces << text
        end
        if depth.zero?
          finish = j
          break
        end
      end
      { pieces: pieces, list: list, line: tokens[start][0].first, end_type: tokens[finish][1], after: tokens[finish + 1] }
    end

    def check_literal(literal, path, tokens, start, stack)
      key = key_position?(literal, tokens, start, stack)
      require_path = previous_name(tokens, start) == "require"
      literal[:pieces].filter_map do |piece|
        next if SEPARATOR_ONLY.match?(piece.gsub(/\\./, ""))
        next if key && IDENTIFIER.match?(piece)
        next if require_path
        next if EXEMPTIONS.fetch(path, {}).key?(piece)

        Offense.new(path: path, line: literal[:line], text: piece, reason: reason_for(piece))
      end
    end

    def key_position?(literal, tokens, start, stack)
      after = literal[:after]
      return true if literal[:list]
      return true if literal[:end_type] == :on_label_end
      return true if after && after[1] == :on_op && after[2] == "=>"
      return true if stack.last == [ :index ]
      return true if stack.last&.first == :call && KEY_CALLS.include?(stack.last.last)
      return true if stack.last&.first == :call && FIRST_ARGUMENT_KEY_CALLS.include?(stack.last.last) && first_argument?(tokens, start)

      false
    end

    # 呼び出しの第 1 引数か（直前の字句が、呼び出しの開き括弧）。
    def first_argument?(tokens, start)
      return false if start.zero?

      tokens[start - 1][1] == :on_lparen
    end

    def reason_for(piece)
      return "日本語の文字列" if JAPANESE.match?(piece)

      "英字・数字を含む文字列（設定へ移す）"
    end

    # 直前のトークン（空白を除く）の名前。メソッド名（on_ident など）の時だけ返す。
    def previous_name(tokens, index)
      previous = tokens[index - 1]
      return nil if previous.nil?
      return previous[2] if %i[on_ident on_const on_fid].include?(previous[1])

      nil
    end

    # 角括弧が添字（x[...]）か。直前が値（識別子・閉じ括弧・定数）のときだけ添字とみなす。
    def index_bracket?(tokens, index)
      previous = tokens[index - 1]
      return false if previous.nil?

      %i[on_ident on_const on_rparen on_rbracket on_fid].include?(previous[1])
    end
  end
end

RSpec.describe "文字列リテラルの分離" do
  let(:ruby_files) { Dir[Rails.root.join("{app,config,lib}/**/*.{rb,rake}").to_s] }
  let(:japanese_text) { /[\p{Hiragana}\p{Katakana}\p{Han}]/ }

  it "app・config の Ruby コードに日本語の文字列リテラルを置かない（文言は config/locales/ja.yml に置く）" do
    expect(ruby_files).not_to be_empty

    offenders = ruby_files.flat_map do |path|
      Ripper.lex(File.read(path)).filter_map do |(position, type, token)|
        [ path, position.first, token ] if type == :on_tstring_content && token.match?(japanese_text)
      end
    end

    expect(offenders).to be_empty, "日本語の文字列リテラル: #{offenders.inspect}"
  end

  describe "英字・数字を含む文字列（app・config/initializers・lib）" do
    let(:scanned_files) do
      Dir[Rails.root.join("{app,config/initializers,lib}/**/*.rb").to_s].sort
    end

    it "検査の対象のファイルを読む（コントローラ・サービス・初期化子を含む）" do
      relatives = scanned_files.map { |absolute| Pathname.new(absolute).relative_path_from(Rails.root).to_s }

      expect(relatives).to include("app/controllers/api/sessions_controller.rb", "config/initializers/api_write_guard.rb")
    end

    it "応答の値・ヘッダー名・ログ文などの英字の文字列を、コードに直接置かない（設定の YAML から読む）" do
      offenses = scanned_files.flat_map do |absolute|
        relative = Pathname.new(absolute).relative_path_from(Rails.root).to_s
        HardcodedStringScanner.offenses(File.read(absolute), relative)
      end

      expect(offenses).to be_empty,
        "設定へ移す文字列が残っています（ファイル:行 => 文字列）: " \
        "#{offenses.map { |o| "#{o.path}:#{o.line} => #{o.text.inspect}" }.join(', ')}"
    end

    it "app・config/initializers・lib に、日本語の文字列を置かない（英字の検査と同じ対象で確かめる）" do
      japanese = scanned_files.flat_map do |absolute|
        relative = Pathname.new(absolute).relative_path_from(Rails.root).to_s
        HardcodedStringScanner.offenses(File.read(absolute), relative).select { |o| o.reason == "日本語の文字列" }
      end

      expect(japanese).to be_empty
    end
  end

  describe "検査の検査（スキャナが違反と対象外を正しく分けること）" do
    def offenses_of(source)
      HardcodedStringScanner.offenses(source, "app/example.rb").map(&:text)
    end

    it "代入された英字の値を違反にする" do
      expect(offenses_of(%(STATUS = "created".freeze\n))).to eq([ "created" ])
    end

    it "メソッドの引数の英字の文言（ログ文など）を違反にする" do
      expect(offenses_of(%(Rails.logger.error("[api] unexpected failure")\n))).to eq([ "[api] unexpected failure" ])
    end

    it "ヘッダー名（ハイフンを含む文字列）を違反にする" do
      expect(offenses_of(%(headers["Retry-After"] = value\n))).to eq([ "Retry-After" ])
    end

    it "日本語の文字列を違反にする" do
      expect(offenses_of(%(raise "失敗しました"\n))).to eq([ "失敗しました" ])
    end

    it "ハッシュのキー・添字・fetch の引数の項目名は対象外にする" do
      source = <<~RUBY
        x = { "age" => 1, "status": 2 }
        y = params["turn_no"] + state.fetch("status") + json_params("difficulty")["difficulty"]
        z = params.fetch("website", nil)
      RUBY

      expect(offenses_of(source)).to eq([])
    end

    it "require のパス・記号だけの文字列・文字列の区切りは対象外にする" do
      source = <<~RUBY
        require "ipaddr"
        sep = ", "
        joined = lines.join("\\n")
      RUBY

      expect(offenses_of(source)).to eq([])
    end

    it "%w の一覧の項目名は対象外にする" do
      expect(offenses_of(%(KEYS = %w[turn_no living_pct]\n))).to eq([])
    end

    it "fetch の第 2 引数（既定値）の英字の語は違反にする（項目名は第 1 引数だけ）" do
      expect(offenses_of(%(value = params.fetch("website", "created")\n))).to eq([ "created" ])
    end

    it "添字でも項目名でもない英字の値は、ハッシュの中でも違反にする" do
      expect(offenses_of(%(x = { "a" => "created" }\n))).to eq([ "created" ])
    end
  end
end
