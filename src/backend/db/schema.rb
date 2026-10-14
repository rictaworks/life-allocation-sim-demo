# This file is auto-generated from the current state of the database. Instead
# of editing this file, please use the migrations feature of Active Record to
# incrementally modify your database, and then regenerate this schema definition.
#
# This file is the source Rails uses to define your schema when running `bin/rails
# db:schema:load`. When creating a new database, `bin/rails db:schema:load` tends to
# be faster and is potentially less error prone than running all of your
# migrations from scratch. Old migrations may fail to apply correctly if those
# migrations use external dependencies or application code.
#
# It's strongly recommended that you check this file into your version control system.

ActiveRecord::Schema[8.1].define(version: 2026_10_14_000004) do
  create_table "games", force: :cascade do |t|
    t.integer "session_id", null: false
    t.bigint "seed", null: false
    t.string "difficulty", null: false
    t.string "status", null: false
    t.integer "current_age", null: false
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.index ["session_id"], name: "index_games_on_session_id", unique: true
  end

  create_table "request_events", force: :cascade do |t|
    t.string "scope_digest", null: false
    t.string "kind", null: false
    t.datetime "created_at", null: false
    t.index ["created_at"], name: "index_request_events_on_created_at"
  end

  create_table "sessions", force: :cascade do |t|
    t.string "token_digest", null: false
    t.datetime "created_at", null: false
    t.datetime "expires_at", null: false
    t.index ["token_digest"], name: "index_sessions_on_token_digest", unique: true
  end

  create_table "turns", force: :cascade do |t|
    t.integer "game_id", null: false
    t.integer "session_id", null: false
    t.integer "turn_no", null: false
    t.integer "age", null: false
    t.integer "living_pct", null: false
    t.integer "saving_pct", null: false
    t.integer "invest_pct", null: false
    t.string "event_code"
    t.bigint "savings_after", null: false
    t.bigint "invest_after", null: false
    t.integer "satisfaction_after", null: false
    t.datetime "created_at", null: false
    t.index ["game_id", "turn_no"], name: "index_turns_on_game_id_and_turn_no", unique: true
    t.index ["session_id"], name: "index_turns_on_session_id"
  end

  add_foreign_key "games", "sessions", on_delete: :cascade
  add_foreign_key "turns", "games", on_delete: :cascade
  add_foreign_key "turns", "sessions", on_delete: :cascade
end
