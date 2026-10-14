require "rails_helper"
require "ripper"

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
end
