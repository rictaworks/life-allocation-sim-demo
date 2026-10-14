using System.IO;
using LifeAllocationSim.Editor.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LifeAllocationSim.Editor
{
    /// <summary>ビルドに入れるシーンを用意する。シーンは起動点（AppBootstrap）を 1 つ置くだけで、画面の部品はコードで作る。</summary>
    public static class SceneSetup
    {
        /// <summary>シーンが無ければ作り、ビルド設定に入れる。戻り値はシーンのパス（Assets/ から始まる）。</summary>
        public static string EnsureMainScene()
        {
            var path = BuildConfig.MainScenePath;
            if (!File.Exists(Path.Combine(ProjectRoot(), path)))
            {
                if (!AssetDatabase.IsValidFolder(BuildConfig.ScenesFolderPath))
                {
                    AssetDatabase.CreateFolder(BuildConfig.AssetsFolder, BuildConfig.ScenesFolderName);
                }

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject(BuildConfig.AppObjectName).AddComponent<AppBootstrap>();
                EditorSceneManager.SaveScene(scene, path);
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            return path;
        }

        /// <summary>Unity プロジェクトのフォルダ（Assets の 1 つ上）。</summary>
        public static string ProjectRoot() => Path.GetDirectoryName(Application.dataPath);
    }
}
