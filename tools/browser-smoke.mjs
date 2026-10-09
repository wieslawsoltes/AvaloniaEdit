#!/usr/bin/env node
// Run from the repository root after installing playwright@1.64.0 in tools.
// This serves only the published files under the same subpath used by Pages.
// Diagnostic state is read-only; all mutations go through actual UI input.
import assert from 'node:assert/strict';
import { createReadStream } from 'node:fs';
import { mkdir, readdir, stat, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const publishRoot = path.resolve(process.argv[2] ?? 'artifacts/browser');
const basePath = '/' + (process.argv[3] ?? 'AvaloniaEdit').replace(/^\/+|\/+$/g, '') + '/';
const output = path.resolve('artifacts/browser-tests');
await mkdir(output, { recursive: true });

async function findIndexes(directory) {
  const files = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) files.push(...await findIndexes(full));
    else if (entry.name === 'index.html') files.push(full);
  }
  return files;
}
const indexes = await findIndexes(publishRoot);
assert.equal(indexes.length, 1, `Expected one published index.html, found ${indexes.join(', ')}`);
const root = path.dirname(indexes[0]);
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm', '.svg': 'image/svg+xml', '.png': 'image/png', '.woff2': 'font/woff2', '.woff': 'font/woff', '.ttf': 'font/ttf', '.dat': 'application/octet-stream', '.dll': 'application/octet-stream' };
const server = createServer(async (request, response) => {
  try {
    const url = new URL(request.url, 'http://127.0.0.1');
    if (!url.pathname.startsWith(basePath)) { response.writeHead(404); response.end('Incorrect application base path'); return; }
    let relative = decodeURIComponent(url.pathname.slice(basePath.length));
    if (!relative || relative.endsWith('/')) relative += 'index.html';
    const target = path.resolve(root, relative);
    if (target !== root && !target.startsWith(root + path.sep)) { response.writeHead(403); response.end(); return; }
    const info = await stat(target);
    if (!info.isFile()) { response.writeHead(404); response.end(); return; }
    response.writeHead(200, { 'Content-Type': mime[path.extname(target)] ?? 'application/octet-stream', 'Content-Length': info.size, 'Cache-Control': 'no-store' });
    if (request.method === 'HEAD') response.end();
    else createReadStream(target).on('error', () => response.destroy()).pipe(response);
  } catch { response.writeHead(404); response.end('Not found'); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;
const log = [];
const failures = [];
const checks = [];
const metrics = {};
let browser, context, page;
let error;
const started = performance.now();
try {
  browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
  context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin });
  await context.tracing.start({ screenshots: true, snapshots: true, sources: true });
  page = await context.newPage();
  page.on('console', message => log.push(`${message.type()}: ${message.text()}`));
  page.on('pageerror', e => { failures.push(e.stack ?? e.message); log.push(`pageerror: ${e.stack ?? e.message}`); });
  page.on('requestfailed', request => log.push(`requestfailed: ${request.url()} ${request.failure()?.errorText}`));
  page.on('response', response => { if (response.status() >= 400) log.push(`http ${response.status()}: ${response.url()}`); });
  const state = () => page.evaluate(() => globalThis.__unoEditTestState);
  const wait = async (predicate, argument) => {
    await page.waitForFunction(predicate, argument, { timeout: 30000 });
    const current = await state();
    assert.equal(current.Error, null, current.Error ?? 'Input error');
    return current;
  };
  const textIs = text => wait(expected => globalThis.__unoEditTestState?.TextPrefix === expected, text);
  const response = await page.goto(origin + basePath + '?smoke=1', { waitUntil: 'domcontentloaded' });
  assert.equal(response.status(), 200);
  await page.waitForFunction(() => globalThis.__unoEditTestState?.Ready, null, { timeout: 120000 });
  metrics.startupMs = performance.now() - started;
  checks.push('published application starts under the repository subpath');
  let current = await state();
  assert.ok(current.Width > 300 && current.Height > 200, 'Editor viewport must have a useful size');
  await page.mouse.click(current.X + 100, current.Y + 10);
  await page.keyboard.type('alpha');
  await textIs('alpha');
  await page.keyboard.press('Enter');
  await page.keyboard.type('beta');
  await textIs('alpha\nbeta');
  checks.push('native typing and newline insertion');
  await page.keyboard.press('Home');
  await page.keyboard.press('Shift+End');
  await wait(() => globalThis.__unoEditTestState?.SelectionLength === 4);
  await page.keyboard.type('second');
  await textIs('alpha\nsecond');
  checks.push('keyboard selection replacement');
  await page.keyboard.press('Control+a');
  await wait(() => globalThis.__unoEditTestState?.SelectionLength === 12);
  await page.keyboard.press('Backspace');
  await textIs('');
  await page.keyboard.press('Control+z');
  await textIs('alpha\nsecond');
  await page.keyboard.press('Control+y');
  await textIs('');
  checks.push('select all, delete, undo and redo');
  const unicode = 'A\t🙂e\u0301\nمرحبا 日本語';
  await page.evaluate(text => navigator.clipboard.writeText(text), unicode);
  await page.keyboard.press('Control+v');
  await textIs(unicode);
  checks.push('asynchronous clipboard paste with Unicode and tab text');
  await page.keyboard.press('Control+Home');
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('Delete');
  await textIs('A\te\u0301\nمرحبا 日本語');
  await page.keyboard.press('Delete');
  await textIs('A\t\nمرحبا 日本語');
  checks.push('surrogate and combining-character deletion through UI input');
  await page.screenshot({ path: path.join(output, 'unicode.png') });
  current = await state();
  const largeStarted = performance.now();
  await page.mouse.click(current.LargeButtonX, current.LargeButtonY);
  current = await wait(() => globalThis.__unoEditTestState?.LineCount === 100000);
  metrics.largeDocumentLoadMs = performance.now() - largeStarted;
  await page.keyboard.press('Control+End');
  current = await wait(() => globalThis.__unoEditTestState?.CaretOffset === globalThis.__unoEditTestState?.TextLength);
  assert.ok(current.CachedLines <= 256, 'Layout cache exceeds its bounded capacity');
  assert.ok(current.VisibleLines > 0 && current.VisibleLines < 100, 'Paint must remain viewport-limited');
  checks.push('100,000-line document loads and navigates to its end with bounded rendering');
  await page.screenshot({ path: path.join(output, 'large-document.png') });
  await page.setViewportSize({ width: 640, height: 700 });
  await wait(() => globalThis.__unoEditTestState?.Width < 650);
  await page.screenshot({ path: path.join(output, 'narrow-layout.png') });
  checks.push('viewport resizes at narrow window widths');
  assert.deepEqual(failures, [], 'Unhandled browser exceptions');
  await writeFile(path.join(output, 'final-state.json'), JSON.stringify(await state(), null, 2));
} catch (caught) {
  error = caught;
  if (page) {
    await page.screenshot({ path: path.join(output, 'failure.png') }).catch(() => {});
    await writeFile(path.join(output, 'failure-state.json'), JSON.stringify(await page.evaluate(() => ({ state: globalThis.__unoEditTestState, title: document.title, body: document.body?.innerText, activeElement: document.activeElement?.outerHTML })).catch(() => null), null, 2));
  }
} finally {
  await writeFile(path.join(output, 'console.log'), log.join('\n'));
  await writeFile(path.join(output, 'results.json'), JSON.stringify({ passed: !error, checks, metrics, error: error?.stack, browser: browser?.version(), publishRoot: root, basePath }, null, 2));
  await context?.tracing.stop({ path: path.join(output, 'trace.zip') }).catch(() => {});
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
console.log(JSON.stringify({ passed: !error, checks, metrics }, null, 2));
if (error) throw error;
