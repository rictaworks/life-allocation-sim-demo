require "rails_helper"
require "ripper"

# 既定値を持つ ENV.fetch（ENV.fetch(名前, 既定値) と ENV.fetch(名前) { 既定値 }）を置かない。
# 既定値は設定ファイル（config/server.yml など）に明示する（CLAUDE.md「フォールバック禁止」）。
# 字句解析で調べるため、コメントや文字列の中の文字には反応しない。
module EnvDefaultScanner
  IGNORED_TOKENS = %i[on_sp on_ignored_nl on_nl on_comment on_embdoc_beg on_embdoc on_embdoc_end].freeze
  OPENING_TOKENS = %i[on_lparen on_lbracket on_lbrace].freeze
  CLOSING_TOKENS = %i[on_rparen on_rbracket on_rbrace].freeze
  FETCH_START = [ [ :on_const, "ENV" ], [ :on_period, "." ], [ :on_ident, "fetch" ], [ :on_lparen, "(" ] ].freeze

  module_function

  # Ruby のファイル（.rb・.rake）はそのまま調べる。YAML と ERB は、<%= %> の中の式だけを、行の位置を保って調べる。
  def ruby_code(path, source)
    return source if path.end_with?(".rb", ".rake")

    source.each_line.map { |line| "#{line.scan(/<%=?(.*?)%>/).flatten.join('; ')}\n" }.join
  end

  # 既定値を持つ ENV.fetch の行番号の一覧。
  def defaulted_fetch_lines(code)
    tokens = Ripper.lex(code).map { |(position, type, text)| [ position.first, type, text ] }
    significant = tokens.reject { |(_line, type, _text)| IGNORED_TOKENS.include?(type) }

    significant.each_index.filter_map do |index|
      next unless fetch_call_at?(significant, index)

      significant[index].first if defaulted_arguments?(significant, index + FETCH_START.size - 1)
    end
  end

  def fetch_call_at?(tokens, index)
    FETCH_START.each_with_index.all? { |(type, text), offset| at(tokens, index + offset) == [ type, text ] }
  end

  # open_index は開きかっこ。閉じかっこまでに引数のカンマがあるか、閉じかっこの直後にブロックがあれば、既定値がある。
  def defaulted_arguments?(tokens, open_index)
    depth = 0
    comma = false
    close_index = nil

    tokens.drop(open_index).each_with_index do |(_line, type, _text), offset|
      depth += 1 if OPENING_TOKENS.include?(type)
      depth -= 1 if CLOSING_TOKENS.include?(type)
      comma ||= depth == 1 && type == :on_comma
      next unless depth.zero?

      close_index = open_index + offset
      break
    end
    return false if close_index.nil?
    return true if comma

    type, text = at(tokens, close_index + 1)
    type == :on_lbrace || (type == :on_kw && text == "do")
  end

  def at(tokens, index)
    tokens[index]&.values_at(1, 2)
  end
end

RSpec.describe "環境変数の既定値の禁止（ENV.fetch に既定値を書かない）" do
  describe "検査の道具（EnvDefaultScanner が正しく見つけること）" do
    it "既定値（2 つ目の引数・{} のブロック・do のブロック）は行番号つきで見つける" do
      expect(EnvDefaultScanner.defaulted_fetch_lines('ENV.fetch("A", 3)')).to eq([ 1 ])
      expect(EnvDefaultScanner.defaulted_fetch_lines('ENV.fetch("A") { 5 }')).to eq([ 1 ])
      expect(EnvDefaultScanner.defaulted_fetch_lines("ENV.fetch(\"A\") do\n  5\nend")).to eq([ 1 ])
      expect(EnvDefaultScanner.defaulted_fetch_lines("ENV.fetch(name, nil)")).to eq([ 1 ])
      expect(EnvDefaultScanner.defaulted_fetch_lines("x = 1\nENV.fetch(\n  \"A\",\n  2\n)")).to eq([ 2 ])
    end

    it "既定値の無い ENV.fetch・ENV[]・コメントや文字列の中の文字は見つけない" do
      expect(EnvDefaultScanner.defaulted_fetch_lines('ENV.fetch("A")')).to eq([])
      expect(EnvDefaultScanner.defaulted_fetch_lines("ENV.fetch(key).then { |value| value }")).to eq([])
      expect(EnvDefaultScanner.defaulted_fetch_lines('ENV["A"] || 3')).to eq([])
      expect(EnvDefaultScanner.defaulted_fetch_lines('# ENV.fetch("A", 3)')).to eq([])
      expect(EnvDefaultScanner.defaulted_fetch_lines('x = "ENV.fetch(\"A\", 3)"')).to eq([])
    end
  end

  it "app・config・lib の Ruby コードと YAML・ERB に、既定値を持つ ENV.fetch を置かない" do
    files = Dir[Rails.root.join("{app,config,lib}/**/*.{rb,rake,yml,erb}").to_s]
    expect(files).not_to be_empty

    offenders = files.flat_map do |path|
      code = EnvDefaultScanner.ruby_code(path, File.read(path))
      EnvDefaultScanner.defaulted_fetch_lines(code).map { |line| "#{path.delete_prefix(Rails.root.to_s)}:#{line}" }
    end

    expect(offenders).to be_empty, "既定値を持つ ENV.fetch: #{offenders.inspect}"
  end
end
