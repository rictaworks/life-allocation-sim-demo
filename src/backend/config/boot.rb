ENV["BUNDLE_GEMFILE"] ||= File.expand_path("../Gemfile", __dir__)

require "bundler/setup" # Set up gems listed in the Gemfile.
require "bootsnap/setup" # Speed up boot time by caching expensive operations.

# RAILS_ENV が未設定（空を含む）なら production として起動する（Rails の既定の development にしない）。
# 開発では RAILS_ENV=development を明示する。
ENV["RAILS_ENV"] = "production" if ENV["RAILS_ENV"].to_s.strip.empty?
