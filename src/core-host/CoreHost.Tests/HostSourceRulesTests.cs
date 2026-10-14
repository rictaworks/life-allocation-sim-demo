using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using DomainCore.Config;
using DomainCore.Tests.Support;
using Xunit;

namespace CoreHost.Tests
{
    /// <summary>
    /// 再計算ホストのソースの規則。
    /// - 文字列リテラルは Config/ に集める（Config 以外の .cs に文字列リテラル・文字リテラルを書かない）。
    /// - 規則の二重化を防ぐため、難易度・イベントのコードと名前は Domain Core にだけ書く（src/ の Domain Core 以外に書かない）。
    /// </summary>
    public class HostSourceRulesTests
    {
        private const string ConfigDirectoryName = "Config";

        [Fact]
        public void HostCodeHasNoStringLiteralsOutsideConfig()
        {
            var violations = CodeFiles()
                .Where(file => !file.IsConfig)
                .SelectMany(file => CSharpLexer.Tokenize(File.ReadAllText(file.Path))
                    .Where(token => token.Kind == CSharpTokenKind.StringLiteral || token.Kind == CSharpTokenKind.CharLiteral)
                    .Select(token => $"{file.Relative}:{token.Line}: {token.Text}"))
                .ToList();

            Assert.Empty(violations);
        }

        [Fact]
        public void ConfigHoldsTheProtocolStrings()
        {
            // 検査が素通りしていないことの確認：Config には文字列リテラルがある
            var count = CodeFiles()
                .Where(file => file.IsConfig)
                .SelectMany(file => CSharpLexer.Tokenize(File.ReadAllText(file.Path)))
                .Count(token => token.Kind == CSharpTokenKind.StringLiteral);

            Assert.True(count > 0, "Config から文字列リテラルが見つかりません");
        }

        [Fact]
        public void HostFilesDoNotNameDifficultyOrLifeEventCodesOrLabels()
        {
            var words = MasterWords();

            var hits = HostTextFiles()
                .SelectMany(file => words
                    .Where(word => File.ReadAllText(file.Path).Contains(word, StringComparison.Ordinal))
                    .Select(word => $"{file.Relative}: {word}"))
                .ToList();

            Assert.Empty(hits);
        }

        private static string[] MasterWords()
        {
            var difficulties = DifficultyTable.All.SelectMany(difficulty => new[] { difficulty.Code, difficulty.Label });
            var events = LifeEventTable.All.SelectMany(lifeEvent => new[] { lifeEvent.Code, lifeEvent.Label });
            return difficulties.Concat(events).Distinct(StringComparer.Ordinal).ToArray();
        }

        private static IEnumerable<HostFile> HostTextFiles() =>
            HostFilePaths()
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".md", StringComparison.Ordinal) || path.EndsWith(".csproj", StringComparison.Ordinal))
                .Select(path => new HostFile(path, Path.GetRelativePath(HostRoot(), path)));

        private static IReadOnlyList<HostFile> CodeFiles() =>
            HostFilePaths()
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .Where(path => path.EndsWith(".cs", StringComparison.Ordinal))
                .Where(path => path.Contains(Path.DirectorySeparatorChar + "CoreHost" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !path.Contains(Path.DirectorySeparatorChar + "CoreHost.Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .Select(path => new HostFile(path, Path.GetRelativePath(HostRoot(), path)))
                .OrderBy(file => file.Relative, StringComparer.Ordinal)
                .ToList();

        /// <summary>src/core-host の全部（テストのプロジェクトを含む）のファイルの絶対パス。</summary>
        private static IEnumerable<string> HostFilePaths() =>
            Directory.EnumerateFiles(HostRoot(), "*", SearchOption.AllDirectories);

        private static string HostRoot([CallerFilePath] string testSourcePath = "")
        {
            var testsDirectory = Path.GetDirectoryName(testSourcePath)
                ?? throw new InvalidOperationException($"テストのソースの場所を特定できません: {testSourcePath}");
            return Path.GetFullPath(Path.Combine(testsDirectory, ".."));
        }

        private sealed class HostFile
        {
            public HostFile(string path, string relative)
            {
                Path = path;
                Relative = relative;
            }

            public string Path { get; }

            public string Relative { get; }

            public bool IsConfig => Relative.Split(System.IO.Path.DirectorySeparatorChar).Contains(ConfigDirectoryName)
                && Relative.StartsWith("CoreHost" + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }
    }
}
