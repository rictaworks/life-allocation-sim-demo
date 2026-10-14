namespace LifeAllocationSim.Editor.Config
{
    /// <summary>ビルドの設定と文言（Editor 側の設定ファイル）。値は web/js/config.js と一致させる（tests が検査する）。</summary>
    public static class BuildConfig
    {
        public const string ProductName = "LifeAllocationSim";
        public const string CompanyName = "rictaworks";
        public const string ProductVersion = "0.1.0";

        public const string OutputPathArgument = "-outputPath";
        public const string AssetsFolder = "Assets";
        public const string ScenesFolderPath = "Assets/Scenes";
        public const string ScenesFolderName = "Scenes";
        public const string MainScenePath = "Assets/Scenes/Main.unity";
        public const string PluginAssetPath = "Assets/Plugins/DomainCore/DomainCore.dll";
        public const string AppObjectName = "LifeAllocationSim";

        public const string OutputPathMissing = "出力先が指定されていません（-outputPath）。";
        public const string OutputNameMismatchFormat = "出力フォルダの名前は {0} にしてください（Unity は出力のファイル名をフォルダ名から決めます）：{1}";
        public const string PluginMissingFormat = "Domain Core の DLL が見つかりません：{0}。tools/copy-domain-core.sh を実行してください。";
        public const string PluginImportFailed = "Domain Core の DLL の取り込み設定に失敗しました。";
        public const string BuildFailedFormat = "WebGL のビルドに失敗しました：{0}（エラー {1} 件）";
        public const string BuildSucceededFormat = "WebGL のビルドが完了しました：{0}";
    }
}
