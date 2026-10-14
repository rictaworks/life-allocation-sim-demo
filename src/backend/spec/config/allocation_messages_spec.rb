require "rails_helper"

# 配分のエラー文言は、規則の値（0・5・100 など）を書かない。規則は Domain Core（ホストの replay）だけが持つ。
# 規則の値を文言に書くと、規則が変わったときに文言が古くなるため、この検査で防ぐ。
RSpec.describe "配分のエラーの文言（config/locales/ja.yml）" do
  %i[allocation_negative allocation_step_mismatch allocation_sum_mismatch].each do |code|
    it "#{code} の文言に数字と % を含まない" do
      message = I18n.t("api_errors.#{code}", locale: :ja, raise: true)

      expect(message).not_to match(/[0-9%]/), "文言に規則の値が入っています（キー: #{code}）"
    end
  end
end
