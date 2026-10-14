# 全データの削除（毎日 JST 03:00 の cron。lib/tasks/lasd.rake の lasd:reset_all から呼ぶ）。
# sessions・games・turns・request_events の全行を、1 つの transaction で削除する。ほかの表には触らない。
# 子の表から順に消す（turns → games → sessions）。request_events は他の表と関係がない。
class DataReset
  class << self
    # 削除した件数（表ごと）を返す。ID・Cookie・データの中身は返さない。
    def call
      ApplicationRecord.transaction do
        {
          turns: Turn.delete_all,
          games: Game.delete_all,
          sessions: Session.delete_all,
          request_events: RequestEvent.delete_all
        }
      end
    end
  end
end
