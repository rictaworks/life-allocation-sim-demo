// @ts-check
// HTML の妥当性（QC01）。html-validate で src/web の全ページを検査する。設定は src/web/.htmlvalidate.json。
const path = require('node:path');
const { test, expect } = require('@playwright/test');
const { HtmlValidate } = require('html-validate/node');
const support = require('./support');

const PAGES = ['index.html', 'legal.html'];

test.describe('html-validate（QC01）', () => {
  for (const page of PAGES) {
    test(`${page} は html-validate の検査に通る`, async () => {
      const validator = new HtmlValidate();
      const report = await validator.validateFile(path.join(support.WEB_ROOT, page));
      const messages = report.results.flatMap((result) => result.messages.map(
        (message) => `${message.line}:${message.column} ${message.ruleId} ${message.message}`,
      ));
      expect(messages).toEqual([]);
      expect(report.valid).toBe(true);
    });
  }

  test('設定ファイル（.htmlvalidate.json）が存在し、推奨ルールを読み込む', async () => {
    const fs = require('node:fs');
    const raw = fs.readFileSync(path.join(support.WEB_ROOT, '.htmlvalidate.json'), 'utf8');
    const parsed = JSON.parse(raw);
    expect(parsed.extends).toContain('html-validate:recommended');
  });
});
