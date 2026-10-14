# 保存 API の仕様

実装済みの API だけを書きます。経路は `src/backend/config/routes.rb`、頻度と大きさは `src/backend/config/api.yml`、エラーのコードは `src/backend/app/services/api_error.rb`、文言は `src/backend/config/locales/ja.yml` が正です。再計算ホストの入出力は `src/core-host/CoreHost/Protocol.md` が正です。

保存 API（`/api/*`）の要求と応答は JSON です（開発用サーバーを経由して同一オリジンで届きます）。`GET /up` は Rails の死活確認で、既定の応答は HTML です（`Accept: application/json` を付けると JSON）。

## 経路の一覧

| メソッドとパス | 内容 | 状態を変えるか |
|---|---|---|
| `GET /up` | 死活確認 | 変えない |
| `POST /api/session` | 匿名セッションの発行（有効な Cookie があれば何もしない） | Cookie を発行する |
| `GET /api/game` | 現在のゲームの取得（ゲームがあればホストで状態を再計算する。頻度は数えず、記録も残さない） | 変えない |
| `POST /api/games` | 新規ゲームの開始（既存のゲームは上書き） | 変える |
| `POST /api/games/turns` | 1 年ぶんの配分の保存 | 変える |

経路が無いときは 404（`not_found`）を返します。経路はあってもメソッドが違えば、同じく 404 です。

## 入口の検査（この順番。4 と 5 は、本体を読む POST だけ）

1. Host：`APP_ORIGIN` のホストと一致しなければ 403（`origin_forbidden`）。`/up` は対象外です。
2. Origin（POST だけ）：`APP_ORIGIN` と完全一致（スキーム・ホスト・ポート）しなければ 403（`origin_forbidden`）。Origin が無い場合も 403 です。
3. Content-Type：form（`application/x-www-form-urlencoded`）と multipart は、メソッドによらず 415。POST は `application/json` だけを受け付けます（`charset=utf-8` のパラメータは許可）。それ以外は 415（`unsupported_media_type`）。
4. 本体の大きさ：8192 バイトを超えたら 413（`payload_too_large`）。申告された Content-Length と、実際に読んだ長さの両方で検査します。
5. 本体の形：JSON のオブジェクトでなければ 400（`bad_request`）。空の本体も 400 です（`POST /api/session` も `{}` を送ります）。
6. 読むのは許可したキーだけです。未知のキーは無視します。JSON の本体は Rails の `params` に載せません。

## 応答の形

- 成功：各エンドポイントの応答を参照してください。
- エラー：`{"error":{"code":"<code>","message":"<文言>"}}`。`message` は `ja.yml` の `api_errors` から取ります。
- 429 のときは `Retry-After`（秒。1 以上の整数）を付けます。
- 例外・スタックトレース・SQL・内部のパスは応答に出しません。ログには、操作の名前・結果・例外のクラス名を出します。予期しない例外では、デバッグのためにバックトレースの先頭 30 行もログに出します（応答には出しません）。対応表にも内部エラーの一覧にも無い状態（想定していない状態）は、状態の数字と例外のクラス名をログに残します。Cookie・セッション ID・リクエストの本体・接続元の IP は出しません。再計算ホストの標準エラーは内容を出しません。型名の形（英字・下線で始まる英数字と下線。128 文字以内）のときだけ、型名を操作名と終了コードと一緒にログに残します。

## セッション（Cookie）

- 名前は `__Host-lasd_session`。`Secure`・`HttpOnly`・`SameSite=Lax`・`Path=/`・Domain なし・`Max-Age=604800`（7 日）です。
- 値は 32 バイトの乱数を URL 安全な Base64 にしたもの（43 文字）です。DB には SHA-256 のハッシュ（`sessions.token_digest`）だけを保存します。
- Cookie だけから読みます。URL・クエリ・本体からは読みません。
- 寿命は発行から 7 日です（利用では延びません）。期限切れのセッションは検索で使わず、03:00 の全データ削除で消えます。
- ゲームは、必ず現在のセッションに紐づくものだけを読みます。要求にゲームの ID は含まれないため、他のセッションのゲームを指定する方法はありません。現在のセッションにゲームが無いときは、`GET /api/game` は `null`、保存は 404（`game_not_found`）になります。

