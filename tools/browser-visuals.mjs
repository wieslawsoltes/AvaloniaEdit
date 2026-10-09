import assert from 'node:assert/strict';
import path from 'node:path';

export async function verifyVisualExtensions({ page, wait, state, textIs, checks, output }) {
  const original = (await state()).TextPrefix;
  await page.keyboard.press('F8');
  await wait(() => globalThis.__unoEditTestState?.FoldedCount > 0 && globalThis.__unoEditTestState?.FoldLabel?.length === 4);
  const folded = await state();
  assert.ok(folded.VisibleLines < folded.LineCount, 'Closed folding must remove physical rows from the viewport');
  assert.equal(folded.TextPrefix, original, 'Folding must not rewrite the source document');
  await page.screenshot({ path: path.join(output, 'native-folding.png') });
  const label = folded.FoldLabel;
  await page.mouse.click(label[0] + label[2] / 2, label[1] + label[3] / 2);
  await wait(() => globalThis.__unoEditTestState?.FoldedCount === 0);
  await textIs(original);
  checks.push('native folding collapses real visual lines and clicking the shaped label restores them without changing text');

  const view = await state();
  await page.mouse.click(view.X + 80, view.Y + 10);
  await page.keyboard.press('Control+Home'); await page.keyboard.press('F9');
  await wait(() => globalThis.__unoEditTestState?.InlineButton?.length === 4 && globalThis.__unoEditTestState.InlineButton[2] > 0);
  const button = (await state()).InlineButton;
  await page.screenshot({ path: path.join(output, 'native-inline-control.png') });
  await page.mouse.click(button[0] + button[2] / 2, button[1] + button[3] / 2);
  await wait(() => globalThis.__unoEditTestState?.InlineClicks === 1);
  await textIs(original);
  checks.push('inline-object generators host an actual clickable Uno control using shaped width and baseline geometry');
  const after = await state();
  await page.mouse.click(after.X + after.Width - 30, after.Y + 12);
}
