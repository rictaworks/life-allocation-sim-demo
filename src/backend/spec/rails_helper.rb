require "spec_helper"
require_relative "../config/environment"

# テストは必ず test 環境で動かす（本番の設定では動かさない）。
abort("The Rails environment is running in production mode!") if Rails.env.production?

require "rspec/rails"

# spec/support 以下の補助を読む（*_spec.rb は置かない）。
Dir[Rails.root.join("spec/support/**/*.rb")].sort.each { |file| require file }

# db/schema.rb と test の DB の差があれば、テスト用の DB を作り直す。
ActiveRecord::Migration.maintain_test_schema!

RSpec.configure do |config|
  config.use_transactional_fixtures = true
  config.include ActiveSupport::Testing::TimeHelpers
  config.filter_rails_from_backtrace!
end
