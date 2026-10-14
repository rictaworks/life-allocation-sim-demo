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
    /// - 内部エラー：終了コード 1 と一般的なコードだけの JSON。例外の詳細は出さない。
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
                return ReportError(output, rejected.Code, rejected.Message, rejected.TurnNo, ProtocolLimits.ExitRuleViolation);
            }
            catch (Exception)
            {
                // 内部エラー：例外の型・メッセージ・スタックトレースは、応答にも標準エラーにも出さない
                return ReportError(output, ProtocolCodes.InternalError, ProtocolTexts.InternalError, null, ProtocolLimits.ExitInternalError);
            }

            using (response)
            {
                return WriteResponse(response, output);
            }
        }

        /// <summary>
        /// 成功の応答を書く。書き始めたあとに失敗したときは、書きかけの出力の後ろに別の JSON を続けない（出力が 2 つの JSON にならないように）。
        /// そのときの標準出力の内容は保証しない。終了コード 1 で判断する。標準エラーにも何も出さない。
        /// </summary>
        private static int WriteResponse(MemoryStream response, Stream output)
        {
            try
            {
                response.WriteTo(output);
                output.Flush();
                return ProtocolLimits.ExitSuccess;
            }
            catch (Exception)
            {
                // 書けなかった理由（例外の型・メッセージ）は、応答にも標準エラーにも出さない
                return ProtocolLimits.ExitInternalError;
            }
        }

        /// <summary>
        /// 標準入出力を開いてから Run を呼ぶ（Main から呼ぶ）。標準入出力を開く処理も、例外の内側で行う。
        /// 開けなかったとき（例外）は、応答を書けないので何も書かず、例外の詳細を標準エラーにも出さずに、終了コード 1 を返す。
        /// </summary>
        public static int RunOnConsole(string[] args, Func<Stream> openInput, Func<Stream> openOutput)
        {
            try
            {
                return Run(args, openInput(), openOutput());
            }
            catch (Exception)
            {
                // 標準入出力を開けなかった理由（例外の型・メッセージ）は、応答にも標準エラーにも出さない
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
        /// エラーの応答を書き、終了コードを返す。応答が書けないとき（標準出力が閉じている・壊れているとき）は、
        /// 例外を外へ出さず、終了コード 1（内部エラー）だけを返す。理由の JSON が出せない規則違反も、同じく 1 にする。
        /// </summary>
        private static int ReportError(Stream output, string code, string message, int? turnNo, int exitCode)
        {
            try
            {
                WriteError(output, code, message, turnNo);
                return exitCode;
            }
            catch (Exception)
            {
                // 書けなかった理由（例外の型・メッセージ）は、応答にも標準エラーにも出さない
                return ProtocolLimits.ExitInternalError;
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
