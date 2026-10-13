using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace DomainCore.Tests
{
    /// <summary>
    /// Domain Core のプロジェクト設定が、Unity で使える条件（netstandard2.1・C# 9 以下）を守っているかを検査する。
    /// csproj の場所は、このファイルのコンパイル時の場所（CallerFilePath）から求める。bin の場所には依存しない。
    /// </summary>
    public class ToolchainTests
    {
        private const string ExpectedTargetFramework = "netstandard2.1";
        private const decimal MaxLangVersion = 9m;
        private const string TargetFrameworkProperty = "TargetFramework";
        private const string TargetFrameworksProperty = "TargetFrameworks";
        private const string LangVersionProperty = "LangVersion";

        [Fact]
        public void TargetFrameworkIsNetStandard21()
        {
            var project = LoadDomainCoreProject();

            Assert.Equal(ExpectedTargetFramework, SinglePropertyValue(project, TargetFrameworkProperty));
            Assert.Empty(PropertyValues(project, TargetFrameworksProperty));
        }

        [Fact]
        public void LangVersionIsAtMostNine()
        {
            var project = LoadDomainCoreProject();
            var value = SinglePropertyValue(project, LangVersionProperty);

            var isNumber = decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var version);

            Assert.True(isNumber, $"LangVersion は数値で指定してください（latest などは不可）: {value}");
            Assert.True(version <= MaxLangVersion, $"LangVersion は {MaxLangVersion} 以下にしてください: {value}");
        }

        private static XDocument LoadDomainCoreProject([CallerFilePath] string sourceFilePath = "")
        {
            var testsDirectory = Path.GetDirectoryName(sourceFilePath);
            if (string.IsNullOrEmpty(testsDirectory))
            {
                throw new InvalidOperationException($"テストのソースの場所を特定できません: {sourceFilePath}");
            }

            var projectPath = Path.GetFullPath(Path.Combine(testsDirectory, "..", "DomainCore", "DomainCore.csproj"));
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException("DomainCore.csproj が見つかりません", projectPath);
            }

            return XDocument.Load(projectPath);
        }

        private static string[] PropertyValues(XDocument project, string name)
        {
            return project.Descendants()
                .Where(element => element.Name.LocalName == name)
                .Select(element => element.Value.Trim())
                .ToArray();
        }

        private static string SinglePropertyValue(XDocument project, string name)
        {
            var values = PropertyValues(project, name);
            if (values.Length != 1)
            {
                throw new InvalidOperationException($"{name} はちょうど 1 つ必要です（{values.Length} 個あります）");
            }

            return values[0];
        }
    }
}
