# SPEC/

実装済みの仕様だけを書きます。未実装のものは書きません。

- 正本は [requirements.md](../requirements.md)（設計書）です。ここには、実装して確認できた内容だけを写します。設計書は変更しません。
- 設計書と実装の違いは、[設計書との差](design-differences.md) に書きます。
- 図は、コードと照らして確かめた版です。実装を変えたときは、図も直します。

## 文書の一覧

| ファイル | 内容 |
|---|---|
| [api.md](api.md) | 保存 API の経路・入出力・エラーコード・頻度と大きさ |
| [domain-core.md](domain-core.md) | Domain Core の規則（定数・難易度・イベント・1 年の精算・評価・乱数） |
| [diagrams.md](diagrams.md) | ER 図・DFD・シーケンス図・クラス図・状態遷移図・ユースケース図 |
| [design-differences.md](design-differences.md) | 設計書との差と、その状態（実装済み・確認事項・未解決） |
| [web.md](web.md) | Web 殻（ページ・同意とアクセス解析・Unity の読み込み・開発用サーバー） |

## 参照する規定

- 作業の規定と開発の規則は [CLAUDE.md](../CLAUDE.md) にあります。
- 再計算ホストの入出力（JSON の形と終了コード）は [src/core-host/CoreHost/Protocol.md](../src/core-host/CoreHost/Protocol.md) が正です。