## 頻度と大きさ（`config/api.yml`）

| 対象 | 上限 | 超過 |
|---|---|---|
| 新規セッションの発行（`POST /api/session`。接続元の IP ごと） | 1 分 10 件 | 429 `rate_limited` |
| ゲームの開始（`POST /api/games`。セッションごと） | 1 秒 1 件・1 分 20 件 | 429 `rate_limited` |
| 保存（`POST /api/games/turns`。新規と再送を同じ枠で数えます。セッションごと） | 1 秒 1 件・1 分 20 件 | 429 `rate_limited` |
| 現在のゲームの取得（`GET /api/game`） | 上限は設けません（数えず、記録も残しません。ホストの同時実行の上限と制限時間で守ります） | 429 は返しません |
| 本体 | 8192 バイト | 413 `payload_too_large` |
| 再計算ホストへの入力 | 65,536 バイト（64KB） | 起動せずに拒否（`internal_error` の 500 になる） |
| 再計算ホストの出力 | 262,144 バイト | 読むのをやめてプロセスを止め、`internal_error`（500） |
| 再計算ホストの標準エラー | 4,096 バイト | 同上。内容は記録しません。型名の形（英字・下線で始まる英数字と下線。128 文字以内）のときだけ、型名を操作名と終了コードと一緒にログに残します |

- 頻度の確認は、再計算ホストを呼ぶ前に行います。超過したときは、ホストを呼びません。
- 接続元の IP は、信頼するプロキシ（Vercel の rewrites）が付けた `X-Real-IP` からだけ取ります。`X-Forwarded-For` などは見ません。この前提は、Vercel を経由せず Railway の URL に直接届く要求を防ぎません。その扱いは、デプロイの時点で決めます（PR 本文の確認事項）。
- `GET /api/game` は数えません。状態を変えない読み取りのため、request_events にも残さず、429 にもなりません。ゲームがあるときは毎回、再計算ホストを呼びます。
- 再計算ホストは同時に 2 件まで実行します。枠は 2 秒まで待ち、待ちきれなければ 503 です。1 回の実行は 5 秒を超えたら止めて 503 です。

## 数値の扱い

- `seed` は 0 以上 2^63−1 の整数です。JSON の数値として返します。JavaScript の `Number` では 2^53 を超える値の精度が落ちるため、Web は文字列か `BigInt` で扱ってください。
- 金額は円の整数、確率とリターンは万分率（10000 で 100%）の整数です。
- 配分（`living_pct`・`saving_pct`・`invest_pct`）は整数の百分率です。規則（0 以上・5 刻み・合計 100）は再計算ホストが検査します。Rails が検査するのは、キーの有無と整数であることだけです（[設計書との差](design-differences.md)）。

## 応答の部品

### GAME

```json
{
  "seed": 1234567890123456789,
  "difficulty": "normal",
  "status": "playing",
  "current_age": 23,
  "state": { "...HOST_STATE..." },
  "turns": [ { "...TURN..." } ],
  "evaluation": null
}
```

- `status`：`playing`・`completed`・`bankrupt` のどれかです。生活困窮は `status` が `playing` のまま、`state.struggling` が `true` で表します。
- `current_age`：次に迎える年齢です（開始直後は 22）。
- `state`：再計算ホストの HOST_STATE をそのまま返します。
- `turns`：再計算ホストの TURN の配列です。新規開始の直後は空の配列です。
- `evaluation`：完走または破綻のとき、HOST_EVALUATION。プレイ中は `null` です。

### TURN

`turn_no`・`age`・`living_pct`・`saving_pct`・`invest_pct`・`event_code`（発生しなければ `null`）・`savings_after`・`invest_after`・`satisfaction_after`。値はすべてサーバーの再計算の結果です。

### HOST_STATE・HOST_EVALUATION

Protocol.md の形のとおりです（`age`・`income_yen`・`savings_yen`・`investment_yen`・`satisfaction`・`price_index_bp`・`status`・`struggling`・`raise_total_yen`・`occurred_once_events`。評価は `total_assets_yen`・`average_satisfaction`・`is_bankrupt`・`struggle_years`・`investment_sold_years`・`rank`・`comments`）。

