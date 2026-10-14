require "rails_helper"

RSpec.describe "RequestRateLimit（件数を DB で数える制限）" do
  let(:start) { Time.utc(2026, 10, 14, 12, 0, 0) }
  let(:client) { "203.0.113.10" }

  def attempt(kind: :turn_save, key: client)
    RequestRateLimit.perform(kind: kind, key: key) { :saved }
  end

  describe "1 秒 1 件（turn_save の 1 秒の規則）" do
    it "1 秒以内の 2 回目は超過（429）。Retry-After は残りの秒数（1 以上）" do
      travel_to(start) { expect(attempt).to eq(:saved) }

      travel_to(start + 0.3, with_usec: true) do
        expect { attempt }.to raise_error(ApiError) { |error|
          expect(error.code).to eq(:rate_limited)
          expect(error.retry_after).to eq(1)
        }
      end
    end

    it "ちょうど 1 秒後は保存できる（境界）" do
      travel_to(start) { attempt }

      travel_to(start + 1) { expect(attempt).to eq(:saved) }
    end

    it "0.999 秒後はまだ保存できない（境界の手前）" do
      travel_to(start) { attempt }

      travel_to(start + 0.999, with_usec: true) do
        expect { attempt }.to raise_error(ApiError) { |error| expect(error.code).to eq(:rate_limited) }
      end
    end
  end

  describe "1 分 20 件（turn_save の 1 分の規則）" do
    it "3 秒おきの 20 件は通り、21 件目は超過。Retry-After は最も古い記録が外れるまでの秒数" do
      20.times { |index| travel_to(start + (index * 3)) { attempt } }

      travel_to(start + 58) do
        expect { attempt }.to raise_error(ApiError) { |error| expect(error.retry_after).to eq(2) }
      end
    end

    it "59.5 秒後はまだ超過（Retry-After は 1）。60 秒後は最も古い記録が外れて保存できる（境界）" do
      20.times { |index| travel_to(start + (index * 3)) { attempt } }

      travel_to(start + 59.5, with_usec: true) do
        expect { attempt }.to raise_error(ApiError) { |error| expect(error.retry_after).to eq(1) }
      end
      travel_to(start + 60) { expect(attempt).to eq(:saved) }
    end
  end

  describe "件数は DB で数える（プロセス内の状態に頼らない）" do
    it "別のプロセスが記録した行（DB の行）も数える" do
      digest = RequestRateLimit.digest_for(:turn_save, client)
      20.times do |index|
        RequestEvent.create!(kind: "turn_save", scope_digest: digest, created_at: start + (index * 3))
      end

      travel_to(start + 58) do
        expect { attempt }.to raise_error(ApiError) { |error| expect(error.code).to eq(:rate_limited) }
      end
    end
  end

  describe "古い行は記録と同じ処理の中で削除する" do
    it "種類ごとの最長の期間より古い行は、次の記録のときに消え、別の種類の行は消えない" do
      RequestEvent.create!(kind: "session_issue", scope_digest: "other", created_at: start - 3600)
      travel_to(start) { attempt }

      travel_to(start + 61) { attempt }

      expect(RequestEvent.where(kind: "turn_save").pluck(:created_at)).to eq([ start + 61 ])
      expect(RequestEvent.where(kind: "session_issue").count).to eq(1)
    end
  end

  describe "スコープと種類" do
    it "別のスコープ（別のセッション・別の IP）は互いに影響しない" do
      travel_to(start) do
        expect(attempt(key: "session-a")).to eq(:saved)
        expect(attempt(key: "session-b")).to eq(:saved)
      end
    end

    it "種類が違えば別に数える" do
      travel_to(start) do
        expect(attempt(kind: :session_issue, key: client)).to eq(:saved)
        expect(attempt(kind: :turn_save, key: client)).to eq(:saved)
      end
    end

    it "定義されていない種類は例外にする（既定値で補わない）" do
      expect { attempt(kind: :unknown_kind) }.to raise_error(KeyError)
    end
  end

  describe "判定と記録は同じトランザクション" do
    it "ブロックが例外になったら記録しない" do
      expect { RequestRateLimit.perform(kind: :turn_save, key: client) { raise "boom" } }
        .to raise_error(RuntimeError, "boom")

      expect(RequestEvent.count).to eq(0)
    end

    it "超過した要求は記録しない（数えない）" do
      travel_to(start) { attempt }
      travel_to(start + 0.5, with_usec: true) { expect { attempt }.to raise_error(ApiError) }

      expect(RequestEvent.count).to eq(1)
    end
  end

  describe "IP を保存しない（HMAC-SHA256 のダイジェストだけを保存する）" do
    it "保存するのは SECRET_KEY_BASE から導いた鍵の HMAC-SHA256。IP の文字列は保存しない" do
      attempt(kind: :session_issue, key: client)

      row = RequestEvent.sole
      key = Rails.application.key_generator.generate_key(ApiSettings.request_digest_salt, 32)
      expect(row.scope_digest).to match(/\A\h{64}\z/)
      expect(row.scope_digest).to eq(OpenSSL::HMAC.hexdigest("SHA256", key, "session_issue:#{client}"))
      expect(RequestEvent.all.flat_map { |record| record.attributes.values.map(&:to_s) }).not_to include(client)
    end

    it "request_events の列は id・scope_digest・kind・created_at だけで、created_at に索引がある" do
      expect(RequestEvent.column_names).to contain_exactly("id", "scope_digest", "kind", "created_at")
      expect(RequestEvent.connection.indexes(:request_events).map(&:columns)).to include([ "created_at" ])
    end
  end
end
