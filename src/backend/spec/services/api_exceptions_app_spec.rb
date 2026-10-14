require "rails_helper"

# config.exceptions_app（コントローラの外で起きた例外を、{error: {code, message}} の形にする）の検査。
RSpec.describe ApiExceptionsApp do
  # Rails と同じ入り方（env に例外を入れて呼ぶ）。状態・code・本文を返す。
  def respond_to_exception(exception)
    status, _headers, parts = described_class.call(ApiSettings.exception_env_key => exception)
    body = parts.join
    [ status, JSON.parse(body).dig("error", "code"), body ]
  end

  it "対応表にある状態は、その code で返す（400・404）" do
    expect(respond_to_exception(ActionController::BadRequest.new).first(2)).to eq([ 400, "bad_request" ])
    expect(respond_to_exception(ActionController::RoutingError.new("No route matches")).first(2))
      .to eq([ 404, "not_found" ])
  end

  it "対応表にある 413・415 も、その code で返す" do
    allow(ActionDispatch::ExceptionWrapper).to receive(:status_code_for_exception).and_return(413, 415)

    expect(respond_to_exception(RuntimeError.new).first(2)).to eq([ 413, "payload_too_large" ])
    expect(respond_to_exception(RuntimeError.new).first(2)).to eq([ 415, "unsupported_media_type" ])
  end

  it "Rails が出す 405・406・501 は、個別の code を持たないため、内部エラー（500）で返す（既存の挙動）" do
    [ ActionController::MethodNotAllowed.new, ActionController::UnknownFormat.new, ActionController::NotImplemented.new ]
      .each do |exception|
        expect(respond_to_exception(exception).first(2)).to eq([ 500, "internal_error" ]), exception.class.name
      end
  end

  it "対応表に無い例外（既定の 500）は内部エラーにし、例外の内容は応答に出さない" do
    status, code, body = respond_to_exception(RuntimeError.new("SELECT secret_marker"))

    expect([ status, code ]).to eq([ 500, "internal_error" ])
    expect(body).not_to include("secret_marker")
  end

  it "対応表にも内部エラーの一覧にも無い状態は、内部エラーにし、状態と例外のクラス名をログに残す（応答には出さない）" do
    allow(ActionDispatch::ExceptionWrapper).to receive(:status_code_for_exception).and_return(418)

    result = nil
    logs = capture_logs { result = respond_to_exception(ActionController::BadRequest.new) }

    expect(result.first(2)).to eq([ 500, "internal_error" ])
    expect(result.last).not_to include("418")
    expect(logs).to include("418")
    expect(logs).to include("ActionController::BadRequest")
  end
end
