require "rails_helper"

# config/server.yml の値（既定値）と、環境変数による上書き。既定値はコードに書かず、設定ファイルの値を使う。
RSpec.describe ServerSettings do
  let(:unset_overrides) { { "RAILS_MAX_THREADS" => nil, "PORT" => nil, "RAILS_LOG_LEVEL" => nil } }

  describe "環境変数が無いとき（config/server.yml の値を使う）" do
    it "スレッド 3・DB の接続数 5・ポート 3000・ログの水準 info" do
      with_env(unset_overrides) do
        expect(described_class.puma_max_threads).to eq(3)
        expect(described_class.database_max_connections).to eq(5)
        expect(described_class.puma_port).to eq(3000)
        expect(described_class.log_level).to eq("info")
      end
    end

    it "設定ファイルに項目が無ければ例外にする（既定値では補わない）" do
      allow(described_class).to receive(:settings).and_return({ puma: {}, database: {}, log: {} })

      with_env(unset_overrides) do
        expect { described_class.puma_max_threads }.to raise_error(KeyError)
        expect { described_class.database_max_connections }.to raise_error(KeyError)
        expect { described_class.puma_port }.to raise_error(KeyError)
        expect { described_class.log_level }.to raise_error(KeyError)
      end
    end
  end

  describe "環境変数が設定されているとき（その値を使う。形の検査は読む側が行う）" do
    it "RAILS_MAX_THREADS は Puma と DB の両方、PORT・RAILS_LOG_LEVEL はそれぞれの値を使う" do
      with_env("RAILS_MAX_THREADS" => "7", "PORT" => "4567", "RAILS_LOG_LEVEL" => "debug") do
        expect(described_class.puma_max_threads).to eq("7")
        expect(described_class.database_max_connections).to eq("7")
        expect(described_class.puma_port).to eq("4567")
        expect(described_class.log_level).to eq("debug")
      end
    end

    it "空の値も設定されているものとして使う（既定値には戻さない。従来の ENV.fetch と同じ）" do
      with_env("RAILS_MAX_THREADS" => "") do
        expect(described_class.puma_max_threads).to eq("")
      end
    end
  end
end
