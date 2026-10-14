using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CoreHost.Config;
using DomainCore;
using DomainCore.Config;
using Xunit;

namespace CoreHost.Tests
{
    /// <summary>
    /// 再計算ホストの入出力（CoreHostRunner.Run を直接呼ぶ。子プロセスは起動しない）。
    /// 規則のテストは Domain Core 側にある。ここでは、Domain Core の結果を JSON にする部分と、終了コード・入力の上限を確かめる。
    /// </summary>
    public class RunnerTests
    {
        // 難易度のコードは、Domain Core の一覧から取る（ホストのソースに難易度の名前を書かない）
        private static string Code => DifficultyTable.All[1].Code;

        [Fact]
        public void InitReturnsTheInitialStateAsJson()
        {
            var (exit, text) = Run(ProtocolNames.InitCommand, $"{{\"difficulty\":\"{Code}\"}}");

            Assert.Equal(ProtocolLimits.ExitSuccess, exit);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            Assert.Equal(Code, root.GetProperty(ProtocolNames.Difficulty).GetString());
            var state = root.GetProperty(ProtocolNames.State);
            Assert.Equal(GameRules.StartAge, state.GetProperty(ProtocolNames.Age).GetInt32());
            Assert.Equal(LifeStage.ForAge(GameRules.StartAge).TakeHomeIncomeYen, state.GetProperty(ProtocolNames.IncomeYen).GetInt64());
            Assert.Equal(0, state.GetProperty(ProtocolNames.SavingsYen).GetInt64());
            Assert.Equal(0, state.GetProperty(ProtocolNames.InvestmentYen).GetInt64());
            Assert.Equal(GameRules.InitialSatisfaction, state.GetProperty(ProtocolNames.Satisfaction).GetInt32());
            Assert.Equal(GameRules.InitialPriceIndexBp, state.GetProperty(ProtocolNames.PriceIndexBp).GetInt32());
            Assert.Equal(ProtocolNames.StatusPlaying, state.GetProperty(ProtocolNames.Status).GetString());
            Assert.False(state.GetProperty(ProtocolNames.Struggling).GetBoolean());
            Assert.Equal(0, state.GetProperty(ProtocolNames.OccurredOnceEvents).GetArrayLength());
        }

        [Fact]
        public void InitRejectsAnUnknownDifficultyWithItsCode()
        {
            var (exit, text) = Run(ProtocolNames.InitCommand, "{\"difficulty\":\"unknown-code\"}");

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            var error = ErrorOf(text);
            Assert.Equal(ProtocolCodes.DifficultyUnknown, error.GetProperty(ProtocolNames.ErrorCode).GetString());
            Assert.False(error.TryGetProperty(ProtocolNames.TurnNo, out _));
        }

        [Fact]
        public void InitRejectsAMissingDifficulty()
        {
            var (exit, text) = Run(ProtocolNames.InitCommand, "{}");

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            Assert.Equal(ProtocolCodes.DifficultyMissing, ErrorOf(text).GetProperty(ProtocolNames.ErrorCode).GetString());
        }

        [Fact]
        public void InitRejectsANullDifficultyAsMissing()
        {
            // null は未指定（difficulty_missing）。既定値に置き換えず、そのまま拒否する
            var (exit, text) = Run(ProtocolNames.InitCommand, "{\"difficulty\":null}");

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            Assert.Equal(ProtocolCodes.DifficultyMissing, ErrorOf(text).GetProperty(ProtocolNames.ErrorCode).GetString());
            Assert.False(ErrorOf(text).TryGetProperty(ProtocolNames.TurnNo, out _));
        }

