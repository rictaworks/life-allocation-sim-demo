require "rails_helper"

RSpec.describe "AppEnvironment（APP_ENV による分岐）" do
  describe ".development?" do
    it "APP_ENV が development のときだけ true を返す" do
      expect(AppEnvironment.development?({ "APP_ENV" => "development" })).to be(true)
    end

    it "未設定・空・表記ゆれ・不明な値は本番として扱い、false を返す（fail closed）" do
      values = [ nil, "", "production", "Development", " development", "development ", "dev", "staging" ]
      values.each do |value|
        env = value.nil? ? {} : { "APP_ENV" => value }
        expect(AppEnvironment.development?(env)).to be(false), "APP_ENV=#{value.inspect}"
      end
    end
  end
end