## エンドポイント

### GET /up

- 応答：200（例外があるときは 500）。状態は変えません。

### POST /api/session

- 要求：ヘッダー `X-Real-IP`（信頼するプロキシが付けた IP アドレス）、本体 `{}`。
- 処理：
  1. `X-Real-IP` が無い・形式が不正なら 400（セッションは発行しません）。
  2. 有効な Cookie があれば 200（何も発行しません）。
  3. IP ごとの頻度を確認し、超過なら 429。
  4. セッションを作り、Cookie を発行して 201。
- 応答：
  - 201：`{"session":{"status":"created"}}`（Set-Cookie あり）
  - 200：`{"session":{"status":"existing"}}`（Set-Cookie なし）
- エラー：400・403・413・415・429。

### GET /api/game

- 要求：Cookie（無くてもよい）。
- 処理：
  1. 有効なセッションが無ければ、`game` は `null` です。
  2. ゲームが無ければ `game` は `null` です。ゲームがあれば、保存された配分の履歴を再計算ホストの `replay` で読み直して返します。
  3. 頻度は数えず、状態も変えません（ゲーム・履歴・Cookie・request_events のどれも変わりません）。
- 応答：200 `{"game": null}` または 200 `{"game": GAME}`。
- エラー：403（Host が一致しない）・500・503。

### POST /api/games

- 要求：本体 `{"difficulty": "easy" | "normal" | "hard"}`。難易度の一覧は Rails に書きません（ホストと Domain Core が持ちます）。
- 処理（この順番。本体の大きさ（413）と JSON の形（400）は、入口の検査（上の 4・5）で、この処理より前に行います）：
  1. セッションが無ければ 401（`session_required`）。
  2. セッションごとの頻度を確認し、超過なら 429。
  3. 再計算ホストの `init` で難易度と初期状態を得ます。難易度が無い・空は `difficulty_missing`、一覧に無い（文字列以外を含む）は `difficulty_unknown`（どちらも 422）。
  4. seed をサーバーで生成する（0 以上 2^63−1。要求の値は使わない）。
  5. 旧ゲームと旧履歴を消し、新しいゲームを作る（1 つの transaction）。
- 応答：201 `{"game": GAME}`（`turns` は空、`evaluation` は `null`、`current_age` は 22）。
- エラー：400・401・403・413・415・422・429・500・503。

### POST /api/games/turns

- 要求：本体 `{"turn_no": N, "living_pct": L, "saving_pct": S, "invest_pct": I, "website": ""}`。
  - `turn_no`・`living_pct`・`saving_pct`・`invest_pct` は整数です。`website` は空文字列で送ります（ハニーポット。[設計書との差](design-differences.md)）。省略しても 400 にはなりません。
  - `turn_no`・`living_pct`・`saving_pct`・`invest_pct` のキーが無い、または整数でない（文字列・小数・真偽値・`null`）ときは 400 です。整数の大きさ（32 ビットの範囲の外を含む）は問いません。配分の範囲・刻み・合計は再計算ホストが検査し、違反は 422 です。`turn_no` の範囲も問いません（次の番号でなければ 409）。
- 処理（この順番。本体の大きさ（413）と JSON の形（400）は、入口の検査（上の 4・5）で、この処理より前に行います）：
  1. セッションが無ければ 401（`session_required`）。
  2. キーと配分の型を検査する（400）。キーが無い・整数でない（文字列・小数・真偽値・`null`）なら、400（`bad_request`）。
  3. セッションにゲームが無ければ 404（`game_not_found`）。
  4. ゲームが完走または破綻していれば 409（`game_finished`）。保存済みの番号の再送も、同じく 409 です。頻度は数えず、ホストも呼びません。終了の判定は保存された `status` で行います。
  5. `turn_no` が 1 から保存済みの件数までなら、保存済みの番号の再送として扱います。頻度を確認し（超過は 429）、同じ配分なら 200 で保存済みの TURN と現在の GAME を返します。違う配分なら 409（`turn_allocation_mismatch`）。配分の規則は検査しません（規則違反の配分も、保存済みと違えば 409）。
  6. `turn_no` が次の番号でなければ 409（`turn_conflict`）。
  7. セッションごとの頻度を確認し、超過なら 429（ホストは呼ばない）。
  8. 再計算ホストの `replay` で、保存済みの配分の後ろに今回の配分を足して検査します。規則に違反すれば 422（`allocation_negative`・`allocation_step_mismatch`・`allocation_sum_mismatch`）。
  9. `website` に空文字列と `null` 以外の値があれば、通常どおり 201 の応答を返すが、保存しない（ハニーポット）。
  10. 保存する（1 つの transaction。TURN を 1 件追加し、ゲームの `status` と `current_age` を更新します）。201 を返します。
  11. 同じ番号が同時に保存されていた場合（一意制約）は、先の保存を見直します。その保存でゲームが終わっていれば 409（`game_finished`）。そうでなければ、5 と同じく扱います（同じ配分なら 200、違えば 409）。
