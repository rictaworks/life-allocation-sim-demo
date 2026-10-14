require "rails_helper"
require "rake"

RSpec.describe "rake lasd:reset_all（毎日 JST 03:00 の全データ削除）" do
  let(:task) { Rake::Task["lasd:reset_all"] }
  let(:now) { Time.utc(2026, 10, 14, 12, 0, 0) }

  before(:all) do
    Rails.application.load_tasks unless Rake::Task.task_defined?("lasd:reset_all")
  end

  # 4 つの表に行を作る（セッション 2・ゲーム 1・ターン 1・要求の記録 2）。
  def seed_demo_rows
    session = Session.create!(token_digest: "digest-of-a-session", created_at: now, expires_at: now + 7.days)
    Session.create!(token_digest: "digest-of-another-session", created_at: now, expires_at: now + 7.days)
    game = Game.create!(session: session, seed: 1, difficulty: "normal", status: "playing", current_age: 22)
    Turn.create!(game: game, session: session, turn_no: 1, age: 22, living_pct: 60, saving_pct: 20, invest_pct: 20,
                 savings_after: 1, invest_after: 2, satisfaction_after: 3)
    RequestEvent.create!(scope_digest: "digest-a", kind: "turn_save", created_at: now)
    RequestEvent.create!(scope_digest: "digest-b", kind: "session_issue", created_at: now)
  end

  def run_task
    task.reenable
    task.invoke
  end

  def counts
    { sessions: Session.count, games: Game.count, turns: Turn.count, request_events: RequestEvent.count }
  end

  def summary(**numbers)
    I18n.t("reset_all.summary", **numbers, locale: :ja, raise: true)
  end

  def row_count(connection, table)
    connection.select_value("SELECT COUNT(*) FROM #{connection.quote_table_name(table)}")
  end

  it "sessions・games・turns・request_events の全行を削除する" do
    seed_demo_rows
    expect(counts).to eq(sessions: 2, games: 1, turns: 1, request_events: 2)

    expect { run_task }.to output.to_stdout

    expect(counts).to eq(sessions: 0, games: 0, turns: 0, request_events: 0)
  end

  it "削除した件数だけを標準出力に出す（ID・Cookie・データの中身は出さない）" do
    seed_demo_rows

    expect { run_task }.to output("#{summary(sessions: 2, games: 1, turns: 1, request_events: 2)}\n").to_stdout
  end

  it "他のテーブル（Rails の内部の表を含む）の行数は変えない" do
    seed_demo_rows
    connection = ActiveRecord::Base.connection
    other_tables = connection.tables.reject { |table| table.start_with?("sqlite_") } - %w[sessions games turns request_events]
    expect(other_tables).to include("schema_migrations")
    before_counts = other_tables.to_h { |table| [ table, row_count(connection, table) ] }

    expect { run_task }.to output.to_stdout

    expect(other_tables.to_h { |table| [ table, row_count(connection, table) ] }).to eq(before_counts)
  end

  it "2 回実行しても失敗しない（2 回目は 0 件）" do
    seed_demo_rows
    expect { run_task }.to output.to_stdout

    expect { run_task }.to output("#{summary(sessions: 0, games: 0, turns: 0, request_events: 0)}\n").to_stdout

    expect(counts).to eq(sessions: 0, games: 0, turns: 0, request_events: 0)
  end

  it "途中で失敗したら、どの表の行も消えない（1 つのトランザクション）" do
    seed_demo_rows
    allow(RequestEvent).to receive(:delete_all).and_raise(ActiveRecord::StatementInvalid, "database is locked")

    expect { run_task }.to raise_error(ActiveRecord::StatementInvalid)

    expect(counts).to eq(sessions: 2, games: 1, turns: 1, request_events: 2)
  end

  it "rake の入口は Rails の環境（:environment）を読み込んでから動く" do
    expect(task.prerequisites).to include("environment")
  end
end
