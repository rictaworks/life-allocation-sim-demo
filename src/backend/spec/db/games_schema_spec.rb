require "rails_helper"

RSpec.describe "games と turns のスキーマ" do
  let(:connection) { ActiveRecord::Base.connection }

  def column_names(table)
    connection.columns(table).map(&:name)
  end

  it "games は session_id を一意にし、sessions を参照する（1 セッション 1 件）" do
    expect(connection.index_exists?(:games, :session_id, unique: true)).to be(true)
    expect(connection.foreign_keys(:games).map(&:to_table)).to include("sessions")
  end

  it "games は seed・difficulty・status・current_age・created_at・updated_at を持つ" do
    expect(column_names(:games)).to include(
      "session_id", "seed", "difficulty", "status", "current_age", "created_at", "updated_at"
    )
  end

  it "turns は (game_id, turn_no) を一意にし、games と sessions を参照する" do
    expect(connection.index_exists?(:turns, %w[game_id turn_no], unique: true)).to be(true)
    expect(connection.foreign_keys(:turns).map(&:to_table)).to contain_exactly("games", "sessions")
  end

  it "turns は年次の値と作成日時を持つ（updated_at は持たない）" do
    expect(column_names(:turns)).to include(
      "game_id", "session_id", "turn_no", "age", "living_pct", "saving_pct", "invest_pct", "event_code",
      "savings_after", "invest_after", "satisfaction_after", "created_at"
    )
    expect(column_names(:turns)).not_to include("updated_at")
  end
end
