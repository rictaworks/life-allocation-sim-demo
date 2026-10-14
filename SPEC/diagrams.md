# 図（実装に合わせた版）

設計書（`requirements.md` の 7〜12 章）の図を、実装のコードと照らして書き直したものです。違いは [設計書との差](design-differences.md) に書きます。図は Mermaid で書いています（GitHub で表示されます）。

## ER 図

テーブルは `src/backend/db/schema.rb` が正です。マスタ（ライフステージ・イベント・難易度・ランク）は Domain Core の定数で、DB には置きません。

```mermaid
erDiagram
    SESSIONS ||--o| GAMES : "1 件まで持つ"
    GAMES ||--o{ TURNS : "年ごとに持つ"
    SESSIONS ||--o{ TURNS : "オーナーキー"
    SESSIONS {
        integer id PK
        string token_digest "Cookie の値の SHA-256（一意）"
        datetime created_at
        datetime expires_at
    }
    GAMES {
        integer id PK
        integer session_id FK "一意（1 セッション 1 件）"
        bigint seed "サーバーが生成（0〜2^63-1）"
        string difficulty
        string status "playing・completed・bankrupt"
        integer current_age
        datetime created_at
        datetime updated_at
    }
    TURNS {
        integer id PK
        integer game_id FK "game_id と turn_no の組で一意"
        integer session_id FK
        integer turn_no
        integer age
        integer living_pct
        integer saving_pct
        integer invest_pct
        string event_code "発生しなければ NULL"
        bigint savings_after
        bigint invest_after
        integer satisfaction_after
        datetime created_at
    }
    REQUEST_EVENTS {
        integer id PK
        string kind "session_issue・game_start・turn_save"
        string scope_digest "HMAC のダイジェスト（IP やトークンは保存しない）"
        datetime created_at
    }
```

## DFD（データの流れ）

```mermaid
flowchart LR
    P([プレイヤー]) -->|配分を入力| U[Unity 表示・入力]
    U -->|計算| DC[Domain Core DLL]
    DC -->|新しい状態と内訳| U
    U -->|今年の配分と turn_no| R[Rails 保存 API]
    R -->|配分の履歴と seed| H[再計算ホスト CoreHost]
    H -->|再計算の結果| R
    R -->|保存| D[(SQLite)]
    D -->|保存済みの履歴| R
    R -->|GAME と TURN| U
    C[Railway の cron 毎日 UTC 18:00] -->|bin/rails lasd:reset_all| D
```

- 図の「配分の履歴と seed」は、サーバーの中（Rails から再計算ホストへ）の受け渡しです。ブラウザからは seed を送りません。
- 画面が受け取るのは、サーバーで再計算した値だけです。
- Railway の cron は、デプロイ時に設定する手順です（未確認）。図の cron は、その手順の流れを示します。

## シーケンス図（開始と再開）

```mermaid
sequenceDiagram
    participant U as Unity
    participant R as Rails
    participant H as 再計算ホスト
    participant D as SQLite
    U->>R: POST /api/session
    R->>D: セッションを作る（ハッシュだけを保存）
    R-->>U: 201 と Set-Cookie
    U->>R: POST /api/games（難易度）
    R->>H: init（難易度）
    H-->>R: 初期状態
    R->>D: 旧ゲームと旧履歴を消して、新しいゲームを作ります
    R-->>U: 201 GAME
    Note over U,R: 再読み込みのあとは GET /api/game で再開します
    U->>R: GET /api/game
    R->>D: 有効なセッションがあれば頻度を記録（超過は 429。ホストは呼ばない）
    R->>D: 現在のセッションのゲームと履歴を読む
    R->>H: replay（seed・難易度・履歴）
    H-->>R: 状態・TURN・評価
    R-->>U: 200 GAME
```

## シーケンス図（1 年の保存）

```mermaid
sequenceDiagram
    actor P as プレイヤー
    participant U as Unity
    participant DC as Domain Core DLL
    participant R as Rails
    participant H as 再計算ホスト
    participant D as SQLite
    P->>U: 配分を決めて次の年へ
    U->>DC: 検証と精算
    DC-->>U: 新しい状態と内訳
    U->>P: 年次結果を表示
    U->>R: POST /api/games/turns（turn_no と 3 項目と website）
    R->>R: セッション・本体・頻度の確認
    R->>H: replay（保存済みの配分と今年の配分）
    H-->>R: TURN・状態・評価（規則違反は 422）
    R->>D: TURN を追加し、status と current_age を更新
    R-->>U: 201 TURN と GAME
    Note over U: TURN の残高が DLL の結果と一致したときだけ次の年へ進みます
```

- 同じ番号・同じ配分の再送は、保存せず 200 で保存済みの結果を返します（ゲームが終了していないとき）。
- 配分の規則は、ホストが検査します。Rails は規則を持ちません。

## クラス図（Domain Core）

