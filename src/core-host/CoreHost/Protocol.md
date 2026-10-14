# 再計算ホスト（CoreHost）の入出力の形式

このファイルは、再計算ホスト（`dotnet CoreHost.dll`）の入出力を定義します。保存 API（Rails）と Domain Core の間の、JSON の形の正です。

ホストは規則を持ちません。配分の範囲・刻み・合計、年数の上限、難易度の一覧は Domain Core にだけあります。ホストは JSON の読み書き、Domain Core の呼び出し、終了コードだけを担います。

## 呼び出し方

- 新規ゲームの初期状態：`dotnet CoreHost.dll init`（標準入力に要求の JSON、標準出力に応答の JSON）
- 再計算：`dotnet CoreHost.dll replay`
- 入力は UTF-8 の JSON で、64KB（65,536 バイト）以下にします。これを超えたら、解析せずに終了コード 2 で拒否します。
- UTF-8 として正しくないバイト列を含む入力、対にならない代理文字（`\ud800` など）を含む文字列は、どの項目にあっても終了コード 2（`request_invalid`）で拒否します。
- 標準入力には要求の JSON を書き、書き終えたら閉じてください。標準入力を閉じた状態で起動すると、読み取りが終わらないことがあります。呼び出し側は、タイムアウトを設けてください。
- 応答は JSON の 1 つのオブジェクトです（末尾に改行は付けません）。同じ入力からは、バイト単位で同じ応答になります。
- 応答の文字列は JSON の既定のエスケープを使います（日本語は `\uXXXX` で書かれます）。

## 終了コード

| 終了コード | 意味 | 応答 |
|---|---|---|
| 0 | 成功 | 結果の JSON |
| 2 | 入力不正・規則違反 | `error`（理由コード）。配分とターンの違反は `turn_no` も付きます。 |
| 1 | 内部エラー | `error`（`internal_error` だけ）。例外の詳細は、標準出力に出しません。 |

内部エラー（終了コード 1）のとき、標準エラーには最初に起きた例外の型名だけを 1 行で出します（メッセージ・入力値・スタックトレースは出しません）。規則違反（終了コード 2）のときは、標準エラーに何も出しません。

標準入出力を開けなかったときは応答を書けないため、標準出力には何も出さずに終了コード 1 で終わります。このときも標準エラーには、例外の型名だけを出します。

応答の書き出しを始めたあとに失敗したときは、書きかけの出力に別の JSON を続けません。このときも標準エラーには、例外の型名だけを出します。標準出力に書けた内容は保証しないので、終了コードで判断してください。

## 要求

### init

```json
{ "difficulty": "<難易度コード>" }
```

- `difficulty`：難易度のコード（文字列）。一覧は Domain Core にあります。項目がない・null・空文字列は `difficulty_missing`、一覧にないコードは `difficulty_unknown` です。
- 応答：`{"difficulty": "<難易度コード>", "state": HOST_STATE}`

### replay

```json
{
  "seed": 12345,
  "difficulty": "<難易度コード>",
  "history": [
    { "living_pct": 60, "saving_pct": 20, "invest_pct": 20 }
  ]
}
```

- `seed`：0 以上 2^63−1 以下の整数です。Rails が生成して保存します。負の値・小数など整数でない値は `request_invalid` です。
- `difficulty`：`init` と同じです。
- `history`：配分の配列です（先頭が 1 年目）。各要素は整数の `living_pct`（生活費の %）・`saving_pct`（貯蓄の %）・`invest_pct`（投資の %）です。整数は 32 ビット符号付き整数（int32）の範囲で読みます。範囲を超える値は、整数でない値と同じく `request_invalid` で拒否します。値の範囲・刻み・合計は Domain Core が確かめます。
- 応答：`{"turns": [TURN...], "state": HOST_STATE, "evaluation": HOST_EVALUATION または null}`
  - 完走（65 歳）か破綻で終わっていれば `evaluation` があります。プレイ中なら null です。
  - 途中の不正な配分は、そのターンの `turn_no` と理由コードで拒否します（終了コード 2）。
  - 完走・破綻のあとのターンは `game_finished` で拒否します。`turn_no` は拒否されたターンの番号です。

## 応答の形

### HOST_STATE

```json
{
  "age": 22,
  "income_yen": 2800000,
  "savings_yen": 0,
  "investment_yen": 0,
  "satisfaction": 60,
  "price_index_bp": 10000,
  "status": "playing",
  "struggling": false,
  "raise_total_yen": 0,
  "occurred_once_events": []
}
```

- `status`：`playing`・`completed`・`bankrupt` のいずれかです。
- `struggling`：生活困窮なら true です（このとき `status` は `playing`）。Rails は `status` を GAME.status に保存し、生活困窮の表示には `struggling` を使います。
- `price_index_bp`：物価係数（万分率。10000 は 1.0 倍）です。
- `raise_total_yen`：手取り年収への加算の累計（円/年）です。
- `occurred_once_events`：発生済みの一度きりのイベントのコードの配列です。

### TURN

```json
{
  "turn_no": 1,
  "age": 22,
  "living_pct": 60,
  "saving_pct": 20,
  "invest_pct": 20,
  "event_code": null,
  "savings_after": 0,
  "invest_after": 0,
  "satisfaction_after": 60
}
```

- `turn_no`：1 始まりです。`age` はその年の年齢です。
- `event_code`：その年に発生したイベントのコードです。発生しなければ null です。
- `savings_after`・`invest_after`・`satisfaction_after`：その年の精算の後の貯蓄（円）・投資評価額（円）・満足度（点）です。

### HOST_EVALUATION

```json
{
  "total_assets_yen": 0,
  "average_satisfaction": 60,
  "is_bankrupt": false,
  "struggle_years": 0,
  "investment_sold_years": 0,
  "rank": "D",
  "comments": {
    "retirement": "（老後資金の観点の文）",
    "quality": "（生活の質の観点の文）",
    "preparedness": "（備えの観点の文）"
  }
}
```

- `rank`：S・A・B・C・D のいずれかです。
- `comments`：3 観点のコメントです。Domain Core の定型文を組み合わせた文です。

### エラー

```json
{ "error": { "code": "allocation_sum_mismatch", "message": "配分の合計を 100% にしてください。", "turn_no": 2 } }
```

- `code`：下の一覧のどれかです。`message` は利用者に出せる文言です。
- `turn_no`：規則違反が起きたターンの番号です。ターンに関係しないエラーには付きません。

## エラーのコード

| code | 終了コード | 意味 |
|---|---|---|
| input_too_large | 2 | 入力が 64KB を超えた |
| request_invalid | 2 | JSON の形式・型が正しくない（seed の範囲、history の欠落、UTF-8 の誤り、対にならない代理文字など） |
| command_unknown | 2 | 操作の指定が正しくない（init・replay 以外） |
| difficulty_missing | 2 | 難易度が未指定 |
| difficulty_unknown | 2 | 難易度が一覧にない |
| allocation_negative | 2 | 配分に負の値がある |
| allocation_step_mismatch | 2 | 配分が刻みに合わない |
| allocation_sum_mismatch | 2 | 配分の合計が 100 でない |
| game_finished | 2 | 完走・破綻のあとにターンを追加した |
| internal_error | 1 | 内部エラー |

## 変更の方針

- 名前（プロパティ・操作・コード）は、ホストの `Config`（`ProtocolNames`・`ProtocolCodes`）にだけ書きます。この文書とずれたら、両方を直します。
- 規則（範囲・刻み・合計・年数の上限）を変えるときは、Domain Core だけを変えます。ホストは変えません。
