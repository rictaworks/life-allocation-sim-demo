require "rails_helper"

RSpec.describe "ログに記録しない値（filter_parameters）" do
  let(:filter) { ActiveSupport::ParameterFilter.new(Rails.application.config.filter_parameters) }

  it "Cookie・セッション・トークンの値は [FILTERED] に置き換える" do
    filtered = filter.filter(
      "__Host-lasd_session" => "session-value",
      "session_id" => "1",
      "cookie" => "name=value",
      "token_digest" => "digest-value"
    )

    expect(filtered.values).to all(eq("[FILTERED]"))
  end

  it "本体の値はキーに関係なく [FILTERED] にする（文字列・数値・空・ネストした値のすべて。Rack が読んだ値も）" do
    filtered = filter.filter(
      "difficulty" => "MARKER_PLAIN",
      "turn_no" => 2,
      "nested" => { "any_key" => [ "MARKER_NESTED" ] },
      "empty" => nil,
      "" => "MARKER_EMPTY_KEY"
    )

    expect(filtered).to eq(
      "difficulty" => "[FILTERED]",
      "turn_no" => "[FILTERED]",
      "nested" => "[FILTERED]",
      "empty" => "[FILTERED]",
      "" => "[FILTERED]"
    )
    expect(filtered.to_s).not_to include("MARKER")
  end
end
