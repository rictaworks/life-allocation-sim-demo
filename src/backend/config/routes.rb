Rails.application.routes.draw do
  # 死活確認。200 を返すだけで、状態を変えない。
  get "up" => "rails/health#show", as: :rails_health_check

  # 状態を変えるのは POST だけ。GET は状態を読むだけ（GET /api/game は頻度の記録だけを書く）。
  namespace :api do
    # 匿名セッションの発行。
    resource :session, only: :create
    # 新規ゲームの開始（既存は上書き）。
    resources :games, only: :create
    # 現在のゲームの取得。
    resource :game, only: :show, controller: "game"
    # 1 年ぶんの配分の保存（既存の番号の同じ配分の再送は保存済みの結果）。
    post "games/turns" => "turns#create"
  end
end