- 応答：
  - 201 `{"turn": TURN, "game": GAME}`：新規の保存。
  - 200 `{"turn": TURN, "game": GAME}`：保存済みの番号を同じ配分で再送しました（ゲームは終了していないとき）。
- エラー：400・401・403・404・409・413・415・422・429・500・503。

## エラーのコード

| code | HTTP | 主に返す経路 | 意味 |
|---|---|---|---|
| `bad_request` | 400 | すべての POST・`POST /api/session` | 形式が正しくない（JSON・キー・整数・`X-Real-IP`） |
| `session_required` | 401 | `POST /api/games`・`POST /api/games/turns` | セッションが無い、または期限切れ |
| `origin_forbidden` | 403 | Host（`/up` 以外のすべての経路）・POST（Origin） | Host・Origin が `APP_ORIGIN` と一致しない |
| `not_found` | 404 | 経路が無いとき | 経路が無い（メソッドが違う場合を含む） |
| `game_not_found` | 404 | `POST /api/games/turns` | セッションにゲームが無い |
| `game_finished` | 409 | `POST /api/games/turns` | 完走・破綻のあとに、新しい番号を保存しようとした、または保存済みの番号を再送した |
| `turn_conflict` | 409 | `POST /api/games/turns` | 番号が次の番号でない（飛び番・範囲外） |
| `turn_allocation_mismatch` | 409 | `POST /api/games/turns` | 保存済みの番号を、別の配分で送った |
| `payload_too_large` | 413 | すべての POST | 本体が 8192 バイトを超えた |
| `unsupported_media_type` | 415 | すべての経路（form・multipart）・POST | Content-Type が JSON でない |
| `difficulty_missing` | 422 | `POST /api/games` | 難易度が無い・空 |
| `difficulty_unknown` | 422 | `POST /api/games` | 難易度が一覧に無い（文字列以外を含む） |
| `allocation_negative` | 422 | `POST /api/games/turns` | 配分に負の値がある |
| `allocation_step_mismatch` | 422 | `POST /api/games/turns` | 配分が 5 の倍数でない |
| `allocation_sum_mismatch` | 422 | `POST /api/games/turns` | 配分の合計が 100 でない |
| `rate_limited` | 429 | 頻度の上限があるすべての経路 | 頻度の上限を超えた（`Retry-After` あり） |
| `internal_error` | 500 | すべての経路 | 内部エラー。ホストの異常終了・出力の形の不一致・出力や標準エラーの上限の超過・想定外の要求を含む（詳細は出さない） |
| `service_unavailable` | 503 | `GET /api/game`・`POST /api/games`・`POST /api/games/turns` | 再計算ホストが混雑している、または制限時間を超えた |

### 再計算ホストのエラーとの対応

- 終了コード 2 の理由コードのうち、`difficulty_missing`・`difficulty_unknown`・`allocation_negative`・`allocation_step_mismatch`・`allocation_sum_mismatch`・`game_finished` は、同じ名前の API のエラーにします（`config/core_host.yml` の `rule_violation_codes`）。
- それ以外の理由コード（`request_invalid` など）は、Rails が作った要求の不具合として `internal_error`（500）にします。
- 終了コード 1 と、出力の形が違う場合も `internal_error` です。
- 制限時間の超過と、枠の待ち時間の超過は `service_unavailable`（503）です。