        [Fact]
        public void ReplayReturnsTurnsAndStateForAPartialHistory()
        {
            var body = ReplayJson(42, (60, 20, 20), (70, 20, 10));

            var (exit, text) = Run(ProtocolNames.ReplayCommand, body);

            Assert.Equal(ProtocolLimits.ExitSuccess, exit);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var turns = root.GetProperty(ProtocolNames.Turns);
            Assert.Equal(2, turns.GetArrayLength());
            Assert.Equal(1, turns[0].GetProperty(ProtocolNames.TurnNo).GetInt32());
            Assert.Equal(GameRules.StartAge, turns[0].GetProperty(ProtocolNames.Age).GetInt32());
            Assert.Equal(60, turns[0].GetProperty(ProtocolNames.LivingPct).GetInt32());
            Assert.Equal(20, turns[0].GetProperty(ProtocolNames.SavingPct).GetInt32());
            Assert.Equal(20, turns[0].GetProperty(ProtocolNames.InvestPct).GetInt32());
            Assert.Equal(JsonValueKind.Null, turns[0].GetProperty(ProtocolNames.EventCode).ValueKind);
            Assert.Equal(2, turns[1].GetProperty(ProtocolNames.TurnNo).GetInt32());
            Assert.Equal(JsonValueKind.Null, root.GetProperty(ProtocolNames.Evaluation).ValueKind);
            Assert.Equal(ProtocolNames.StatusPlaying, root.GetProperty(ProtocolNames.State).GetProperty(ProtocolNames.Status).GetString());
        }

        [Fact]
        public void ReplayTurnsMatchTheDomainCoreResult()
        {
            var allocations = new[] { (60, 20, 20), (70, 20, 10), (100, 0, 0), (50, 30, 20), (80, 10, 10) };
            var expected = Replayer.Replay(NewGame.Start(Code).Value, 42, allocations.Select(a => new Allocation(a.Item1, a.Item2, a.Item3)).ToArray()).Outcome;

            var (exit, text) = Run(ProtocolNames.ReplayCommand, ReplayJson(42, allocations));

            Assert.Equal(ProtocolLimits.ExitSuccess, exit);
            using var document = JsonDocument.Parse(text);
            var turns = document.RootElement.GetProperty(ProtocolNames.Turns);
            for (var i = 0; i < expected.Years.Count; i++)
            {
                var year = expected.Years[i];
                Assert.Equal(year.Next.SavingsYen, turns[i].GetProperty(ProtocolNames.SavingsAfter).GetInt64());
                Assert.Equal(year.Next.InvestmentYen, turns[i].GetProperty(ProtocolNames.InvestAfter).GetInt64());
                Assert.Equal(year.Next.Satisfaction, turns[i].GetProperty(ProtocolNames.SatisfactionAfter).GetInt32());
                var eventCode = turns[i].GetProperty(ProtocolNames.EventCode);
                Assert.Equal(year.Event.HasEvent ? year.Event.Event.Code : null, eventCode.ValueKind == JsonValueKind.Null ? null : eventCode.GetString());
            }
        }

        [Fact]
        public void CompletedReplayIncludesEvaluationAndCompletedState()
        {
            var (exit, text) = Run(ProtocolNames.ReplayCommand, ReplayJson(1, Enumerable.Repeat((100, 0, 0), 43).ToArray()));

            Assert.Equal(ProtocolLimits.ExitSuccess, exit);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var state = root.GetProperty(ProtocolNames.State);
            Assert.Equal(GameRules.FinalAge, state.GetProperty(ProtocolNames.Age).GetInt32());
            Assert.Equal(ProtocolNames.StatusCompleted, state.GetProperty(ProtocolNames.Status).GetString());
            var evaluation = root.GetProperty(ProtocolNames.Evaluation);
            Assert.False(evaluation.GetProperty(ProtocolNames.IsBankrupt).GetBoolean());
            Assert.False(string.IsNullOrEmpty(evaluation.GetProperty(ProtocolNames.Rank).GetString()));
            var comments = evaluation.GetProperty(ProtocolNames.Comments);
            Assert.False(string.IsNullOrEmpty(comments.GetProperty(ProtocolNames.RetirementComment).GetString()));
            Assert.False(string.IsNullOrEmpty(comments.GetProperty(ProtocolNames.QualityComment).GetString()));
            Assert.False(string.IsNullOrEmpty(comments.GetProperty(ProtocolNames.PreparednessComment).GetString()));
        }

        [Fact]
        public void InvalidAllocationReturnsItsCodeAndTurnNumber()
        {
            var (exit, text) = Run(ProtocolNames.ReplayCommand, ReplayJson(7, (60, 20, 20), (50, 50, 50)));

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            var error = ErrorOf(text);
            Assert.Equal(ProtocolCodes.AllocationSumMismatch, error.GetProperty(ProtocolNames.ErrorCode).GetString());
            Assert.Equal(2, error.GetProperty(ProtocolNames.TurnNo).GetInt32());
        }

