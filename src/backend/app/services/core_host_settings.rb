# config/core_host.yml の値を読む。値が無ければ例外になる（既定値では補わない）。
module CoreHostSettings
  class << self
    def executable
      fetch(:executable)
    end

    def dll_env_key
      fetch(:dll_env_key)
    end

    def child_env_keys
      fetch(:child_env_keys)
    end

    # 環境変数の名前は文字列で扱う（YAML の読み込みでキーがシンボルになるため、ここで戻す）。
    def child_env
      fetch(:child_env).transform_keys(&:to_s)
    end

    def timeout_seconds
      fetch(:timeout_seconds)
    end

    def wait_seconds
      fetch(:wait_seconds)
    end

    def max_concurrent
      fetch(:max_concurrent)
    end

    def max_input_bytes
      fetch(:max_input_bytes)
    end

    def max_output_bytes
      fetch(:max_output_bytes)
    end

    def max_stderr_bytes
      fetch(:max_stderr_bytes)
    end

    def command(name)
      fetch(:commands, name)
    end

    def exit_code(name)
      fetch(:exit_codes, name)
    end

    def rule_violation_codes
      fetch(:rule_violation_codes)
    end

    # ゲームの状態の文字列（playing・completed・bankrupt）。
    def game_status(name)
      fetch(:game_statuses, name)
    end

    private

    def fetch(*path)
      path.reduce(settings) { |node, key| node.fetch(key) }
    end

    def settings
      @settings ||= Rails.application.config_for(:core_host)
    end
  end
end
