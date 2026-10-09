import assert from 'node:assert/strict';
import path from 'node:path';

// Read-only bounds come from the actual native ControlTemplate. Every mutation
// below goes through browser pointer/keyboard input, never the C# object model.
export async function verifySearchPanel({ page, wait, state, textIs, checks, output }) {
  const clickPart = async name => {
    await wait(key => {
      const box = globalThis.__unoEditTestState?.SearchParts?.[key];
      return box && box[2] > 10 && box[3] > 10;
    }, name);
    const box = (await state()).SearchParts[name];
    await page.mouse.click(box[0] + box[2] / 2, box[1] + box[3] / 2);
  };
  const fillPart = async (name, value) => {
    await clickPart(name);
    await page.keyboard.press('Control+a');
    await page.keyboard.press('Backspace');
    await page.keyboard.type(value);
  };
  const countIs = count => wait(expected => globalThis.__unoEditTestState?.SearchResultCount === expected, count);
  const original = 'cat Cat cat scatter\ncat';
  await page.keyboard.press('Control+a');
  await page.keyboard.type('cat Cat cat scatter');
  await page.keyboard.press('Enter');
  await page.keyboard.type('cat');
  await textIs(original);
  await page.keyboard.press('Control+Home');
  await page.keyboard.press('Control+h');
  await wait(() => globalThis.__unoEditTestState?.SearchOpen);
  await wait(() => (globalThis.__unoEditTestState?.SearchParts?.PART_searchTextBox?.[2] ?? 0) > 10);
  // No pointer focus assist: Ctrl+H must focus the first-created native field.
  await page.keyboard.type('cat');
  await countIs(5);
  assert.equal((await state()).TextPrefix, original, 'Typing in the search field must not edit the document');
  await clickPart('PART_matchCase');
  await countIs(4);
  await clickPart('PART_wholeWords');
  await countIs(3);
  checks.push('native search template opens with Ctrl+H and options do not leak text input into the document');

  await clickPart('PART_findNext');
  await wait(() => globalThis.__unoEditTestState?.SelectionStart === 8);
  await page.keyboard.press('Shift+F3');
  await wait(() => globalThis.__unoEditTestState?.SelectionStart === 0);
  await page.screenshot({ path: path.join(output, 'search-panel.png') });
  checks.push('native next/previous buttons and Shift+F3 navigate cached whole-word matches');

  await fillPart('PART_replaceTextBox', '$1');
  await wait(() => globalThis.__unoEditTestState?.ReplacePattern === '$1');
  await clickPart('PART_replaceAll');
  await textIs('$1 Cat $1 scatter\n$1');
  assert.equal((await state()).SearchReplaceCount, 3);
  checks.push('native literal Replace All preserves dollar text and reports the replacement count');

  await clickPart('PART_useRegex');
  await fillPart('PART_searchTextBox', '(?<word>Cat)');
  await countIs(1);
  await fillPart('PART_replaceTextBox', '${word}!');
  await wait(() => globalThis.__unoEditTestState?.ReplacePattern === '${word}!');
  await clickPart('PART_replaceAll');
  await textIs('$1 Cat! $1 scatter\n$1');
  checks.push('native regex replacement expands named captures');

  await fillPart('PART_searchTextBox', '[');
  await wait(() => !!globalThis.__unoEditTestState?.SearchError);
  assert.equal((await state()).SearchResultCount, 0);
  assert.equal((await state()).TextPrefix, '$1 Cat! $1 scatter\n$1');
  await page.screenshot({ path: path.join(output, 'search-invalid-regex.png') });
  await page.keyboard.press('Escape');
  await wait(() => globalThis.__unoEditTestState?.SearchOpen === false);
  await page.keyboard.press('Control+z');
  await textIs('$1 Cat $1 scatter\n$1');
  await page.keyboard.press('Control+z');
  await textIs(original);
  checks.push('invalid regex leaves the document intact and Escape returns focus for grouped undo');

  await page.keyboard.press('Control+Home');
  await page.keyboard.press('Control+f');
  await wait(() => globalThis.__unoEditTestState?.SearchOpen);
  await clickPart('PART_wholeWords');
  await fillPart('PART_searchTextBox', '^');
  await countIs(2);
  const first = (await state()).CaretOffset;
  await page.keyboard.press('F3');
  await wait(offset => globalThis.__unoEditTestState?.CaretOffset !== offset, first);
  assert.equal((await state()).SelectionLength, 0);
  await page.keyboard.press('F3');
  await wait(offset => globalThis.__unoEditTestState?.CaretOffset === offset, first);
  await page.keyboard.press('Escape');
  await wait(() => globalThis.__unoEditTestState?.SearchOpen === false);
  checks.push('zero-width regex navigation advances and wraps without a caret loop');
}