        [Theory]
        [InlineData(-5, 55, 50, ProtocolCodes.AllocationNegative)]
        [InlineData(33, 33, 34, ProtocolCodes.AllocationStepMismatch)]
        public void NegativeAndStepReasonsUseTheirCodes(int living, int saving, int invest, string code)
        {
            var (exit, text) = Run(ProtocolNames.ReplayCommand, ReplayJson(7, (living, saving, invest)));

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            Assert.Equal(code, ErrorOf(text).GetProperty(ProtocolNames.ErrorCode).GetString());
        }

        [Fact]
        public void TurnsAfterCompletionAreRejectedAsGameFinished()
        {
            var (exit, text) = Run(ProtocolNames.ReplayCommand, ReplayJson(1, Enumerable.Repeat((100, 0, 0), 44).ToArray()));

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            var error = ErrorOf(text);
            Assert.Equal(ProtocolCodes.GameFinished, error.GetProperty(ProtocolNames.ErrorCode).GetString());
            Assert.Equal(44, error.GetProperty(ProtocolNames.TurnNo).GetInt32());
        }

        [Fact]
        public void NegativeSeedIsRejectedAsInvalidRequest()
        {
            var (exit, text) = Run(ProtocolNames.ReplayCommand, ReplayJson(-1));

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            Assert.Equal(ProtocolCodes.RequestInvalid, ErrorOf(text).GetProperty(ProtocolNames.ErrorCode).GetString());
        }

        [Fact]
        public void LargestSeedIsAccepted()
        {
            var (exit, _) = Run(ProtocolNames.ReplayCommand, ReplayJson(long.MaxValue));

            Assert.Equal(ProtocolLimits.ExitSuccess, exit);
        }

        [Fact]
        public void MissingHistoryIsRejectedAsInvalidRequest()
        {
            var (exit, text) = Run(ProtocolNames.ReplayCommand, $"{{\"seed\":1,\"difficulty\":\"{Code}\"}}");

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            Assert.Equal(ProtocolCodes.RequestInvalid, ErrorOf(text).GetProperty(ProtocolNames.ErrorCode).GetString());
        }

        [Fact]
        public void NonIntegerPercentIsRejectedAsInvalidRequest()
        {
            var body = $"{{\"seed\":1,\"difficulty\":\"{Code}\",\"history\":[{{\"living_pct\":70.5,\"saving_pct\":20,\"invest_pct\":10}}]}}";

            var (exit, text) = Run(ProtocolNames.ReplayCommand, body);

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            Assert.Equal(ProtocolCodes.RequestInvalid, ErrorOf(text).GetProperty(ProtocolNames.ErrorCode).GetString());
        }

        [Fact]
        public void UnknownCommandIsRejected()
        {
            var (exit, text) = Run("unknown-command", "{}");

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            Assert.Equal(ProtocolCodes.CommandUnknown, ErrorOf(text).GetProperty(ProtocolNames.ErrorCode).GetString());
        }

        [Fact]
        public void InvalidJsonIsRejectedWithoutExceptionText()
        {
            var (exit, text) = Run(ProtocolNames.InitCommand, "{");

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            Assert.Equal(ProtocolCodes.RequestInvalid, ErrorOf(text).GetProperty(ProtocolNames.ErrorCode).GetString());
            Assert.DoesNotContain("Exception", text);
        }

        [Theory]
        [MemberData(nameof(InvalidUtf8Bodies))]
        public void InvalidUtf8IsRejectedAsInvalidRequest(byte[] body)
        {
            // 入力は UTF-8 の JSON。UTF-8 として正しくないバイト列は、どの項目に入っていても、解析の前に request_invalid で拒否する（終了コード 1 にしない）
            var (exit, text) = Run(ProtocolNames.InitCommand, body);

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            Assert.Equal(ProtocolCodes.RequestInvalid, ErrorOf(text).GetProperty(ProtocolNames.ErrorCode).GetString());
        }

