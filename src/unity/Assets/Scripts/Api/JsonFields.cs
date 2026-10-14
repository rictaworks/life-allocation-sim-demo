using System;
using LifeAllocationSim.Config;
using Newtonsoft.Json.Linq;

namespace LifeAllocationSim.Api
{
    /// <summary>保存 API の応答から項目を読む。想定と違えば FormatException（呼び出し側で汎用の文言にする）。</summary>
    public static class JsonFields
    {
        public static JToken Token(JObject source, string key)
        {
            var token = source[key];
            if (token == null)
            {
                throw new FormatException(string.Format(LogTexts.FieldInvalid, key));
            }

            return token;
        }

        public static JObject AsObject(JToken token, string key)
        {
            if (token is JObject value)
            {
                return value;
            }

            throw new FormatException(string.Format(LogTexts.FieldInvalid, key));
        }

        public static JObject Object(JObject source, string key) => AsObject(Token(source, key), key);

        public static JArray Array(JObject source, string key)
        {
            if (Token(source, key) is JArray value)
            {
                return value;
            }

            throw new FormatException(string.Format(LogTexts.FieldInvalid, key));
        }

        public static long Integer(JObject source, string key)
        {
            var token = Token(source, key);
            if (token.Type != JTokenType.Integer)
            {
                throw new FormatException(string.Format(LogTexts.FieldInvalid, key));
            }

            return token.Value<long>();
        }

        public static int Int32(JObject source, string key)
        {
            var value = Integer(source, key);
            if (value < int.MinValue || value > int.MaxValue)
            {
                throw new FormatException(string.Format(LogTexts.FieldInvalid, key));
            }

            return (int)value;
        }

        public static string String(JObject source, string key)
        {
            var token = Token(source, key);
            if (token.Type != JTokenType.String)
            {
                throw new FormatException(string.Format(LogTexts.FieldInvalid, key));
            }

            return token.Value<string>();
        }
    }
}
