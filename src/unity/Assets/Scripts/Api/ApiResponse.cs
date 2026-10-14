using LifeAllocationSim.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LifeAllocationSim.Api
{
    /// <summary>保存 API の 1 回の応答。応答が無い通信の失敗（NetworkError）も含む。</summary>
    public sealed class ApiResponse
    {
        public ApiResponse(bool networkError, long statusCode, string bodyText, string retryAfter)
        {
            NetworkError = networkError;
            StatusCode = statusCode;
            BodyText = bodyText ?? string.Empty;
            RetryAfter = retryAfter;
        }

        /// <summary>応答が返らなかった（接続の失敗・時間切れ）。</summary>
        public bool NetworkError { get; }

        /// <summary>HTTP の状態コード。応答が無いときは 0。</summary>
        public long StatusCode { get; }

        /// <summary>応答の本文（JSON）。</summary>
        public string BodyText { get; }

        /// <summary>Retry-After の値（無ければ null）。</summary>
        public string RetryAfter { get; }

        /// <summary>2xx の応答か。</summary>
        public bool Succeeded =>
            !NetworkError && StatusCode >= ApiConfig.StatusSuccessMin && StatusCode <= ApiConfig.StatusSuccessMax;

        /// <summary>エラー応答の code。読めないときは null（呼び出し側は汎用の文言にする）。</summary>
        public string ErrorCode
        {
            get
            {
                if (Succeeded || NetworkError)
                {
                    return null;
                }

                try
                {
                    var error = JObject.Parse(BodyText)[ApiConfig.KeyError] as JObject;
                    var code = error?[ApiConfig.KeyCode];
                    return code != null && code.Type == JTokenType.String ? code.Value<string>() : null;
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }

        /// <summary>本文を JSON のオブジェクトとして読む。形が違えば JsonException。</summary>
        public JObject ParseBody() => JObject.Parse(BodyText);
    }
}
