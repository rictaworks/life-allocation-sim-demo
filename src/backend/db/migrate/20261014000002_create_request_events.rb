class CreateRequestEvents < ActiveRecord::Migration[8.1]
  def change
    create_table :request_events do |t|
      t.string :scope_digest, null: false
      t.string :kind, null: false
      t.datetime :created_at, null: false
    end

    add_index :request_events, :created_at
  end
end