        [Fact]
        public void UnpairedSurrogateEscapeInTheDifficultyIsRejectedAsInvalidRequest()
        {
            // 対にならない代理文字の書き方（\ud800）は、文字列として読めない。読むときに例外にせず、request_invalid で拒否する
            var (exit, text) = Run(ProtocolNames.InitCommand, "{\"difficulty\":\"\\ud800\"}");

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            Assert.Equal(ProtocolCodes.RequestInvalid, ErrorOf(text).GetProperty(ProtocolNames.ErrorCode).GetString());
        }

        [Fact]
        public void UnpairedSurrogateEscapeInAnUnknownValueIsRejectedAsInvalidRequest()
        {
            // 読まない項目でも、文字列として正しくない値は拒否する（UTF-8 の誤りと同じ扱い）
            var (exit, text) = Run(ProtocolNames.InitCommand, $"{{\"x\":\"\\ud800\",\"{ProtocolNames.Difficulty}\":\"{Code}\"}}");

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            Assert.Equal(ProtocolCodes.RequestInvalid, ErrorOf(text).GetProperty(ProtocolNames.ErrorCode).GetString());
        }

        [Fact]
        public void ValidUtf8AndSurrogatePairsInUnknownValuesAreStillAccepted()
        {
            // 正しい文字（日本語・対になった代理文字）は、読まない項目にあっても拒否しない
            var (exit, _) = Run(ProtocolNames.InitCommand, $"{{\"x\":\"日本語 \\ud83d\\ude00\",\"{ProtocolNames.Difficulty}\":\"{Code}\"}}");

            Assert.Equal(ProtocolLimits.ExitSuccess, exit);
        }

        [Fact]
        public void InputOverTheLimitIsRejectedBeforeParsing()
        {
            // 64KB を 1 バイトでも超えたら、中身を解析せずに拒否する（この入力は JSON として壊れている）
            var oversized = new string('x', ProtocolLimits.MaxInputBytes + 1);

            var (exit, text) = Run(ProtocolNames.InitCommand, oversized);

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            Assert.Equal(ProtocolCodes.InputTooLarge, ErrorOf(text).GetProperty(ProtocolNames.ErrorCode).GetString());
        }

        [Fact]
        public void InputAtTheLimitIsParsed()
        {
            var json = $"{{\"difficulty\":\"{Code}\"}}";
            var atLimit = json + new string(' ', ProtocolLimits.MaxInputBytes - json.Length);

            var (exit, _) = Run(ProtocolNames.InitCommand, atLimit);

            Assert.Equal(ProtocolLimits.ExitSuccess, exit);
        }

        [Fact]
        public void InternalErrorReturnsAGenericCodeAndNoDetails()
        {
            using var output = new MemoryStream();

            var exit = CoreHostRunner.Run(new[] { ProtocolNames.InitCommand }, new FailingInput(), output);

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.Equal(ProtocolLimits.ExitInternalError, exit);
            Assert.Equal(ProtocolCodes.InternalError, ErrorOf(text).GetProperty(ProtocolNames.ErrorCode).GetString());
            Assert.DoesNotContain("Exception", text);
            Assert.DoesNotContain("読み取り", text);
        }

        [Fact]
        public void ResponseThatCannotBeWrittenEndsWithTheInternalErrorCodeWithoutThrowing()
        {
            // 標準出力が書けない（例：出力が閉じている）ときも、例外を外へ出さず、終了コード 1 だけを返す
            using var input = new MemoryStream(Encoding.UTF8.GetBytes($"{{\"{ProtocolNames.Difficulty}\":\"{Code}\"}}"));

            var exit = CoreHostRunner.Run(new[] { ProtocolNames.InitCommand }, input, new FailingOutput());

            Assert.Equal(ProtocolLimits.ExitInternalError, exit);
        }

        [Fact]
        public void RuleViolationThatCannotBeWrittenEndsWithTheInternalErrorCodeWithoutThrowing()
        {
            // 規則違反の応答も書けないときは、理由の JSON が出せないので、内部エラーの終了コード 1 を返す
            using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"difficulty\":\"unknown-code\"}"));

            var exit = CoreHostRunner.Run(new[] { ProtocolNames.InitCommand }, input, new FailingOutput());

