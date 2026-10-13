using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace DomainCore.Tests.Support
{
    /// <summary>
    /// Domain Core のソースファイル（.cs）を列挙する。
    /// 場所は、呼び出し元のソースのコンパイル時の場所（CallerFilePath）から、テストのプロジェクトを探して求める。bin の場所には依存しない。
    /// </summary>
    internal static class DomainCoreSources
    {
        private const string TestsProjectFileName = "DomainCore.Tests.csproj";
        private const string DomainCoreDirectoryName = "DomainCore";
        private const string DomainCoreProjectFileName = "DomainCore.csproj";
        private const string SourcePattern = "*.cs";

        public static string DomainCoreDirectory([CallerFilePath] string callerFilePath = "")
        {
            var directory = Path.GetDirectoryName(callerFilePath);
            while (!string.IsNullOrEmpty(directory) && !File.Exists(Path.Combine(directory, TestsProjectFileName)))
            {
                directory = Path.GetDirectoryName(directory);
            }

            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException($"テストのプロジェクトの場所を特定できません: {callerFilePath}");
            }

            var domainCore = Path.GetFullPath(Path.Combine(directory, "..", DomainCoreDirectoryName));
            if (!File.Exists(Path.Combine(domainCore, DomainCoreProjectFileName)))
            {
                throw new DirectoryNotFoundException($"Domain Core のプロジェクトが見つかりません: {domainCore}");
            }

            return domainCore;
        }

        /// <summary>Domain Core の .cs ファイル（bin・obj の生成物を除く）を、相対パスの順に返す。</summary>
        public static IReadOnlyList<SourceFile> All()
        {
            var root = DomainCoreDirectory();
            return Directory.EnumerateFiles(root, SourcePattern, SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, Path.GetRelativePath(root, path)))
                .Where(file => !file.IsBuildOutput)
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .ToList();
        }
    }

    internal sealed class SourceFile
    {
        private const string ConfigDirectoryName = "Config";
        private const string BinDirectoryName = "bin";
        private const string ObjDirectoryName = "obj";

        public SourceFile(string fullPath, string relativePath)
        {
            FullPath = fullPath;
            RelativePath = relativePath;
        }

        public string FullPath { get; }

        public string RelativePath { get; }

        /// <summary>Domain Core の直下の Config/ にあるファイルなら true。</summary>
        public bool IsConfig => FirstSegment == ConfigDirectoryName;

        public bool IsBuildOutput => FirstSegment == BinDirectoryName || FirstSegment == ObjDirectoryName;

        private string FirstSegment => RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

        public string ReadAllText() => File.ReadAllText(FullPath);
    }
}
