using System;
using System.IO;
using System.Text.Json;
using CoreHost.Config;
using DomainCore;

namespace CoreHost
{
    /// <summary>
    /// 2 つの操作（init・replay）。Domain Core を呼び、結果を応答の JSON にする。規則は持たない（入出力の形は Protocol.md）。
    /// 成功の応答を MemoryStream に組み立てて返す。規則違反は RequestRejectedException にして、終了コード 2 で返す。
    /// </summary>
    internal static class Commands
    {
        /// <summary>init：難易度から、新規ゲームの初期状態を返す。</summary>
        public static MemoryStream Init(JsonElement root)
        {
            var start = StartFrom(root);

            var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer))
            {
                json.WriteStartObject();
                json.WriteString(ProtocolNames.Difficulty, start.Difficulty.Code);
                ProtocolWriter.WriteState(json, ProtocolNames.State, start.InitialState);
                json.WriteEndObject();
            }

            return buffer;
        }

        /// <summary>replay：シード・難易度・配分履歴から、各年の結果と最終状態（完走・破綻なら評価）を返す。</summary>
        public static MemoryStream Replay(JsonElement root)
        {
            var seed = RequestReader.ReadSeed(root);
            var start = StartFrom(root);
            var history = RequestReader.ReadHistory(root);

            var replay = Replayer.Replay(start, seed, history);
            if (!replay.IsValid)
            {
                throw RejectionOf(replay.Failure);
            }

            var outcome = replay.Outcome;
            var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer))
            {
                json.WriteStartObject();
                json.WriteStartArray(ProtocolNames.Turns);
                for (var index = 0; index < outcome.Years.Count; index++)
                {
                    ProtocolWriter.WriteTurn(json, index + 1, outcome.Years[index]);
                }

                json.WriteEndArray();
                ProtocolWriter.WriteState(json, ProtocolNames.State, outcome.FinalState);
                if (outcome.IsFinished)
                {
                    ProtocolWriter.WriteEvaluation(json, ProtocolNames.Evaluation, outcome.Evaluation);
                }
                else
                {
                    json.WriteNull(ProtocolNames.Evaluation);
                }

                json.WriteEndObject();
            }

            return buffer;
        }

        private static GameStart StartFrom(JsonElement root)
        {
            // 未指定（項目がない・null）も、不明なコードも、Domain Core の検証の結果をそのまま返す。空文字列は未指定として扱われる
            var start = NewGame.Start(RequestReader.ReadDifficultyCode(root) ?? string.Empty);
            if (!start.IsValid)
            {
                throw RejectionOf(start.Reason);
            }

            return start.Value;
        }

        private static RequestRejectedException RejectionOf(DifficultyCodeViolation violation)
        {
            var code = violation == DifficultyCodeViolation.Missing ? ProtocolCodes.DifficultyMissing : ProtocolCodes.DifficultyUnknown;
            return new RequestRejectedException(code, ReasonDescriptions.Describe(violation), null);
        }

        private static RequestRejectedException RejectionOf(ReplayFailure failure)
        {
            if (failure.Violation == ReplayViolation.GameFinished)
            {
                return new RequestRejectedException(
                    ProtocolCodes.GameFinished,
                    ReasonDescriptions.Describe(failure.Violation),
                    failure.TurnNo);
            }

            var reason = failure.AllocationReason ?? throw new InvalidOperationException(ProtocolTexts.InternalError);
            return new RequestRejectedException(CodeOf(reason), ReasonDescriptions.Describe(reason), failure.TurnNo);
        }

        private static string CodeOf(AllocationViolation violation)
        {
            switch (violation)
            {
                case AllocationViolation.Negative:
                    return ProtocolCodes.AllocationNegative;
                case AllocationViolation.StepMismatch:
                    return ProtocolCodes.AllocationStepMismatch;
                case AllocationViolation.SumMismatch:
                    return ProtocolCodes.AllocationSumMismatch;
                default:
                    throw new ArgumentOutOfRangeException(nameof(violation), violation, null);
            }
        }
    }
}