```mermaid
classDiagram
    class Simulator {
        +ValidateAllocation(allocation)
        +ValidateDifficultyCode(code)
    }
    class NewGame {
        +Start(difficultyCode)
    }
    class YearSettlement {
        +Advance(state, allocation, difficulty, random)
    }
    class EventDrawer {
        +Draw(state, random)
    }
    class Replayer {
        +Replay(start, seed, history)
    }
    class Evaluator {
        +Evaluate(finalState, history)
    }
    class AllocationAdjuster {
        +Move(item, newPct)
    }
    class SeededRandom {
        +NextUInt64()
        +NextInt(exclusiveMax)
    }
    class Allocation {
        +int LivingPct
        +int SavingPct
        +int InvestPct
    }
    class PlayerState {
        +int Age
        +long IncomeYen
        +long SavingsYen
        +long InvestmentYen
        +int Satisfaction
        +int PriceIndexBp
        +PlayerStatus Status
        +long RaiseTotalYen
        +bool IsFinished
    }
    class YearResult {
        +PlayerState Next
        +long UnmetYen
        +long MarketReturnBp
    }
    class LifeEvent {
        +string Code
        +LifeEventEffect Effect
        +int MinAge
        +int MaxAge
        +bool OccursOnce
        +int ProbabilityBp
        +long AmountYen
        +int MarketReturnBp
    }
    class Difficulty {
        +string Code
        +int MeanReturnBp
        +int VolatilityBp
    }
    class LifeStage {
        +int MinAge
        +int MaxAge
        +long TakeHomeIncomeYen
        +long BaseLivingCostYen
        +ForAge(age)
    }
    class Rank {
        +string Code
        +long MinTotalAssetsYen
        +int MinAverageSatisfaction
    }
    class Evaluation {
        +long TotalAssetsYen
        +int AverageSatisfaction
        +bool IsBankrupt
        +Rank Rank
    }
    NewGame ..> Simulator
    YearSettlement ..> Simulator
    YearSettlement ..> EventDrawer
    YearSettlement ..> SeededRandom
    YearSettlement ..> Difficulty
    YearSettlement ..> LifeStage
    YearSettlement --> YearResult
    YearResult --> PlayerState
    YearResult --> LifeEvent
    EventDrawer ..> LifeEvent
    Replayer ..> YearSettlement
    Replayer ..> Evaluator
    Evaluator --> Evaluation
    Evaluation --> Rank
    AllocationAdjuster ..> Allocation
    Simulator ..> Allocation
```

## クラス図（保存 API）

```mermaid
classDiagram
    class SessionsController {
        +create()
    }
    class GamesController {
        +create()
    }
    class GameController {
        +show()
    }
    class TurnsController {
        +create()
    }
    class GameStarter {
        +call(owner, difficulty)
    }
    class TurnSaver {
        +call(session, turn_request)
    }
    class GameSnapshot {
        +call(game)
    }
    class TurnRequest {
        +from_params(params)
    }
    class CoreHostClient {
        +init(difficulty)
        +replay(seed, difficulty, history)
    }
    class CoreHostProcess {
        +run(argv, env, input, timeout, max_output_bytes, max_stderr_bytes)
    }
    class RequestRateLimit {
        +consume!(kind, key)
        +perform(kind, key)
    }
    class ApiError {
        +code
        +status
        +body()
    }
    class DataReset {
        +call()
    }
    class Session {
        +issue!()
        +find_active(token)
    }
    class Game {
        +playing?()
    }
    class Turn {
        +allocation()
    }
    SessionsController ..> RequestRateLimit
    SessionsController ..> Session
    GamesController ..> GameStarter
    GamesController ..> RequestRateLimit
    GameController ..> GameSnapshot
    GameController ..> RequestRateLimit
    TurnsController ..> TurnSaver
    TurnsController ..> TurnRequest
    TurnSaver ..> TurnRequest
    TurnSaver ..> GameSnapshot
    TurnSaver ..> RequestRateLimit
    TurnSaver ..> CoreHostClient
    GameStarter ..> CoreHostClient
    GameSnapshot ..> CoreHostClient
    CoreHostClient ..> CoreHostProcess
    CoreHostClient ..> ApiError
    DataReset ..> Turn
    DataReset ..> Game
    DataReset ..> Session
    Session "1" --> "0..1" Game
    Game "1" --> "0..*" Turn
```

## 状態遷移図（ゲームの状態）

状態は `games.status` と、再計算ホストの `state.status`・`state.struggling` で決まります。「生活困窮」は `status` が `playing` のまま、`struggling` が `true` の状態です。

```mermaid
stateDiagram-v2
    [*] --> ゲームなし
    ゲームなし --> プレイ中 : 新規開始
    プレイ中 --> 生活困窮 : 精算後の満足度が 20 未満
    生活困窮 --> プレイ中 : 満足度が 20 以上に戻る
    プレイ中 --> 完走 : 次の年齢が 65 以上
    生活困窮 --> 完走 : 次の年齢が 65 以上
    プレイ中 --> 破綻 : 取り崩し後も不足が残る
    生活困窮 --> 破綻 : 取り崩し後も不足が残る
    完走 --> プレイ中 : 新規開始（旧ゲームは消える）
    破綻 --> プレイ中 : 新規開始（旧ゲームは消える）
    プレイ中 --> ゲームなし : 03:00 の全データ削除
    生活困窮 --> ゲームなし : 03:00 の全データ削除
    完走 --> ゲームなし : 03:00 の全データ削除
    破綻 --> ゲームなし : 03:00 の全データ削除
    完走 --> [*]
    破綻 --> [*]
```

- 完走と破綻のあとは、新しい番号の保存も、保存済みの番号の再送も、409（`game_finished`）で断ります。
- 03:00 の全データ削除では、セッションも消えます。その後の要求は、セッションが無いものとして扱われます。

## ユースケース図

```mermaid
flowchart LR
    P([プレイヤー])
    C([Railway の cron])
    subgraph システム
        U1(難易度を選んで新規開始する)
        U2(続きから再開する)
        U3(配分を決めて次の年へ進める)
        U4(年次の結果とイベントを見る)
        U5(資産推移を見る)
        U6(最終評価を見る)
        U7(全データを削除する)
    end
    P --- U1
    P --- U2
    P --- U3
    P --- U4
    P --- U5
    P --- U6
    C --- U7
```
