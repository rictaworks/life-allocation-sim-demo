# ログの出力を文字列として取り出す。テストの中で Rails のロガーを一時的に差し替える。
module LogCapture
  LOGGING_CLASSES = [ ActionController::API, ActionController::Base, ActiveRecord::Base ].freeze

  def capture_logs
    io = StringIO.new
    logger = ActiveSupport::Logger.new(io)
    logger.level = Logger::DEBUG

    previous_rails = Rails.logger
    previous_classes = LOGGING_CLASSES.to_h { |klass| [ klass, klass.logger ] }
    Rails.logger = logger
    LOGGING_CLASSES.each { |klass| klass.logger = logger }

    yield
    io.string
  ensure
    previous_classes&.each { |klass, previous| klass.logger = previous }
    Rails.logger = previous_rails if previous_rails
  end
end

RSpec.configure do |config|
  config.include LogCapture
end
