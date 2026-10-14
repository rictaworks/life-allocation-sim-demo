using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Unicode;
using CoreHost.Config;
using DomainCore;

namespace CoreHost
{
    /// <summary>
    /// 要求の読み取り。形式（UTF-8・JSON・型・必須の有無）だけを確かめる。値の規則（範囲・刻み・合計など）は Domain Core が確かめる。
    /// 入力は、解析の前にバイト数で上限を確かめる（上限を超えたら解析せずに拒否する）。
    /// 許可していないプロパティは無視する（ただし、UTF-8 として正しくない入力と、Unicode として正しくない文字列は拒否する）。
    /// </summary>
    internal static class RequestReader
    {
        /// <summary>標準入力を、上限まで読む。上限を 1 バイトでも超えたら、残りを読まずに拒否する。</summary>
        public static byte[] ReadBody(Stream input)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[ProtocolLimits.ReadChunkBytes];
            int read;
            while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > ProtocolLimits.MaxInputBytes)
                {
                    throw new RequestRejectedException(ProtocolCodes.InputTooLarge, ProtocolTexts.InputTooLarge, null);
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }

        /// <summary>
        /// UTF-8 の JSON を解析する。次のときは拒否する（request_invalid）。UTF-8 として正しくないバイト列、JSON として壊れている、
        /// オブジェクトでない、文字列（値と名前）が Unicode として正しくない（対にならない代理文字など）。
        /// UTF-8 の誤りは解析の前に確かめる（解析は文字列の中身を確かめないため）。文字列の誤りは、読む前に確かめる（読むときは例外になるため）。
        /// </summary>
        public static JsonDocument Parse(byte[] body)
        {
            if (!Utf8.IsValid(body))
            {
                throw InvalidRequest();
            }

            var document = ParseJson(body);
            try
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw InvalidRequest();
                }

                RequireReadableStrings(document.RootElement);
            }
            catch (Exception)
            {
                document.Dispose();
                throw;
            }

            return document;
        }

        private static JsonDocument ParseJson(byte[] body)
        {
            var options = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            };

            try
            {
                return JsonDocument.Parse(body, options);
            }
            catch (JsonException)
            {
                throw InvalidRequest();
            }
        }

        /// <summary>
        /// 文字列（値と名前）をすべて読めることを確かめる。対にならない代理文字（"\ud800" など）は、読むときに例外になるので、ここで拒否する。
        /// 読まない項目の値も対象にする（UTF-8 の誤りと同じ扱い）。
        /// </summary>
        private static void RequireReadableStrings(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    RequireReadable(() => element.GetString());
                    break;
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        RequireReadable(() => property.Name);
                        RequireReadableStrings(property.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        RequireReadableStrings(item);
                    }

                    break;
            }
        }

        /// <summary>文字列を読む。読めなかったら（Unicode として正しくないとき）、要求の形式の誤りにする。</summary>
        private static void RequireReadable(Func<string?> read)
        {
            try
            {
                _ = read();
            }
            catch (InvalidOperationException)
            {
                throw InvalidRequest();
            }
        }

        /// <summary>難易度のコード。項目がないか null なら null（未指定）。文字列以外は形式の誤り。</summary>
        public static string? ReadDifficultyCode(JsonElement root)
        {
            if (!root.TryGetProperty(ProtocolNames.Difficulty, out var element) || element.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (element.ValueKind != JsonValueKind.String)
            {
                throw InvalidRequest();
            }

            return element.GetString();
        }

        /// <summary>シード。0 以上の 64 ビット整数（2^63−1 まで）。</summary>
        public static long ReadSeed(JsonElement root)
        {
            if (!root.TryGetProperty(ProtocolNames.Seed, out var element) || !element.TryGetInt64(out var seed) || seed < 0)
            {
                throw InvalidRequest();
            }

            return seed;
        }

        /// <summary>配分の履歴。各要素は、生活費・貯蓄・投資の整数の %（任意の桁数。範囲・刻み・合計は Domain Core が確かめる）。</summary>
        public static AllocationInput[] ReadHistory(JsonElement root)
        {
            if (!root.TryGetProperty(ProtocolNames.History, out var list) || list.ValueKind != JsonValueKind.Array)
            {
                throw InvalidRequest();
            }

            var allocations = new List<AllocationInput>();
            foreach (var item in list.EnumerateArray())
            {
                allocations.Add(ReadAllocation(item));
            }

            return allocations.ToArray();
        }

        private static AllocationInput ReadAllocation(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw InvalidRequest();
            }

            return new AllocationInput(
                ReadPercent(item, ProtocolNames.LivingPct),
                ReadPercent(item, ProtocolNames.SavingPct),
                ReadPercent(item, ProtocolNames.InvestPct));
        }

        /// <summary>
        /// 配分の 1 項目（整数の %）。数の字面を、符号付きの整数として読む。桁数には上限を置かない（範囲の外の値も、規則の理由で拒否するため）。
        /// 数でない値（文字列・真偽値・null など）と、整数でない字面（小数点や指数を含むもの）は request_invalid で拒否する。
        /// </summary>
        private static BigInteger ReadPercent(JsonElement item, string name)
        {
            // TryGetInt32 などは、数でない値に対して例外（InvalidOperationException）を投げるため、先に数かどうかを確かめる
            if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            {
                throw InvalidRequest();
            }

            // 符号と数字だけを許す（小数点・指数は読めず、拒否される）
            if (!BigInteger.TryParse(value.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var percent))
            {
                throw InvalidRequest();
            }

            return percent;
        }

        private static RequestRejectedException InvalidRequest() =>
            new RequestRejectedException(ProtocolCodes.RequestInvalid, ProtocolTexts.RequestInvalid, null);
    }
}
