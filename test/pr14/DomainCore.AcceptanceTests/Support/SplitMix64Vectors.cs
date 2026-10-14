using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DomainCore.AcceptanceTests.Support
{
    /// <summary>
    /// test/pr14/splitmix64-vectors.json（出力フォルダにコピーされる）を読む。
    /// 値は acceptance.check.mjs が Node.js の BigInt で計算し直して確かめる。読めない・空のときは例外にする（素通りさせない）。
    /// </summary>
    internal sealed class SplitMix64Vectors
    {
        private const string FileName = "splitmix64-vectors.json";
        private const string HexPrefix = "0x";

        private SplitMix64Vectors(IReadOnlyList<UInt64Vector> uint64Vectors, IReadOnlyList<BoundedVector> boundedVectors)
        {
            UInt64Vectors = uint64Vectors;
            BoundedVectors = boundedVectors;
        }

        public IReadOnlyList<UInt64Vector> UInt64Vectors { get; }

        public IReadOnlyList<BoundedVector> BoundedVectors { get; }

        public static SplitMix64Vectors Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, FileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"{FileName} が出力フォルダにありません（csproj の None 項目を確かめてください）", path);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            var uint64Vectors = root.GetProperty("nextUInt64").EnumerateArray()
                .Select(entry => new UInt64Vector(
                    ParseSeed(RequireString(entry, "seed")),
                    entry.GetProperty("values").EnumerateArray().Select(value => ParseHex(RequireString(value))).ToArray()))
                .ToList();
            var boundedVectors = root.GetProperty("nextInt").EnumerateArray()
                .Select(entry => new BoundedVector(
                    ParseSeed(RequireString(entry, "seed")),
                    entry.GetProperty("bound").GetInt32(),
                    entry.GetProperty("values").EnumerateArray().Select(value => value.GetInt32()).ToArray()))
                .ToList();

            if (uint64Vectors.Count == 0 || boundedVectors.Count == 0 || uint64Vectors.Any(v => v.Values.Length == 0) || boundedVectors.Any(v => v.Values.Length == 0))
            {
                throw new InvalidDataException($"{FileName} に空の項目があります");
            }

            return new SplitMix64Vectors(uint64Vectors, boundedVectors);
        }

        private static string RequireString(JsonElement element, string propertyName) =>
            RequireString(element.GetProperty(propertyName));

        private static string RequireString(JsonElement element) =>
            element.GetString() ?? throw new InvalidDataException($"{FileName} に文字列でない値があります: {element}");

        private static long ParseSeed(string text) =>
            long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        private static ulong ParseHex(string text)
        {
            if (!text.StartsWith(HexPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{FileName} の値が 0x で始まっていません: {text}");
            }

            return ulong.Parse(text.Substring(HexPrefix.Length), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }
    }

    internal sealed class UInt64Vector
    {
        public UInt64Vector(long seed, ulong[] values)
        {
            Seed = seed;
            Values = values;
        }

        public long Seed { get; }

        public ulong[] Values { get; }
    }

    internal sealed class BoundedVector
    {
        public BoundedVector(long seed, int bound, int[] values)
        {
            Seed = seed;
            Bound = bound;
            Values = values;
        }

        public long Seed { get; }

        public int Bound { get; }

        public int[] Values { get; }
    }
}
