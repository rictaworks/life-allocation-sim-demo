// Unity WebGL の読み込み。ビルドが無いとき・起動に失敗したときは、画面に理由を表示する（黙って失敗しない）。
import { config } from './config.js';

export class UnityLauncher {
  /**
   * @param {{ doc: Document, win: Window, canvas: HTMLCanvasElement, status: HTMLElement, logger: Console }} deps
   */
  constructor({ doc, win, canvas, status, logger }) {
    this.doc = doc;
    this.win = win;
    this.canvas = canvas;
    this.status = status;
    this.logger = logger;
  }

  /** ローダーを読み込む。読み込みに成功したら起動する。 */
  start() {
    this.setStatus(config.texts.unity.loading);
    const script = this.doc.createElement('script');
    script.src = config.unity.loaderUrl;
    script.addEventListener('load', () => {
      this.launch();
    });
    script.addEventListener('error', () => {
      this.logger.error(config.log.unityLoaderFailed, config.unity.loaderUrl);
      this.setStatus(config.texts.unity.missingBuild);
    });
    this.doc.body.append(script);
  }

  /** ローダーが用意した createUnityInstance で起動する。失敗したら理由を表示する。 */
  async launch() {
    if (typeof this.win.createUnityInstance !== 'function') {
      this.logger.error(config.log.unityCreateMissing);
      this.setStatus(config.texts.unity.loadFailed);
      return;
    }
    try {
      await this.win.createUnityInstance(this.canvas, this.buildConfig(), (progress) => {
        this.showProgress(progress);
      });
      this.setStatus('');
    } catch (error) {
      this.logger.error(config.log.unityLaunchFailed, error);
      this.setStatus(config.texts.unity.loadFailed);
    }
  }

  /** 契約（requirements と Issue の #11）どおりの読み込み先を渡す。 */
  buildConfig() {
    return {
      arguments: [],
      dataUrl: config.unity.dataUrl,
      frameworkUrl: config.unity.frameworkUrl,
      codeUrl: config.unity.codeUrl,
      streamingAssetsUrl: config.unity.streamingAssetsUrl,
      companyName: config.unity.companyName,
      productName: config.unity.productName,
      productVersion: config.unity.productVersion,
    };
  }

  /** @param {number} progress 0 から 1 の値 */
  showProgress(progress) {
    const percent = String(Math.round(progress * 100));
    this.setStatus(config.texts.unity.progress.replace(config.texts.unity.percentToken, percent));
  }

  /** @param {string} text 空文字のときは表示を消す */
  setStatus(text) {
    this.status.textContent = text;
    this.status.hidden = text === '';
  }
}
