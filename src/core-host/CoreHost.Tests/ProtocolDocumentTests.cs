using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CoreHost.Config;
using Xunit;

namespace CoreHost.Tests
{
    /// <summary>
    /// Protocol.md（入出力の形式の文書）が、プロトコルの名前とコードをすべて書いていること。
    /// 文書とコードのずれ（名前だけ変えて文書を直し忘れる、など）を見つけるための検査。
    /// </summary>
    public class ProtocolDocumentTests
    {
        [Fact]
        public void DocumentNamesEveryPropertyCommandAndStatus()
        {
            var document = ReadProtocolDocument();

            foreach (var name in ConstantsOf(typeof(ProtocolNames)))
            {
                Assert.True(document.Contains(name, StringComparison.Ordinal), $"Protocol.md に名前がありません: {name}");
            }
        }

        [Fact]
        public void DocumentNamesEveryErrorCode()
        {
            var document = ReadProtocolDocument();

            foreach (var code in ConstantsOf(typeof(ProtocolCodes)))
            {
                Assert.True(document.Contains(code, StringComparison.Ordinal), $"Protocol.md にコードがありません: {code}");
            }
        }

        [Fact]
        public void DocumentStatesTheInputLimitAndExitCodes()
        {
            var document = ReadProtocolDocument();

            Assert.Contains("64KB", document, StringComparison.Ordinal);
            Assert.Contains("終了コード", document, StringComparison.Ordinal);
        }

        private static string[] ConstantsOf(Type type) =>
            type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue()!)
                .ToArray();

        private static string ReadProtocolDocument([CallerFilePath] string testSourcePath = "")
        {
            var testsDirectory = Path.GetDirectoryName(testSourcePath)
                ?? throw new InvalidOperationException($"テストのソースの場所を特定できません: {testSourcePath}");
            var path = Path.GetFullPath(Path.Combine(testsDirectory, "..", "CoreHost", "Protocol.md"));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Protocol.md が見つかりません", path);
            }

            return File.ReadAllText(path);
        }
    }
}
