// 画面の文言と設定値。JavaScript の文言・URL・ID はこのファイルにだけ書く（テストで検査する）。
// 値は公開される。個人情報や秘密情報は書かない。
export const config = Object.freeze({
  analytics: Object.freeze({
    measurementId: 'G-C04W1XKS16',
    scriptUrl: 'https://www.googletagmanager.com/gtag/js',
    consentStorageKey: 'life-allocation-sim.analytics-consent',
    consentGranted: 'granted',
    consentDenied: 'denied',
  }),
  unity: Object.freeze({
    productName: 'LifeAllocationSim',
    companyName: 'rictaworks',
    productVersion: '0.1.0',
    loaderUrl: 'unity/Build/LifeAllocationSim.loader.js',
    dataUrl: 'unity/Build/LifeAllocationSim.data',
    frameworkUrl: 'unity/Build/LifeAllocationSim.framework.js',
    codeUrl: 'unity/Build/LifeAllocationSim.wasm',
    streamingAssetsUrl: 'unity/StreamingAssets',
  }),
  texts: Object.freeze({
    unity: Object.freeze({
      loading: 'シミュレーションを読み込んでいます。',
      progress: 'シミュレーションを読み込んでいます（{percent}%）。',
      percentToken: '{percent}',
      missingBuild: 'シミュレーションの表示データが見つからないため、表示できません。Unity のビルドを配置すると、ここに表示されます。',
      loadFailed: 'シミュレーションを読み込めませんでした。ページを再読み込みしてください。',
    }),
    consent: Object.freeze({
      readFailed: '同意の選択を読み込めませんでした。同意バナーを表示します。',
      saveFailed: '同意の選択を保存できませんでした。この表示の間だけ有効です。',
    }),
  }),
  log: Object.freeze({
    missingElement: '画面の必要な要素が見つかりません。',
    consentValueInvalid: '保存されていた同意の値が不正のため、無視します。',
    analyticsLoadFailed: 'アクセス解析のスクリプトを読み込めませんでした。',
    unityLoaderFailed: 'Unity のローダーを読み込めませんでした。ビルドが unity/Build/ に配置されているか確認してください。',
    unityCreateMissing: 'Unity の起動関数（createUnityInstance）が見つかりません。',
    unityLaunchFailed: 'Unity の起動に失敗しました。',
  }),
});
