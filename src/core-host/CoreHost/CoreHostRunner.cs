using System;
using System.IO;
using System.Text.Json;
using CoreHost.Config;

namespace CoreHost
{
    /// <summary>
    /// 操作の振り分けと、応答・終了コード。規則は持たず、Domain Core を呼んで結果を JSON にするだけ。
    /// - 成功：終了コード 0 と結果の JSON。
    /// - 入力不正・規則違反：終了コード 2 と理由コードの JSON（error.code・error.message・必要なら error.turn_no）。
    /// - 内部エラー：終了コード 1 と一般的なコードだけの JSON。標準エラーには、最初に起きた例外の型名だけを 1 行で出す（メッセージ・入力値・スタックトレースは出さない）。
    /// 応答は、いったんメモリに組み立ててから一度に書く（組み立ての途中で失敗しても、出力は始まらない）。
    /// </summary>
    internal static class CoreHostRunner
    {
        /// <summary>args の操作を、input の JSON で実行し、output に応答を書く。終了コードを返す。応答を書けなくても、例外は外へ出さない。</summary>
        public static int Run(string[] args, Stream input, Stream output)
        {
            MemoryStream response;
            try
            {
                response = Execute(args, input);
            }
            catch (RequestRejectedException rejected)
            {
                return ReportRuleViolation(output, rejected);
            }
            catch (Exception error)
            {
                // 内部エラー：例外の詳細（メッセージ・スタックトレース）は応答にも標準エラーにも出さない。標準エラーには、最初の例外の型名だけを 1 行で出す
                WriteStandardErrorTypeName(error);
                return ReportInternalError(output);
            }

            using (response)
            {
                return WriteResponse(response, output);
            }
        }

        /// <summary>
        /// 成功の応答を書く。書き始めたあとに失敗したときは、書きかけの出力の後ろに別の JSON を続けない（出力が 2 つの JSON にならないように）。
        /// そのときの標準出力の内容は保証しない。終了コード 1 で判断する。標準エラーには、例外の型名だけを出す。
        /// </summary>
        private static int WriteResponse(MemoryStream response, Stream output)
        {
            try
            {
                response.WriteTo(output);
                output.Flush();
                return ProtocolLimits.ExitSuccess;
            }
            catch (Exception error)
            {
                // 書けなかった理由は、例外の型名だけを標準エラーに出す（メッセージは出さない）
                WriteStandardErrorTypeName(error);
                return ProtocolLimits.ExitInternalError;
            }
        }

        /// <summary>
        /// 標準入出力を開いてから Run を呼ぶ（Main から呼ぶ）。標準入出力を開く処理も、例外の内側で行う。
        /// 開けなかったとき（例外）は、応答を書けないので標準出力には何も書かず、例外の型名だけを標準エラーに出して、終了コード 1 を返す。
        /// </summary>
        public static int RunOnConsole(string[] args, Func<Stream> openInput, Func<Stream> openOutput)
        {
            try
            {
                return Run(args, openInput(), openOutput());
            }
            catch (Exception error)
            {
                // 標準入出力を開けなかった理由は、例外の型名だけを標準エラーに出す（メッセージは出さない）
                WriteStandardErrorTypeName(error);
                return ProtocolLimits.ExitInternalError;
            }
        }

        private static MemoryStream Execute(string[] args, Stream input)
        {
            var isKnownCommand = args.Length == 1
                && (args[0] == ProtocolNames.InitCommand || args[0] == ProtocolNames.ReplayCommand);
            if (!isKnownCommand)
            {
                throw new RequestRejectedException(ProtocolCodes.CommandUnknown, ProtocolTexts.CommandUnknown, null);
            }

            var body = RequestReader.ReadBody(input);
            using var document = RequestReader.Parse(body);
            var root = document.RootElement;

            return args[0] == ProtocolNames.InitCommand ? Commands.Init(root) : Commands.Replay(root);
        }

        /// <summary>
        /// 規則違反の応答を書き、終了コード 2 を返す。応答が書けないとき（標準出力が閉じている・壊れているとき）は、
        /// 例外を外へ出さず、書けなかった例外の型名を標準エラーに出して、終了コード 1（内部エラー）を返す。
        /// </summary>
        private static int ReportRuleViolation(Stream output, RequestRejectedException rejected)
        {
            try
            {
                WriteError(output, rejected.Code, rejected.Message, rejected.TurnNo);
                return ProtocolLimits.ExitRuleViolation;
            }
            catch (Exception error)
            {
                // 書けなかった理由は、例外の型名だけを標準エラーに出す（メッセージは出さない）
                WriteStandardErrorTypeName(error);
                return ProtocolLimits.ExitInternalError;
            }
        }

        /// <summary>
        /// 内部エラーの応答を書き、終了コード 1 を返す。応答が書けなくても、例外を外へ出さない。
        /// 標準エラーの型名は呼び出し側が最初の例外について出しているので、ここで書けなかった例外の型名は出さない（標準エラーは 1 行にする）。
        /// </summary>
        private static int ReportInternalError(Stream output)
        {
            try
            {
                WriteError(output, ProtocolCodes.InternalError, ProtocolTexts.InternalError, null);
            }
            catch (Exception)
            {
                // 書けなかった理由は出さない（最初の例外の型名は出している。終了コードの 1 で判断する）
            }

            return ProtocolLimits.ExitInternalError;
        }

        /// <summary>
        /// 内部エラーの例外の型名（名前空間を除く）だけを、標準エラーに 1 行で出す。メッセージ・入力値・スタックトレースは出さない。
        /// 標準エラーに書けないときも、例外を外へ出さない（終了コード 1 で内部エラーは分かる）。
        /// </summary>
        private static void WriteStandardErrorTypeName(Exception error)
        {
            try
            {
                Console.Error.WriteLine(error.GetType().Name);
            }
            catch (Exception)
            {
                // 標準エラーにも書けないときは、何も出さない（終了コードの 1 が内部エラーを示す）
            }
        }

        private static void WriteError(Stream output, string code, string message, int? turnNo)
        {
            using var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer))
            {
                json.WriteStartObject();
                json.WriteStartObject(ProtocolNames.Error);
                json.WriteString(ProtocolNames.ErrorCode, code);
                json.WriteString(ProtocolNames.ErrorMessage, message);
                if (turnNo.HasValue)
                {
                    json.WriteNumber(ProtocolNames.TurnNo, turnNo.Value);
                }

                json.WriteEndObject();
                json.WriteEndObject();
            }

            buffer.WriteTo(output);
            output.Flush();
        }
    }
}
