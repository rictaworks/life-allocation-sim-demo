# ENV を一時的に変え、ブロックの後で元の値に戻す。nil は「未設定」を表す。
module EnvHelpers
  def with_env(overrides)
    original = overrides.keys.to_h { |key| [ key, ENV.fetch(key, nil) ] }
    apply_env(overrides)
    yield
  ensure
    apply_env(original) if original
  end

  private

  def apply_env(values)
    values.each do |key, value|
      if value.nil?
        ENV.delete(key)
      else
        ENV[key] = value
      end
    end
  end
end

RSpec.configure do |config|
  config.include EnvHelpers
end
