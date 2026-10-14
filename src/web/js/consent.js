// 同意（Consent Mode v2）と GA4 の読み込み。既定は拒否にし、同意の選択はこのブラウザの localStorage に保存する。
// 保存や読み取りに失敗しても画面は止めず、コンソールに出す（黙って失敗させない）。
import { config } from './config.js';
import { requireElement } from './dom.js';

/**
 * 同意の選択を localStorage に読み書きする。
 * 保存先の取得そのものが例外を投げる環境（保存を禁止した環境など）にも対応する。
 */
export class ConsentStore {
  /**
   * @param {() => Storage} getStorage 保存先を返す関数（例外が出うるので、呼び出しは try の中で行う）
   * @param {Console} logger
   */
  constructor(getStorage, logger) {
    this.getStorage = getStorage;
    this.logger = logger;
  }

  /**
   * 保存済みの選択を返す。未選択・読み取り失敗・不正な値のときは null を返す。
   * @returns {string | null}
   */
  read() {
    try {
      const value = this.getStorage().getItem(config.analytics.consentStorageKey);
      if (value === null) return null;
      if (value === config.analytics.consentGranted || value === config.analytics.consentDenied) {
        return value;
      }
      this.logger.warn(config.log.consentValueInvalid);
      return null;
    } catch (error) {
      this.logger.warn(config.texts.consent.readFailed, error);
      return null;
    }
  }

  /**
   * 選択を保存する。成功したら true、失敗したら false を返す（失敗はコンソールに出す）。
   * @param {string} value
   * @returns {boolean}
   */
  write(value) {
    try {
      this.getStorage().setItem(config.analytics.consentStorageKey, value);
      return true;
    } catch (error) {
      this.logger.warn(config.texts.consent.saveFailed, error);
      return false;
    }
  }
}

/**
 * GA4（gtag.js）の読み込みと同意の更新。
 * 既定は全項目「拒否」にしてから、保存済みの選択を反映し、測定 ID を読み込む。
 */
export class AnalyticsTag {
  /**
   * @param {Window} win
   * @param {Document} doc
   * @param {Console} logger
   */
  constructor(win, doc, logger) {
    this.win = win;
    this.doc = doc;
    this.logger = logger;
    this.win.dataLayer = this.win.dataLayer || [];
    this.gtag = createGtag(this.win);
  }

  /**
   * @param {string | null} storedValue 保存済みの選択（未選択なら null）
   */
  start(storedValue) {
    this.gtag('consent', 'default', {
      ad_storage: 'denied',
      ad_user_data: 'denied',
      ad_personalization: 'denied',
      analytics_storage: 'denied',
    });
    if (storedValue === config.analytics.consentGranted) {
      this.update(config.analytics.consentGranted);
    }
    this.gtag('js', new Date());
    this.gtag('config', config.analytics.measurementId);
    this.loadScript();
  }

  /**
   * 分析の同意を更新する。広告関連は常に拒否のままにする。
   * @param {string} value config.analytics.consentGranted か consentDenied
   */
  update(value) {
    this.gtag('consent', 'update', { analytics_storage: value });
  }

  loadScript() {
    const script = this.doc.createElement('script');
    script.async = true;
    script.src = `${config.analytics.scriptUrl}?id=${config.analytics.measurementId}`;
    script.addEventListener('error', () => {
      this.logger.warn(config.log.analyticsLoadFailed);
    });
    this.doc.head.append(script);
  }
}

/**
 * gtag.js が読む形（arguments オブジェクト）で dataLayer に積む関数を作る。
 * @param {Window} win
 * @returns {(...args: unknown[]) => void}
 */
function createGtag(win) {
  return function gtag() {
    win.dataLayer.push(arguments);
  };
}

/**
 * 同意バナーと「設定を変更」ボタンを制御する。
 */
export class ConsentBanner {
  /**
   * @param {{ doc: Document, store: ConsentStore, analytics: AnalyticsTag, logger: Console }} deps
   */
  constructor({ doc, store, analytics, logger }) {
    this.doc = doc;
    this.store = store;
    this.analytics = analytics;
    this.logger = logger;
    this.banner = requireElement(doc, 'consent-banner', logger);
    this.title = requireElement(doc, 'consent-title', logger);
    this.reopen = requireElement(doc, 'consent-reopen', logger);
    this.main = requireElement(doc, 'main', logger);
  }

  start() {
    const stored = this.store.read();
    this.analytics.start(stored);
    for (const button of this.banner.querySelectorAll('[data-consent-choice]')) {
      button.addEventListener('click', () => this.choose(button.dataset.consentChoice ?? ''));
    }
    this.reopen.addEventListener('click', () => this.showBanner(true));
    this.render(stored !== null);
  }

  /**
   * 選択を保存し、分析の同意を更新して、バナーを閉じる。
   * @param {string} value
   */
  choose(value) {
    this.store.write(value);
    this.analytics.update(value);
    this.render(true);
    this.main.focus();
  }

  /**
   * @param {boolean} focusTitle 再表示のときは見出しへ移動する
   */
  showBanner(focusTitle) {
    this.render(false);
    if (focusTitle) this.title.focus();
  }

  /**
   * @param {boolean} decided 選択済みのとき true（バナーを隠し、設定の変更ボタンを出す）
   */
  render(decided) {
    this.banner.hidden = decided;
    this.reopen.hidden = !decided;
  }
}

