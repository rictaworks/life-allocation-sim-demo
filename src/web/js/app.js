// 画面の起動。同意バナー・GA4・Unity の読み込みを、そのページにある要素に合わせて組み立てる。
import { AnalyticsTag, ConsentBanner, ConsentStore } from './consent.js';
import { requireElement } from './dom.js';
import { UnityLauncher } from './unity-launcher.js';

/**
 * @param {Document} doc
 * @param {Window} win
 */
function startPage(doc, win) {
  const logger = win.console;
  const store = new ConsentStore(() => win.localStorage, logger);
  const analytics = new AnalyticsTag(win, doc, logger);
  new ConsentBanner({ doc, store, analytics, logger }).start();

  if (doc.getElementById('unity-canvas') !== null) {
    new UnityLauncher({
      doc,
      win,
      logger,
      canvas: /** @type {HTMLCanvasElement} */ (requireElement(doc, 'unity-canvas', logger)),
      status: requireElement(doc, 'unity-status', logger),
    }).start();
  }
}

startPage(document, window);
