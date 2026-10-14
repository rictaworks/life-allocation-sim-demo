using System;

namespace CoreHost
{
    /// <summary>
    /// 入力が不正、または Domain Core の規則に違反した要求（終了コード 2）。
    /// 応答の error.code と error.message にそのまま使う。内部の例外の詳細は含めない。
    /// </summary>
    internal sealed class RequestRejectedException : Exception
    {
        public RequestRejectedException(string code, string message, int? turnNo)
            : base(message)
        {
            Code = code;
            TurnNo = turnNo;
        }

        /// <summary>エラーのコード（ProtocolCodes）。</summary>
        public string Code { get; }

        /// <summary>規則違反が起きたターン番号（1 始まり）。ターンに関係しない場合は null。</summary>
        public int? TurnNo { get; }
    }
}
