# config/messages.yml の文言（ログと内部の例外の文言）を読む。応答には出さない文言の置き場。
# 文言は形式（%{name} の差し込み）で、呼ぶ側が値を渡す。キーや値が足りなければ例外になる（既定値では補わない）。
module InternalMessages
  class << self
    # path はキーの並び（例：text(:core_host, :timed_out)）。values は差し込む値。
    def text(*path, **values)
      template = path.reduce(settings) { |node, key| node.fetch(key) }
      format(template, **values)
    end

    private

    def settings
      @settings ||= Rails.application.config_for(:messages)
    end
  end
end
