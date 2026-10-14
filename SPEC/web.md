# Web 殻の仕様

Unity の画面を載せる HTML の殻（`src/web/`）の仕様です。見た目は既定のフォントと最小限の CSS にとどめています（デザインはしていません）。文言と設定値は `src/web/js/config.js` と各 HTML にあります。

## ページ

| パス | ファイル | 内容 |
|---|---|---|
| `/` | `src/web/index.html` | 人生配分シミュレーション（Unity の画面を読み込む） |
| `/legal` | `src/web/legal.html` | 利用規約・免責事項・データの取り扱い・連絡先 |

`/legal` は、Vercel の `cleanUrls`（`src/web/vercel.json`）で `legal.html` を指します。開発用サーバーも同じ規則で配ります。`/legal` の連絡先は、組織の窓口の `info@rictaworks.jp` だけです（個人名と個人のメールアドレスは書きません）。

## すべてのページに共通の要素（CLAUDE.md の必須要素）

- 最上部のバナー（アンバーの背景）：「これはデモ版です。データはサーバー再起動時にリセットされる場合があります。」
- ナビの右端：「← デモ一覧へ」（リンク先 `https://rictaworks.jp/#demos`）。
- 「ご相談はこちら」（リンク先 `https://rictaworks.jp/`）。幅 1280px 以上では右下に固定します。1280px 未満では固定せず、本文の末尾（フッターの後）に置きます。理由と判断待ちの案は [設計書との差](design-differences.md) の 14 を参照してください。
- フッター：「利用規約」（`/legal#terms`）・「免責事項」（`/legal#disclaimer`）・「連絡先」（`/legal#contact`）。
- ページのタイトルは日本語です。`lang="ja"`。
- テーマはライト固定です。`<meta name="color-scheme" content="light">` と CSS の `color-scheme: light` を書き、`prefers-color-scheme` の分岐は作りません。
- 利回り・イベント・物価がすべて架空のモデル値であることを示す文を、`/` の本文に常時表示します。
- 文字列の `alert`・`confirm`・`prompt` は使いません。絵文字は使いません。

## アクセシビリティ

- 先頭のスキップリンク（「本文へ移動」）。
- ランドマーク：`header`・`nav`（主要・フッター）・`main`・`footer`。
- 見出しは `h1` から始め、段を飛ばしません。`/` は `h1`（人生配分シミュレーション）の下に `h2`（シミュレーション）を置きます。`/legal` は `h1` の下に、各節の `h2` を置きます。
- キーボードで操作できること。フォーカスの見た目は、ブラウザの既定を消しません。
- 幅 375px のスマートフォンでも、左右の余白は 16px で、横スクロールは出ません（テストで確認）。

## 同意とアクセス解析（GA4）

- 測定 ID：`G-C04W1XKS16`（`config.js` の `analytics.measurementId`）。
- Consent Mode v2 で、既定の同意はすべて「拒否」です。読み込みの前に既定を設定します。
- 同意バナーに「許可」と「拒否」の 2 つのボタンを、同じ強さで置きます。
- 選択は `localStorage` に保存します（キーは `life-allocation-sim.analytics-consent`。値は `granted` または `denied`）。保存された値が不正なら無視します。
- 読み取りや保存に失敗しても、画面の操作は止めません。失敗した旨はコンソールに出します。同意の状態が分からないときは、同意バナーを表示します。
- 同意の選択のあとは、フッターの「アクセス解析の設定を変更」で同意バナーを開き直せます（選択の前は表示しません）。

## Unity の画面（`/` の本文）

- 描画領域：`<canvas id="unity-canvas">`。
- 読み込みの契約（`config.js` の `unity`）：
  - 製品名は `LifeAllocationSim`、会社名は `rictaworks`、版は `0.1.0`。
  - ローダー `unity/Build/LifeAllocationSim.loader.js`。
  - `createUnityInstance` の設定：`dataUrl`（`unity/Build/LifeAllocationSim.data`）・`frameworkUrl`（`unity/Build/LifeAllocationSim.framework.js`）・`codeUrl`（`unity/Build/LifeAllocationSim.wasm`）・`streamingAssetsUrl`（`unity/StreamingAssets`）。
- ビルドが無いとき（ローダーを読めないとき）は、黙って失敗せず、画面に理由を表示します。文言は「シミュレーションの表示データが見つからないため、表示できません。Unity のビルドを配置すると、ここに表示されます。」です。
- 読み込み中は進み具合（％）を表示します。読み込みに失敗したときは、再読み込みを案内します。
- Unity のビルドは Git に載りません（`src/web/unity/`）。配置の手順は README の「Unity」の節にあります。
- 保存 API の失敗は、次のように表示します（`src/unity/Assets/Scripts/Game/GameController.cs` の `HandleFailure`。文言は `src/unity/Assets/Scripts/Config/UiTexts.cs` が正です）。
  - 401（`session_required`）で、ゲームの途中（次の年への保存）のとき：「データがリセットされました。新しく始めてください。」を表示して、タイトルへ戻ります（`ResetNotice`）。
  - 404（`game_not_found`）のとき：同じ「データがリセットされました。新しく始めてください。」を表示して、タイトルへ戻ります。
  - 401 で、ゲームの途中以外のとき：「セッションを確認できませんでした。ページを再読み込みしてください。」を表示します。再試行すると、セッションの発行からやり直します（`ErrorSession`）。
  - 429（`rate_limited`）：「アクセスが集中しています。少し時間をおいてから、もう一度お試しください。」を表示し、再試行できます（`ErrorRateLimited`）。
  - 503（`service_unavailable`）：「ただいま混み合っています。少し時間をおいてから、もう一度お試しください。」を表示し、再試行できます（`ErrorUnavailable`）。
  - 通信できないとき：「通信できませんでした。接続を確認して、もう一度お試しください。」を表示し、再試行できます（`ErrorNetwork`）。
  - 409（番号のずれ・完走のあとの保存）：「最新の状態を読み込む」を出します。422（配分の規則違反・難易度）：「タイトルへ戻る」だけを出します。そのほかの応答は、汎用の文言（`ErrorGeneric`）と再試行を出します。

## 開発用サーバー（`src/web/dev-server.mjs`）

- `APP_ENV` が `development` のときだけ起動します。それ以外は起動を拒否します（fail closed）。
- 待ち受けは `127.0.0.1:8080`。静的ファイルを配り、`/api` を Rails（既定 `http://localhost:3000`）へ転送します。
- 転送のとき、`X-Real-IP` は接続元のアドレスで付け直します。クライアントが送った `X-Real-IP`・`X-Forwarded-*`・`Forwarded` は捨てます。
- Rails が止まっているときは、502 または 504 を返します。本文は案内の文だけで、例外やスタックトレースは含みません。
- 開発用サーバーのテストは、テストの中で `APP_ENV=development` を付けて起動します。
