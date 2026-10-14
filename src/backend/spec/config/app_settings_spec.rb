require "rails_helper"
require "json"
require "open3"

RSpec.describe "起動時の設定検査（AppSettings）" do
  let(:valid_env) do
    {
      "RAILS_ENV" => "production",
      "SECRET_KEY_BASE" => "a" * 64,
      "DATABASE_PATH" => "/var/lib/app/data.sqlite3",
      "APP_ORIGIN" => "https://life-allocation.rictaworks.jp"
    }
  end

  describe ".verify!" do
    it "必要な値がそろっていれば何もしない" do
      expect { AppSettings.verify!(valid_env) }.not_to raise_error
    end

    %w[SECRET_KEY_BASE DATABASE_PATH APP_ORIGIN].each do |name|
      it "#{name} が無ければ起動を止め、メッセージに変数名を出す" do
        expect { AppSettings.verify!(valid_env.except(name)) }
          .to raise_error(AppSettings::Invalid, /#{name}/)
      end

      it "#{name} が空でも起動を止める（既定値で補わない）" do
        expect { AppSettings.verify!(valid_env.merge(name => "")) }
          .to raise_error(AppSettings::Invalid, /#{name}/)
      end
    end

    it "RAILS_ENV が development・test・production 以外なら起動を止める" do
      expect { AppSettings.verify!(valid_env.merge("RAILS_ENV" => "staging")) }
        .to raise_error(AppSettings::Invalid, /RAILS_ENV/)
    end

    it "APP_ENV=development は RAILS_ENV=development のときだけ許す（本番の設定で開発用の分岐を有効にしない）" do
      expect { AppSettings.verify!(valid_env.merge("APP_ENV" => "development")) }
        .to raise_error(AppSettings::Invalid, /APP_ENV/)
    end

    it "RAILS_ENV=development のときは APP_ENV も development にする（未設定・表記ゆれは起動を止める）" do
      [ nil, "", "Development", " development", "dev" ].each do |value|
        env = valid_env.merge("RAILS_ENV" => "development", "APP_ENV" => value)

        expect { AppSettings.verify!(env) }
          .to raise_error(AppSettings::Invalid, /APP_ENV/), "APP_ENV=#{value.inspect}"
      end
    end

    it "SECRET_KEY_BASE が 64 文字未満なら起動を止め、値をメッセージに出さない" do
      short_value = "short-secret-value"
      expect { AppSettings.verify!(valid_env.merge("SECRET_KEY_BASE" => short_value)) }
        .to raise_error(AppSettings::Invalid) { |error| expect(error.message).not_to include(short_value) }
    end

    it "DATABASE_PATH は絶対パスだけを受け付ける" do
      expect { AppSettings.verify!(valid_env.merge("DATABASE_PATH" => "storage/data.sqlite3")) }
        .to raise_error(AppSettings::Invalid, /DATABASE_PATH/)
    end

    it "APP_ORIGIN は scheme://ホスト[:ポート] の形だけを受け付ける（末尾の / や path、大文字、scheme の欠落は不可）" do
      invalid_origins = [
        "https://life-allocation.rictaworks.jp/",
        "https://life-allocation.rictaworks.jp/path",
        "https://Life-Allocation.rictaworks.jp",
        "life-allocation.rictaworks.jp",
        "ftp://life-allocation.rictaworks.jp",
        "https://life-allocation.rictaworks.jp:99999x",
        "https://life-allocation.rictaworks.jp."
      ]
      invalid_origins.each do |origin|
        expect { AppSettings.verify!(valid_env.merge("APP_ORIGIN" => origin)) }
          .to raise_error(AppSettings::Invalid, /APP_ORIGIN/), "APP_ORIGIN=#{origin}"
      end
    end

    it "本番では APP_ORIGIN に http を使えない。開発（APP_ENV=development）では http://localhost を許す" do
      expect { AppSettings.verify!(valid_env.merge("APP_ORIGIN" => "http://localhost:8080")) }
        .to raise_error(AppSettings::Invalid, /APP_ORIGIN/)

      development_env = valid_env.merge(
        "RAILS_ENV" => "development",
        "APP_ENV" => "development",
        "APP_ORIGIN" => "http://localhost:8080"
      )
      expect { AppSettings.verify!(development_env) }.not_to raise_error
    end
  end

  describe "実際の起動（config/environment の読み込み）" do
    let(:backend_root) { Rails.root.to_s }

    def boot(env, script)
      Open3.capture3(env, RbConfig.ruby, "-e", script, chdir: backend_root)
    end

    def production_boot_env(overrides = {})
      {
        "APP_ENV" => nil,
        "RAILS_ENV" => "production",
        "SECRET_KEY_BASE" => ENV.fetch("SECRET_KEY_BASE"),
        "DATABASE_PATH" => ENV.fetch("DATABASE_PATH"),
        "APP_ORIGIN" => "https://life-allocation.rictaworks.jp"
      }.merge(overrides)
    end

    it "SECRET_KEY_BASE が無ければ起動を止める（変数名を出し、値は出さない）" do
      _out, err, status = boot(production_boot_env("SECRET_KEY_BASE" => nil), "require './config/environment'")
      expect(status).not_to be_success
      expect(err).to include("SECRET_KEY_BASE")
    end

    it "RAILS_ENV が未設定なら production として起動し、本番の設定を使う" do
      script = <<~RUBY
        require "./config/environment"
        require "json"
        puts JSON.generate(
          env: Rails.env,
          api_only: Rails.application.config.api_only,
          force_ssl: Rails.application.config.force_ssl,
          assume_ssl: Rails.application.config.assume_ssl,
          hosts: Rails.application.config.hosts.map(&:to_s)
        )
      RUBY
      out, err, status = boot(production_boot_env("RAILS_ENV" => nil), script)
      expect(status).to be_success, err
      result = JSON.parse(out)
      expect(result).to include("env" => "production", "api_only" => true, "force_ssl" => true, "assume_ssl" => true)
      expect(result["hosts"]).to eq([ "life-allocation.rictaworks.jp" ])
    end

    def inspect_boot_script
      <<~RUBY
        require "./config/environment"
        require "json"
        config = Rails.application.config
        puts JSON.generate(
          env: Rails.env,
          force_ssl: config.force_ssl,
          assume_ssl: config.assume_ssl,
          enable_reloading: config.enable_reloading,
          eager_load: config.eager_load
        )
      RUBY
    end

    it "RAILS_ENV=development で APP_ENV が未設定なら起動を止める（開発用の設定を本番扱いの値で有効にしない）" do
      _out, err, status = boot(production_boot_env("RAILS_ENV" => "development"), "require './config/environment'")

      expect(status).not_to be_success
      expect(err).to include("APP_ENV")
    end

    it "RAILS_ENV=test で APP_ENV が未設定なら、開発用の分岐を使わず、本番相当の HTTPS の設定にする" do
      out, err, status = boot(production_boot_env("RAILS_ENV" => "test"), inspect_boot_script)

      expect(status).to be_success, err
      expect(JSON.parse(out)).to include("env" => "test", "force_ssl" => true, "assume_ssl" => true)
    end

    it "APP_ENV が表記ゆれ（Development）なら、開発用の分岐を使わず、本番相当の設定にする" do
      out, err, status = boot(production_boot_env("APP_ENV" => "Development"), inspect_boot_script)

      expect(status).to be_success, err
      expect(JSON.parse(out)).to include(
        "env" => "production", "force_ssl" => true, "assume_ssl" => true, "enable_reloading" => false
      )
    end

    it "APP_ENV=development と RAILS_ENV=development がそろうと、開発用の設定になる（force_ssl=false・enable_reloading=true）" do
      env = production_boot_env(
        "RAILS_ENV" => "development",
        "APP_ENV" => "development",
        "APP_ORIGIN" => "http://localhost:8080"
      )
      out, err, status = boot(env, inspect_boot_script)

      expect(status).to be_success, err
      expect(JSON.parse(out)).to include(
        "env" => "development", "force_ssl" => false, "assume_ssl" => false,
        "enable_reloading" => true, "eager_load" => false
      )
    end
  end
end
