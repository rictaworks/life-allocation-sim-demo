# 全データの削除（毎日 JST 03:00 の cron。UTC では 18:00 と指定する）。実行：bin/rails lasd:reset_all
# 件数だけを標準出力に出す（ID・Cookie・データの中身は出さない）。行が無ければ 0 件で成功する（2 回実行しても失敗しない）。
namespace :lasd do
  task reset_all: :environment do
    counts = DataReset.call
    puts I18n.t("reset_all.summary", **counts, locale: :ja, raise: true)
  end
end
