# life-allocation-sim-demo

給料を「生活費・貯蓄・投資」に配分し、22歳から65歳までの資産推移を体験する人生設計シミュレーション（デモ版）。

- 目的：配分の比率の違いが数十年後の資産と生活の安定にどう効くかを、短い時間のプレイで体感してもらう。
- 注意：利回り・イベント・物価はすべて架空のモデル値。実在の金融商品や個別の投資判断を示すものではない。
- 認証：なし。個人情報は入力しない。毎日 JST 03:00 に全データを削除する。

## 仕様

- 設計書：[requirements.md](requirements.md)
- 実装済みの仕様：[SPEC/](SPEC/)（未実装のものは書かない）

## ページ一覧・API 一覧

実装したものから順に追記する。未実装のものは書かない。

## 開発

開発の手順と規定は [CLAUDE.md](CLAUDE.md) にある。

### 構成

| 場所 | 中身 |
|---|---|
| `src/core/DomainCore/` | Domain Core（netstandard2.1・C# 9。Unity で使える条件に合わせて固定） |
| `src/core/DomainCore.Tests/` | Domain Core のテスト（xUnit.net v3・net8.0） |
| `src/backend/` | 保存 API（Rails 8.1 の API mode・SQLite・RSpec） |
| `test/` | E2E テスト（Playwright）の設定 |
| `.github/workflows/ci.yml` | CI。pull_request と main への push で、core（.NET）と backend（Rails）のテストを実行する |

### 必要なもの

- .NET 8 SDK
- Ruby 3.4（`src/backend/.ruby-version`）
- Node.js 20 以上（npm）

### Domain Core のテスト

```bash
dotnet test src/core/DomainCore.Tests
```

### 保存 API の準備とテスト

```bash
cd src/backend
bundle install
RAILS_ENV=test bin/rails db:prepare
bundle exec rspec
```

### E2E テストの準備

```bash
cd test
npm ci
```

`test/` には Playwright の設定だけを置いている。テストはまだ無い。

### 環境変数

- 名前の一覧は [.env.example](.env.example) にある。値はコミットしない。
- Rails は development・test のときだけ `src/backend/.env` を読む（dotenv-rails）。本番は `.env` を読まず、環境変数で渡す。

### 環境名の規定（APP_ENV）

- `development` のときだけ、開発用の分岐にする。
- 未設定・不明な値は、本番として扱う（fail closed）。
- Cookie の Secure・レート制限・CORS は、環境によって緩めない。
- この規定に沿った分岐は、まだ実装していない。
