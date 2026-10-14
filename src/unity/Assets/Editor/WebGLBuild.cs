using System;
using System.IO;
using LifeAllocationSim.Editor.Config;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LifeAllocationSim.Editor
{
    /// <summary>
    /// WebGL のビルド。次のように呼ぶ（tools/build-webgl.sh が行う）：
    /// Unity.exe -batchmode -nographics -quit -executeMethod LifeAllocationSim.Editor.WebGLBuild.Build -outputPath &lt;出力先&gt;
    /// 圧縮は無効にして、出力のファイル名を固定する。Domain Core の DLL は WebGL に含める。
    /// </summary>
    public static class WebGLBuild
    {
        public static void Build()
        {
            var outputPath = ReadArgument(BuildConfig.OutputPathArgument);
            if (string.IsNullOrEmpty(outputPath))
            {
                throw new InvalidOperationException(BuildConfig.OutputPathMissing);
            }

            var folderName = Path.GetFileName(outputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (folderName != BuildConfig.ProductName)
            {
                throw new InvalidOperationException(string.Format(BuildConfig.OutputNameMismatchFormat, BuildConfig.ProductName, outputPath));
            }

            RequirePlugin();
            ConfigurePlugin();
            ConfigurePlayer();
            var scenePath = SceneSetup.EnsureMainScene();

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = outputPath,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            });
            AssetDatabase.SaveAssets();

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    string.Format(BuildConfig.BuildFailedFormat, report.summary.result, report.summary.totalErrors));
            }

            Debug.Log(string.Format(BuildConfig.BuildSucceededFormat, outputPath));
        }

        private static void RequirePlugin()
        {
            var path = Path.Combine(SceneSetup.ProjectRoot(), BuildConfig.PluginAssetPath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(string.Format(BuildConfig.PluginMissingFormat, BuildConfig.PluginAssetPath));
            }
        }

        private static void ConfigurePlugin()
        {
            AssetDatabase.Refresh();
            var importer = AssetImporter.GetAtPath(BuildConfig.PluginAssetPath) as PluginImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(BuildConfig.PluginImportFailed);
            }

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetCompatibleWithPlatform(BuildTarget.WebGL, true);
            importer.SaveAndReimport();
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.productName = BuildConfig.ProductName;
            PlayerSettings.companyName = BuildConfig.CompanyName;
            PlayerSettings.bundleVersion = BuildConfig.ProductVersion;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.WebGL, ApiCompatibilityLevel.NET_Standard);
        }

        /// <summary>コマンドラインの「名前 値」の組から、値を読む。無ければ null。</summary>
        private static string ReadArgument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var index = 0; index < args.Length - 1; index++)
            {
                if (args[index] == name)
                {
                    return args[index + 1];
                }
            }

            return null;
        }
    }
}
