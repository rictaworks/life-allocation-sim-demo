# 1 年ごとの記録。配分は保存要求から、年次の値は再計算ホストの結果だけを保存する。(game_id, turn_no) は一意。
class CreateTurns < ActiveRecord::Migration[8.1]
  def change
    create_table :turns do |t|
      t.references :game, null: false, index: false, foreign_key: { on_delete: :cascade }
      t.references :session, null: false, foreign_key: { on_delete: :cascade }
      t.integer :turn_no, null: false
      t.integer :age, null: false
      t.integer :living_pct, null: false
      t.integer :saving_pct, null: false
      t.integer :invest_pct, null: false
      t.string :event_code
      t.bigint :savings_after, null: false
      t.bigint :invest_after, null: false
      t.integer :satisfaction_after, null: false
      t.datetime :created_at, null: false
    end

    add_index :turns, [ :game_id, :turn_no ], unique: true
  end
end
