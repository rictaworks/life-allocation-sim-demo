require "rails_helper"

RSpec.describe CoreHostShape do
  include CoreHostFixtures

  it "init の形（difficulty と state）を受け付ける" do
    expect { described_class.check!(init_output, :init) }.not_to raise_error
  end

  it "replay の形（turns・state・evaluation）を受け付ける。evaluation は null でも評価の形でもよい" do
    expect { described_class.check!(one_year_replay_output, :replay) }.not_to raise_error

    with_evaluation = one_year_replay_output.merge("evaluation" => evaluation_output)
    expect { described_class.check!(with_evaluation, :replay) }.not_to raise_error
  end

  it "欠けたキー・違う型は Mismatch にし、どこが違うかをパスで示す（値は示さない）" do
    missing = init_output.deep_dup
    missing["state"].delete("age")
    expect { described_class.check!(missing, :init) }.to raise_error(CoreHostShape::Mismatch, "init.state.age")

    wrong_type = init_output.deep_dup
    wrong_type["state"]["struggling"] = "no"
    expect { described_class.check!(wrong_type, :init) }.to raise_error(CoreHostShape::Mismatch, "init.state.struggling")
  end

  it "整数は整数だけ（小数・文字列の数字は不可）" do
    as_float = init_output.deep_dup
    as_float["state"]["age"] = 22.0
    expect { described_class.check!(as_float, :init) }.to raise_error(CoreHostShape::Mismatch, "init.state.age")

    as_string = init_output.deep_dup
    as_string["state"]["savings_yen"] = "0"
    expect { described_class.check!(as_string, :init) }.to raise_error(CoreHostShape::Mismatch, "init.state.savings_yen")
  end

  it "配列の要素の型も確かめる（event_code は文字列か null）" do
    output = one_year_replay_output.deep_dup
    output["turns"][0]["event_code"] = 12

    expect { described_class.check!(output, :replay) }
      .to raise_error(CoreHostShape::Mismatch, "replay.turns[0].event_code")
  end

  it "評価の中のコメントの形も確かめる" do
    output = one_year_replay_output.merge("evaluation" => evaluation_output.deep_dup)
    output["evaluation"]["comments"].delete("quality")

    expect { described_class.check!(output, :replay) }
      .to raise_error(CoreHostShape::Mismatch, "replay.evaluation.comments.quality")
  end

  it "エラーの形は code と message（文字列）" do
    expect { described_class.check!({ "error" => { "code" => "x", "message" => "m" } }, :error) }.not_to raise_error
    expect { described_class.check!({ "error" => { "code" => 1, "message" => "m" } }, :error) }
      .to raise_error(CoreHostShape::Mismatch, "error.error.code")
  end

  it "トップレベルが対象の形でなければ（配列など）Mismatch" do
    expect { described_class.check!([], :init) }.to raise_error(CoreHostShape::Mismatch, "init")
  end
end
