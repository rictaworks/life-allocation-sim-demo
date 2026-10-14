# life-allocation-sim-demo

給料を「生活費・貯蓄・投資」に配分し、22歳から65歳までの資産推移を体験する人生設計シミュレーション（デモ版）です。

- 目的：配分の比率の違いが数十年後の資産と生活の安定にどう効くかを、短い時間のプレイで体感していただくことです。
- 注意：利回り・イベント・物価はすべて架空のモデル値です。実在の金融商品や個別の投資判断を示すものではありません。
- 認証：なし。個人情報は入力しません。毎日 JST 03:00 に全データを削除します（Railway の cron の設定は、デプロイ時に確認します。[運用](#運用) の節）。

## 仕様

- 設計書：[requirements.md](requirements.md)（正本。変更しません）
- 実装済みの仕様：[SPEC/](SPEC/)（未実装のものは書きません）
  - [API の仕様](SPEC/api.md)・[Domain Core の規則](SPEC/domain-core.md)・[図](SPEC/diagrams.md)・[設計書との差](SPEC/design-differences.md)・[Web 殻](SPEC/web.md)

## ページ一覧

| パス | 内容 |
|---|---|
| `/` | 人生配分シミュレーション（Unity の画面を読み込みます。ビルドが無いときは、その理由を画面に表示します） |
| `/legal` | 利用規約・免責事項・データの取り扱い・連絡先 |

`/api/*` は JSON の API です（次の節）。ページではありません。

## API 一覧

| メソッドとパス | 内容 |
|---|---|
| `GET /up` | 死活確認（状態は変えません） |
| `POST /api/session` | 匿名セッションの発行（Cookie で管理します。有効な Cookie があれば何もしません） |
| `GET /api/game` | 現在のゲームの取得（ゲームが無ければ `game` は `null`。頻度の上限は 1 秒 2 件・1 分 60 件） |
| `POST /api/games` | 新規ゲームの開始（既存のゲームは上書きします） |
| `POST /api/games/turns` | 1 年ぶんの配分の保存（同じ番号・同じ配分の再送は、保存済みの結果を返します） |

`/api/*` の要求と応答は JSON です（`GET /up` は既定で HTML）。入出力・エラーコード・頻度の上限は [SPEC/api.md](SPEC/api.md) にあります。

## 開発

開発の手順と規定は [CLAUDE.md](CLAUDE.md) にあります。

### 構成

| 場所 | 中身 |
|---|---|
| `src/core/DomainCore/` | Domain Core（netstandard2.1・C# 9。Unity で使える条件に合わせて固定） |
| `src/core/DomainCore.Tests/` | Domain Core のテスト（xUnit.net v3・net10.0） |
| `src/core-host/CoreHost/` | 再計算ホスト（net10.0）。Domain Core を呼んで結果を JSON にするだけの層です |
| `src/core-host/CoreHost.Tests/` | 再計算ホストのテスト（net10.0） |
| `src/backend/` | 保存 API（Rails 8.1 の API mode・SQLite・RSpec） |
| `src/web/` | Web 殻（HTML・CSS・JavaScript・開発用サーバー） |
| `src/unity/` | Unity（WebGL。表示と入力だけを担います） |
| `test/` | Playwright と html-validate の設定とテスト（web） |
| `SPEC/` | 実装済みの仕様 |
| `.github/workflows/ci.yml` | CI（[CI の節](#ci)） |

### 必要なもの

- .NET 10 SDK
- Ruby 3.4.11（`src/backend/.ruby-version`）
- Node.js 22
- Unity の版：6000.0.34f1（WebGL Build Support を含みます。Unity のビルドをするときだけ要ります。[Unity の節](#unity)）

### ローカルでの起動

次の 3 つを、この順で行います。ホストは Rails が呼び出すため、先にビルドします。

1. 再計算ホストをビルドします（リポジトリのルートで実行）。

   ```bash
   dotnet build src/core-host/CoreHost -c Release
   ```

   出力は `src/core-host/CoreHost/bin/Release/net10.0/CoreHost.dll` です。`CORE_HOST_DLL` には、この絶対パスを指定します。

2. Rails を起動します（`src/backend` で実行）。`RAILS_ENV` と `APP_ENV` は両方 `development` にします。片方だけでは起動しません。

   ```bash
   cd src/backend
   bundle install
   RAILS_ENV=development APP_ENV=development APP_ORIGIN=http://localhost:8080 DATABASE_PATH=/絶対パス/src/backend/tmp/development.sqlite3 SECRET_KEY_BASE=64文字以上の値 CORE_HOST_DLL=/絶対パス/src/core-host/CoreHost/bin/Release/net10.0/CoreHost.dll bin/rails db:prepare
   RAILS_ENV=development APP_ENV=development APP_ORIGIN=http://localhost:8080 DATABASE_PATH=/絶対パス/src/backend/tmp/development.sqlite3 SECRET_KEY_BASE=64文字以上の値 CORE_HOST_DLL=/絶対パス/src/core-host/CoreHost/bin/Release/net10.0/CoreHost.dll bin/rails server
   ```

   - 待ち受けは `http://localhost:3000` です。
   - `SECRET_KEY_BASE` は `bin/rails secret` で作れます。表示された値を入れてください。コミットしないでください。
   - `DATABASE_PATH` は絶対パスです。`src/backend/tmp/` は Git に載らないので、ローカルの DB はそこに作られます。
   - `APP_ORIGIN` は、ブラウザが送る Origin と完全一致させます（`http://localhost:8080`）。

3. 開発用サーバーを起動します（リポジトリのルートで実行。別のターミナル）。

   ```bash
   APP_ENV=development node src/web/dev-server.mjs
   ```

   - 待ち受けは `http://127.0.0.1:8080`（ブラウザでは `http://localhost:8080` を開きます）です。
   - `/api` は `http://localhost:3000` の Rails へ転送します。転送のとき `X-Real-IP` は接続元のアドレスで付け直します。
   - `APP_ENV` が `development` でなければ、起動を拒否します。

Unity のビルドが `src/web/unity/` に無いときは、`/` に理由が表示されます（黙って失敗しません）。

### テスト

- Domain Core と再計算ホスト：

  ```bash
  dotnet test src/core/DomainCore.Tests
  dotnet test src/core-host/CoreHost.Tests
  ```

- 保存 API。統合テストは再計算ホストを呼ぶため、先にホストをビルドします。

  ```bash
  dotnet build src/core-host/CoreHost -c Release
  cd src/backend
  bundle install
  bundle exec rspec
  bin/rubocop
  bin/brakeman --no-pager
  bin/bundler-audit check --update
  ```

  テスト用の DB は、`spec/rails_helper.rb` が `db/schema.rb` から作ります。テスト用の環境変数は `spec/spec_helper.rb` が設定するため、秘密の値は要りません。`CORE_HOST_DLL` を指定しなければ、リポジトリ内のホストのビルド結果を使います。

- Web（Playwright と html-validate）：

  ```bash
  cd test
  npm ci
  npx playwright install chromium-headless-shell
  npx playwright test web
  npm run validate:html
  ```

  Linux で必要なライブラリが無いときは、`npx playwright install --with-deps chromium-headless-shell` を使います。開発用サーバーは、テストの中で起動します。別に起動する必要はありません。

- Unity の静的検査（Node の標準テスト。Unity 本体は動かしません）：

  ```bash
  node --test src/unity/tests/static-checks.test.mjs
  ```

### CI

`.github/workflows/ci.yml` は、pull_request と main への push で動きます。

| ジョブ | 内容 |
|---|---|
| `core` | .NET 10 で、Domain Core のテスト・再計算ホストのテスト・ホストの Release ビルド |
| `backend` | Ruby 3.4.11 で、ホストを Release でビルドし、`CORE_HOST_DLL` を設定してから、Rubocop・Brakeman・bundler-audit・RSpec（テスト用の DB は RSpec が作ります） |
| `web` | Node.js 22 で、Unity の静的検査（Node の標準テスト）、`test/` の Playwright（`web`）と html-validate |

Unity 本体のビルドは CI に入れません。静的検査は CI で実行します。静的検査は `web` ジョブで、Node の標準テストとして動かします。ビルドを入れない理由は次のとおりです。

- Unity 6000.0.34f1 のエディタは、ライセンスの認証と大きなインストールが要ります。CI のランナーには入っていません。
- ビルドに約 10 分かかります。
- 規則は Domain Core のテストで検査しています。Unity 側は、静的検査（Node のテスト）と、手元でのビルドで確かめます。

### 環境変数

- 名前の一覧は [.env.example](.env.example) にあります。値は書かず、コミットしません。
- 開発とテストでは、Rails が `src/backend/.env` を読みます（development と test のときだけ）。本番は `.env` を読まず、環境変数で渡します。
- `RAILS_ENV` は `.env` では効きません（起動の時点で決まるため）。書かないと production になります。
- 起動の時点で、設定が足りなければ起動しません（`SECRET_KEY_BASE`・`DATABASE_PATH`・`APP_ORIGIN` など）。

### 環境名の規定（APP_ENV）

- `APP_ENV` が `development` のときだけ、開発用の分岐にします（開発用サーバーの起動、http の Origin の許可）。
- 未設定・不明な値は、本番として扱います（fail closed）。
- `RAILS_ENV` と `APP_ENV` は、両方 `development` にするか、両方そうしないかのどちらかです。
- Cookie の `Secure`・レート制限は、環境によって緩めません。CORS の許可は設定していません（同一オリジンだけを受け付けます）。

## 運用

### 毎日 JST 03:00 の全データ削除（Railway の cron）

設計書 2.1 の全データ削除は、コマンド（`bin/rails lasd:reset_all`）として実装済みです。Railway の cron サービスへの設定は、デプロイ時に設定する手順です（未確認。ボリュームの共有は実機で確認する）。

- コマンド（実装済み）：`bin/rails lasd:reset_all`。`sessions`・`games`・`turns`・`request_events` の全行を、1 つの transaction で削除します。削除の件数だけを標準出力に出します。ID・Cookie・データの中身は出しません。2 回実行しても失敗しません。
- デプロイ時に設定する手順（未確認）：
  - スケジュール：`0 18 * * *`。cron は UTC で解釈されるため、UTC 18:00 が JST 03:00 です。
  - コマンド：`bin/rails lasd:reset_all`。アプリのイメージの中で実行します（`bin/rails` と `lib/tasks/lasd.rake` がイメージに入っている必要があります）。
  - 環境変数：Web と同じ名前の変数（`RAILS_ENV`・`SECRET_KEY_BASE`・`DATABASE_PATH`・`APP_ORIGIN`。`APP_ENV` は使うときだけ。値は書きません）を設定します。
  - ボリューム：Web と同じ SQLite のボリュームを設定します。cron サービスと共有できるかは、実機で確認します。共有できない場合は、設計書との差として扱います（[SPEC/design-differences.md](SPEC/design-differences.md) の 5）。

## Unity

Unity の版は 6000.0.34f1 です。2022.3 LTS 系ではなく、6000 系を使っています。

### 前提

- Windows に Unity 6000.0.34f1（WebGL Build Support を含む）。
- WSL に .NET 10 SDK と Node.js 22。

### ビルド

ビルドの前に、環境変数 `UNITY_EXE` に Unity.exe の場所（WSL から見たパス）を設定します。`UNITY_EXE` は必須です。未設定のときは、ビルドを始めずにエラーで止まります。値は各自の環境で設定し、この文書やスクリプトには書きません。

リポジトリのルートで実行します（約 10 分かかります）。

```bash
bash src/unity/tools/build-webgl.sh
```

- Unity は WSL のフォルダ（大文字と小文字を区別するファイルシステム）にあるプロジェクトを開けないため、Windows 側の作業フォルダでビルドします。既定の作業フォルダは `%TEMP%\LifeAllocationSim-unity-build` です。
- 作業フォルダは使い捨てです。ビルドのたびに `src/unity` の内容で上書きされるため、作業フォルダの中で手で変えた内容は残りません。ビルドの途中で `src/unity` を手で変えないでください。終わりに、Unity が作った `.meta`・シーン・ProjectSettings で上書きします。
- ログは作業フォルダの `build.log` に出ます。終了コードが 0 でなければ、ログ末尾のエラーを表示して止まります。
- 環境変数：`UNITY_EXE`（必須：Unity.exe の場所）と、`LASD_UNITY_WORK`（省略可：作業フォルダ。既存の作業フォルダを使うと、Library の取り込みを再利用できます）。
- 出力は `src/web/unity/` に置かれます。Git には載りません。

### DLL だけを更新する

Domain Core の DLL だけを更新するときは、次を実行します。

```bash
bash src/unity/tools/copy-domain-core.sh
```

Domain Core を Release でビルドし、`bin/Release/netstandard2.1/DomainCore.dll` を `src/unity/Assets/Plugins/DomainCore/` にコピーします。ソースはコピーしません。DLL はコミットしません。

### Git に載せるもの

- Unity の `Assets/` 配下の `.meta` ファイルは GUID を含むため、Git に載せます。
- 載せないもの：Domain Core の DLL のフォルダ（`src/unity/Assets/Plugins/DomainCore/`）、WebGL のビルドの出力（`src/web/unity/`）、Library などの生成物です（`.gitignore` で除外しています）。

### 静的検査

```bash
node --test src/unity/tests/static-checks.test.mjs
```

Unity の自動テストは作っていません。規則は Domain Core のテストで検査し、Unity 側は、数値の直書きや禁止した API を使っていないかを静的に検査します。あわせて、例外の握りつぶし、`??` の既定値、static の可変フィールド、外部の JavaScript・ネイティブの呼び出し（ExternalEval・DllImport など）を検査します。

### 確認の範囲

- 確認済み：バッチでの WebGL のビルドが成功したこと（終了コード 0）。
- 未確認：Unity エディタでの画面操作、マウスホイールでのスクロール、キーボードだけでの操作、スマートフォンの実機、性能、応答のエラーの画面（401・409・422）、破綻のゲームの終わり方、支出と市場のイベントの文言。
- 画面の動作は、ブラウザで次の手順により確認します。Rails と開発用サーバーを起動し、ビルドを `src/web/unity/` に置いた状態で行います。
  1. `http://localhost:8080` を開き、上部のデモ版のバナーと、フッターのリンクがあることを確かめます。
  2. 難易度を選んで「新規開始」を押し、メインの画面が出ることを確かめます。
  3. 配分を変えて「次の年へ」を押し、年次の結果が出ることを確かめます。
  4. ページを再読み込みし、「続きから」で同じ年から再開できることを確かめます。
  5. 画面の幅を 375px 程度にして、横スクロールが出ないことを確かめます。
