require "rails_helper"

RSpec.describe "アプリケーションの設定" do
  it "API mode で動く（config.api_only が true）" do
    expect(Rails.application.config.api_only).to be(true)
  end
end
