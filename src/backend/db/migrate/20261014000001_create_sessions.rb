class CreateSessions < ActiveRecord::Migration[8.1]
  def change
    create_table :sessions do |t|
      t.string :token_digest, null: false
      t.datetime :created_at, null: false
      t.datetime :expires_at, null: false
    end

    add_index :sessions, :token_digest, unique: true
  end
end
