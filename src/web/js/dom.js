// 画面の要素の取得。無いときは例外にする（画面の作りの誤りを黙って進めない）。
import { config } from './config.js';

/**
 * id の要素を取得する。
 * @param {Document} doc
 * @param {string} id
 * @param {Console} logger
 * @returns {HTMLElement}
 */
export function requireElement(doc, id, logger) {
  const element = doc.getElementById(id);
  if (element === null) {
    logger.error(config.log.missingElement, id);
    throw new Error(`${config.log.missingElement} (#${id})`);
  }
  return element;
}
