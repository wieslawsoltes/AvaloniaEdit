import assert from 'node:assert/strict';
import path from 'node:path';

export async function verifyCompletionSnippets({ page, wait, state, textIs, checks, output }) {
  await page.keyboard.press('Control+a'); await page.keyboard.press('Backspace'); await textIs('');
  await page.keyboard.press('Control+Space');
  await wait(() => globalThis.__unoEditTestState?.CompletionOpen);
  await page.keyboard.type('Con');
  await wait(() => globalThis.__unoEditTestState?.CompletionSelected === 'Console');
  await page.screenshot({ path: path.join(output, 'native-completion.png') });
  await page.keyboard.press('Enter'); await textIs('Console');
  await wait(() => !globalThis.__unoEditTestState?.CompletionOpen);
  await page.keyboard.press('Control+z'); await textIs('Con');
  checks.push('native completion filters without stealing focus and inserts one undoable replacement');

  await page.keyboard.press('Control+a'); await page.keyboard.press('Backspace'); await textIs('');
  await page.keyboard.type('fo'); await page.keyboard.press('Control+Space');
  await wait(() => globalThis.__unoEditTestState?.CompletionSelected === 'for');
  await page.keyboard.press('Enter');
  await wait(() => globalThis.__unoEditTestState?.SnippetActive && globalThis.__unoEditTestState?.SelectionLength === 1);
  await page.keyboard.type('index');
  await wait(() => globalThis.__unoEditTestState?.TextPrefix.startsWith('for (int index = 0; index < count; index++)'));
  await page.keyboard.press('Tab'); await wait(() => globalThis.__unoEditTestState?.SelectionLength === 5);
  await page.keyboard.type('10');
  await wait(() => globalThis.__unoEditTestState?.TextPrefix.startsWith('for (int index = 0; index < 10; index++)'));
  await page.keyboard.press('Shift+Tab'); await wait(() => globalThis.__unoEditTestState?.SelectionLength === 5);
  await page.keyboard.type('n');
  await wait(() => globalThis.__unoEditTestState?.TextPrefix.startsWith('for (int n = 0; n < 10; n++)'));
  await page.screenshot({ path: path.join(output, 'linked-snippet.png') });
  const beforeExit = (await state()).TextPrefix;
  await page.keyboard.press('Enter'); await wait(() => !globalThis.__unoEditTestState?.SnippetActive);
  await textIs(beforeExit);
  checks.push('completion starts original linked snippets; Tab and Shift+Tab navigate fields; Enter finishes without inserting a newline');

  await page.keyboard.press('Control+Shift+Space'); await wait(() => globalThis.__unoEditTestState?.InsightOpen);
  await page.keyboard.press('ArrowDown'); await wait(() => globalThis.__unoEditTestState?.InsightIndex === 1);
  await page.keyboard.press('ArrowUp'); await wait(() => globalThis.__unoEditTestState?.InsightIndex === 0);
  await page.keyboard.press('ArrowUp'); await wait(() => globalThis.__unoEditTestState?.InsightIndex === 2);
  await page.screenshot({ path: path.join(output, 'native-overloads.png') });
  await page.keyboard.press('Escape'); await wait(() => !globalThis.__unoEditTestState?.InsightOpen);
  assert.equal((await state()).TextPrefix, beforeExit);
  checks.push('native overload popup navigates and wraps without changing the document');
}