            Assert.Equal(ProtocolLimits.ExitInternalError, exit);
        }

        [Fact]
        public void StandardInputThatCannotBeOpenedEndsWithTheInternalErrorCodeWithoutThrowing()
        {
            // 標準入力を開けないとき（Console の取得に失敗するとき）も、例外を外へ出さず、終了コード 1 だけを返す。応答は書けないので書かない
            using var output = new MemoryStream();

            var exit = CoreHostRunner.RunOnConsole(new[] { ProtocolNames.InitCommand }, () => throw new IOException("標準入力を開けません"), () => output);

            Assert.Equal(ProtocolLimits.ExitInternalError, exit);
            Assert.Empty(output.ToArray());
        }

        [Fact]
        public void StandardOutputThatCannotBeOpenedEndsWithTheInternalErrorCodeWithoutThrowing()
        {
            // 標準出力を開けないときも、例外を外へ出さず、終了コード 1 だけを返す
            using var input = new MemoryStream(Encoding.UTF8.GetBytes($"{{\"{ProtocolNames.Difficulty}\":\"{Code}\"}}"));

            var exit = CoreHostRunner.RunOnConsole(new[] { ProtocolNames.InitCommand }, () => input, () => throw new IOException("標準出力を開けません"));

            Assert.Equal(ProtocolLimits.ExitInternalError, exit);
        }

        [Fact]
        public void ResponseThatFailsPartwayIsNotFollowedByAnErrorObject()
        {
            // 応答の書き出しの途中で失敗しても、書きかけの応答の後ろに、エラーの JSON を続けて書かない（出力が 2 つの JSON にならない）
            const int bytesBeforeFailure = 10;
            var body = Encoding.UTF8.GetBytes($"{{\"{ProtocolNames.Difficulty}\":\"{Code}\"}}");
            var (_, complete) = RunOutput(ProtocolNames.InitCommand, body);
            using var input = new MemoryStream(body);
            var output = new PartiallyFailingOutput(bytesBeforeFailure);

            var exit = CoreHostRunner.Run(new[] { ProtocolNames.InitCommand }, input, output);

            Assert.Equal(ProtocolLimits.ExitInternalError, exit);
            Assert.Equal(complete.Take(bytesBeforeFailure).ToArray(), output.Written);
        }

        [Fact]
        public void StandardInputThatCannotBeOpenedWritesOnlyTheExceptionTypeToStandardError()
        {
            // 内部エラーは、例外の型名だけを標準エラーに出す（Protocol.md）。メッセージは出さない。応答は書けないので、標準出力は空
            using var output = new MemoryStream();

            var (exit, error) = CaptureStandardError(() =>
                CoreHostRunner.RunOnConsole(new[] { ProtocolNames.InitCommand }, () => throw new IOException("標準入力を開けません"), () => output));

            Assert.Equal(ProtocolLimits.ExitInternalError, exit);
            Assert.Empty(output.ToArray());
            Assert.Equal(TypeNameLine(nameof(IOException)), error);
            Assert.DoesNotContain("標準入力を開けません", error);
        }

        [Fact]
        public void StandardOutputThatCannotBeOpenedWritesOnlyTheExceptionTypeToStandardError()
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes($"{{\"{ProtocolNames.Difficulty}\":\"{Code}\"}}"));

            var (exit, error) = CaptureStandardError(() =>
                CoreHostRunner.RunOnConsole(new[] { ProtocolNames.InitCommand }, () => input, () => throw new IOException("標準出力を開けません")));

            Assert.Equal(ProtocolLimits.ExitInternalError, exit);
            Assert.Equal(TypeNameLine(nameof(IOException)), error);
            Assert.DoesNotContain("標準出力を開けません", error);
        }

        [Fact]
        public void InternalErrorWritesOnlyTheExceptionTypeToStandardError()
        {
            // 読み取りの例外は、型名だけを 1 行で出す（メッセージ・入力値・スタックトレースは出さない）
            using var output = new MemoryStream();

            var (exit, error) = CaptureStandardError(() => CoreHostRunner.Run(new[] { ProtocolNames.InitCommand }, new FailingInput(), output));

            Assert.Equal(ProtocolLimits.ExitInternalError, exit);
            Assert.Equal(TypeNameLine(nameof(IOException)), error);
            Assert.DoesNotContain("読み取りに失敗しました", error);
        }

        [Fact]
        public void UnwritableResponseWritesOnlyTheExceptionTypeToStandardError()
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes($"{{\"{ProtocolNames.Difficulty}\":\"{Code}\"}}"));

            var (exit, error) = CaptureStandardError(() => CoreHostRunner.Run(new[] { ProtocolNames.InitCommand }, input, new FailingOutput()));

            Assert.Equal(ProtocolLimits.ExitInternalError, exit);
            Assert.Equal(TypeNameLine(nameof(IOException)), error);
            Assert.DoesNotContain("書き込みに失敗しました", error);
        }

        [Fact]
        public void RuleViolationWritesNothingToStandardError()
        {
            // 規則違反は内部エラーではない。理由は応答の JSON で返すので、標準エラーには何も出さない
            using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"difficulty\":\"unknown-code\"}"));
            using var output = new MemoryStream();

            var (exit, error) = CaptureStandardError(() => CoreHostRunner.Run(new[] { ProtocolNames.InitCommand }, input, output));

            Assert.Equal(ProtocolLimits.ExitRuleViolation, exit);
            Assert.Empty(error);
        }

        [Fact]
        public void InternalErrorFollowedByAnUnwritableResponseWritesOnlyTheFirstExceptionTypeToStandardError()
        {
            // 読み取りの例外のあとに、内部エラーの応答も書けないときも、標準エラーには最初の例外の型名だけを 1 行で出す（2 行にしない）
            var (exit, error) = CaptureStandardError(() => CoreHostRunner.Run(new[] { ProtocolNames.InitCommand }, new FailingInput(), new FailingOutput()));

            Assert.Equal(ProtocolLimits.ExitInternalError, exit);
            Assert.Equal(TypeNameLine(nameof(IOException)), error);
        }

        [Fact]
        public void RuleViolationThatCannotBeWrittenWritesOnlyTheExceptionTypeToStandardError()
        {
            // 規則違反の応答が書けないときは内部エラー（終了コード 1）になるので、書けなかった例外の型名を標準エラーに出す
            using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"difficulty\":\"unknown-code\"}"));

            var (exit, error) = CaptureStandardError(() => CoreHostRunner.Run(new[] { ProtocolNames.InitCommand }, input, new FailingOutput()));

            Assert.Equal(ProtocolLimits.ExitInternalError, exit);
            Assert.Equal(TypeNameLine(nameof(IOException)), error);
        }

        [Fact]
        public void OpenedStandardStreamsRunTheOperation()
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes($"{{\"{ProtocolNames.Difficulty}\":\"{Code}\"}}"));
            using var output = new MemoryStream();

            var exit = CoreHostRunner.RunOnConsole(new[] { ProtocolNames.InitCommand }, () => input, () => output);

            Assert.Equal(ProtocolLimits.ExitSuccess, exit);
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(output.ToArray()));
            Assert.Equal(Code, document.RootElement.GetProperty(ProtocolNames.Difficulty).GetString());
        }

        [Fact]
        public void SameInputGivesIdenticalBytes()
        {
            var body = ReplayJson(42, (60, 20, 20), (70, 20, 10), (100, 0, 0));

            var first = Run(ProtocolNames.ReplayCommand, body).Text;
            var second = Run(ProtocolNames.ReplayCommand, body).Text;

            Assert.Equal(first, second);
        }

        private static (int Exit, string Text) Run(string command, string json) => Run(command, Encoding.UTF8.GetBytes(json));

        private static (int Exit, string Text) Run(string command, byte[] body)
        {
            var (exit, output) = RunOutput(command, body);
            return (exit, Encoding.UTF8.GetString(output));
        }

        private static (int Exit, byte[] Output) RunOutput(string command, byte[] body)
        {
            using var input = new MemoryStream(body);
            using var output = new MemoryStream();
            var exit = CoreHostRunner.Run(new[] { command }, input, output);
            return (exit, output.ToArray());
        }

        /// <summary>UTF-8 の文字列と、そのままのバイト列をつなぐ（UTF-8 として正しくない入力を作るため）。</summary>
        private static byte[] JoinBytes(string before, byte[] raw, string after) =>
            Encoding.UTF8.GetBytes(before).Concat(raw).Concat(Encoding.UTF8.GetBytes(after)).ToArray();

        /// <summary>UTF-8 として正しくない入力（項目の場所を変えたもの）。</summary>
        public static IEnumerable<object[]> InvalidUtf8Bodies()
        {
            yield return new object[] { JoinBytes("{\"difficulty\":\"no", new byte[] { 0xFF }, "rmal\"}") };
            yield return new object[] { JoinBytes("{\"seed\":\"", new byte[] { 0xFF }, $"\",\"{ProtocolNames.Difficulty}\":\"{Code}\",\"{ProtocolNames.History}\":[]}}") };
            yield return new object[] { JoinBytes("{\"x\":\"", new byte[] { 0xFF }, $"\",\"{ProtocolNames.Difficulty}\":\"{Code}\"}}") };
            yield return new object[] { JoinBytes("{\"", new byte[] { 0xFF }, $"\":1,\"{ProtocolNames.Difficulty}\":\"{Code}\"}}") };
            yield return new object[] { JoinBytes("{\"x\":\"", new byte[] { 0xC0, 0xAF }, $"\",\"{ProtocolNames.Difficulty}\":\"{Code}\"}}") };
            yield return new object[] { JoinBytes("{\"x\":\"", new byte[] { 0xED, 0xA0, 0x80 }, $"\",\"{ProtocolNames.Difficulty}\":\"{Code}\"}}") };
            yield return new object[] { JoinBytes("{\"x\":\"", new byte[] { 0xF4, 0x90, 0x80, 0x80 }, $"\",\"{ProtocolNames.Difficulty}\":\"{Code}\"}}") };
        }

        /// <summary>標準エラーへの出力を捕捉して、操作の終了コードと一緒に返す（テストの間だけ差し替える）。</summary>
        private static (int Exit, string StandardError) CaptureStandardError(Func<int> operation)
        {
            var original = Console.Error;
            using var captured = new StringWriter();
            try
            {
                Console.SetError(captured);
                var exit = operation();
                return (exit, captured.ToString());
            }
            finally
            {
                Console.SetError(original);
            }
        }

        /// <summary>標準エラーに出るはずの 1 行（型名と改行）。</summary>
        private static string TypeNameLine(string typeName) => typeName + Environment.NewLine;

        private static JsonElement ErrorOf(string text)
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.GetProperty(ProtocolNames.Error).Clone();
        }

        private static string ReplayJson(long seed, params (int Living, int Saving, int Invest)[] turns)
        {
            var items = string.Join(",", turns.Select(turn =>
                $"{{\"{ProtocolNames.LivingPct}\":{turn.Living},\"{ProtocolNames.SavingPct}\":{turn.Saving},\"{ProtocolNames.InvestPct}\":{turn.Invest}}}"));
            return $"{{\"{ProtocolNames.Seed}\":{seed},\"{ProtocolNames.Difficulty}\":\"{Code}\",\"{ProtocolNames.History}\":[{items}]}}";
        }

        /// <summary>読み取りの途中で失敗する入力（内部エラーの確認用）。</summary>
        private sealed class FailingInput : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new IOException("読み取りに失敗しました");

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        /// <summary>書き込みに必ず失敗する出力（標準出力が書けない状態の確認用）。</summary>
        private sealed class FailingOutput : Stream
        {
            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() => throw new IOException("書き込みに失敗しました");

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new IOException("書き込みに失敗しました");
        }

        /// <summary>最初の書き込みだけ、指定した長さまで書いてから失敗する出力（応答の書き出しの途中で失敗する状態の確認用）。</summary>
        private sealed class PartiallyFailingOutput : Stream
        {
            private readonly int _bytesBeforeFailure;
            private readonly MemoryStream _written = new MemoryStream();
            private bool _failed;

            public PartiallyFailingOutput(int bytesBeforeFailure)
            {
                _bytesBeforeFailure = bytesBeforeFailure;
            }

            /// <summary>書けた部分（失敗までに受け取ったバイト）。</summary>
            public byte[] Written => _written.ToArray();

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (_failed)
                {
                    _written.Write(buffer, offset, count);
                    return;
                }

                _failed = true;
                _written.Write(buffer, offset, Math.Min(count, _bytesBeforeFailure));
                throw new IOException("書き込みの途中で失敗しました");
            }
        }
    }
}
