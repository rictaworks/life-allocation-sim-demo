# 1 セッション 1 件のゲーム。seed はサーバーが生成して保存する（保存要求では受け取らない）。
class CreateGames < ActiveRecord::Migration[8.1]
  def change
    create_table :games do |t|
      t.references :session, null: false, index: { unique: true }, foreign_key: { on_delete: :cascade }
      t.bigint :seed, null: false
      t.string :difficulty, null: false
      t.string :status, null: false
      t.integer :current_age, null: false
      t.timestamps
    end
  end
end
