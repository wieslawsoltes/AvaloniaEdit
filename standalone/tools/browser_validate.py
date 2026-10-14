#!/usr/bin/env python3
"""Validate the real published Scribe Avalonia WASM application, not a substitute HTML editor."""
from __future__ import annotations
import functools
import http.server
import json
import os
from pathlib import Path
import sys
import threading
import time
import traceback
from playwright.sync_api import sync_playwright
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'artifacts/validation'
OUTPUT.mkdir(parents=True, exist_ok=True)
PUBLISH = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else ROOT / 'artifacts/browser'
roots = [p.parent for p in PUBLISH.rglob('index.html') if (p.parent / '_framework/dotnet.js').is_file()]
if not roots:
    raise SystemExit('Missing published Avalonia WASM app. Source HTML is not a valid test target.')
class Handler(http.server.SimpleHTTPRequestHandler):
    extensions_map = {**http.server.SimpleHTTPRequestHandler.extensions_map, '.wasm': 'application/wasm', '.js': 'text/javascript', '.mjs': 'text/javascript'}
    def end_headers(self):
        self.send_header('Cross-Origin-Opener-Policy', 'same-origin')
        self.send_header('Cross-Origin-Embedder-Policy', 'require-corp')
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()
    def log_message(self, *_args):
        pass
server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(Handler, directory=str(roots[0])))
threading.Thread(target=server.serve_forever, daemon=True).start()
results, errors, console, resource_failures = [], [], [], []
metrics = {}
with sync_playwright() as pw:
    browser = pw.chromium.launch(headless=True, args=['--no-sandbox', '--disable-dev-shm-usage', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
    context = browser.new_context(viewport={'width': 1440, 'height': 960}, device_scale_factor=1, permissions=['clipboard-read', 'clipboard-write'])
    page = context.new_page()
    page.set_default_timeout(20000)
    page.on('pageerror', lambda e: errors.append(str(e)))
    page.on('console', lambda m: console.append(m.type + ': ' + m.text))
    page.on('response', lambda r: resource_failures.append(f'{r.status} {r.url}') if r.status >= 400 else None)
    def state():
        return page.evaluate('() => window.scribe.state()')
    def command(name, argument=''):
        if not isinstance(argument, str):
            argument = json.dumps(argument, ensure_ascii=False)
        raw = page.evaluate('async ([name, argument]) => await window.scribe.execute(name, argument)', [name, argument])
        page.wait_for_timeout(80)
        return json.loads(raw)
    def wait(expression, timeout=20000):
        page.wait_for_function('() => ' + expression, timeout=timeout)
    def check(value, reason):
        if not value:
            raise AssertionError(reason)
    def text(expected):
        page.wait_for_function('(text) => window.scribe.state().text === text', arg=expected, timeout=5000)
    def reset(value='', language='Plain Text'):
        command('document', '0')
        command('reset', value)
        command('language', language)
        command('focus')
        page.wait_for_timeout(120)
    def bounds(name):
        return page.evaluate('name => window.scribe.bounds(name)', name)
    def click(name):
        b = bounds(name)
        check(b['width'] > 0 and b['height'] > 0, 'Empty bounds: ' + name)
        page.mouse.click(b['x'] + b['width']/2, b['y'] + b['height']/2)
        page.wait_for_timeout(180)
    def point(offset):
        return page.evaluate('offset => window.scribe.point(offset)', offset)
    def click_offset(offset):
        p = point(offset)
        page.mouse.click(p['x'] + .1, p['y'])
    def screenshot(name):
        page.screenshot(path=str(OUTPUT / name))
    def test(name, fn):
        start = time.perf_counter()
        try:
            fn()
            results.append({'name': name, 'passed': True, 'milliseconds': round((time.perf_counter()-start)*1000, 2)})
            print('PASS', name, flush=True)
        except Exception:
            failure = {'name': name, 'passed': False, 'error': traceback.format_exc()}
            try:
                failure['state'] = state()
                screenshot(f'failure-{len(results)+1:02}.png')
            except Exception:
                pass
            results.append(failure)
            print('FAIL', name, failure['error'], flush=True)
    try:
        start = time.perf_counter()
        page.goto(f'http://127.0.0.1:{server.server_port}/?test=1', wait_until='domcontentloaded', timeout=120000)
        wait('window.scribe?.ready || window.scribeError', 120000)
        failure = page.evaluate('() => window.scribeError || null')
        if failure:
            raise RuntimeError(failure)
        wait('window.scribe.state().visualLines > 0', 60000)
        metrics['coldStartSeconds'] = round(time.perf_counter()-start, 3)
        original = state()['text']
        def boot():
            check(page.locator('canvas').count() > 0, 'No Avalonia rendering canvas')
            current = state()
            check(current['viewportType'] == 'Scribe.Avalonia.EditorViewport', 'Wrong editing surface')
            check(current['viewportBase'] == 'Avalonia.Controls.Control', 'Viewport wraps an editor class')
            check(current['undoSourceAssembly'] == 'Scribe.Core', 'Document engine is not independent')
            check(current['visualLines'] > 10, 'No visible document text')
            screenshot('preview-dark.png')
            image = Image.open(OUTPUT / 'preview-dark.png').convert('RGB')
            check(len(image.getcolors(image.width*image.height)) > 100, 'Blank screenshot')
        test('actual WASM canvas and original engine identity', boot)
        def typing():
            reset()
            click_offset(0)
            page.keyboard.type('hello editor', delay=12)
            text('hello editor')
            check(state()['dirty'], 'Typing did not mark the original document dirty')
        test('pointer focus and keyboard typing', typing)
        def undo_redo():
            reset()
            page.keyboard.type('hello', delay=12)
            page.keyboard.press('Control+z')
            text('')
            page.keyboard.press('Control+y')
            text('hello')
        test('contiguous typing undo and redo', undo_redo)
        def selection():
            reset('replace me')
            page.keyboard.press('Control+a')
            page.keyboard.type('new text')
            text('new text')
        test('native select-all replacement', selection)
        def newline():
            reset('    if (x) {', 'C#')
            command('caret', '12')
            page.keyboard.press('Enter')
            text('    if (x) {\n        ')
        test('newline and automatic indentation', newline)
        def unicode_commit():
            reset()
            content = 'Zażółć 🌍 日本語'
            cdp = context.new_cdp_session(page)
            try:
                count = len(content.encode('utf-16-le'))//2
                cdp.send('Input.imeSetComposition', {'text': content, 'selectionStart': count, 'selectionEnd': count})
                page.wait_for_timeout(100)
                check(state()['text'] == '', 'Preedit mutated the document before commit')
                check(state()['preedit'] == content, 'Original IME client did not receive preedit')
                screenshot('ime-preedit.png')
                cdp.send('Input.insertText', {'text': content})
            finally:
                cdp.detach()
            text(content)
            check(state()['preedit'] == '', 'Preedit was not cleared after commit')
        test('IME preedit and Unicode composition commit', unicode_commit)
        def graphemes():
            reset('a🌍é👩‍👩‍👧‍👦z')
            command('caret', '1')
            for expected in (3, 5, 16):
                page.keyboard.press('ArrowRight')
                check(state()['caret'] == expected, f'Grapheme navigation split a cluster at {expected}')
            page.keyboard.press('Backspace')
            text('a🌍éz')
        test('emoji, combining marks and grapheme deletion', graphemes)
        def crlf():
            reset('a\r\nb')
            command('caret', '3')
            page.keyboard.press('Backspace')
            text('ab')
            page.keyboard.press('Control+z')
            text('a\r\nb')
        test('CRLF deletion is indivisible and undoable', crlf)
        def readonly():
            reset('locked')
            command('readonly', 'true')
            page.keyboard.press('Control+a')
            page.keyboard.type('X')
            page.keyboard.press('Backspace')
            command('duplicate')
            text('locked')
            command('readonly', 'false')
        test('read-only blocks native editing and commands', readonly)
        def find_ui():
            reset('alpha beta alpha')
            page.keyboard.press('Control+f')
            wait('window.scribe.state().searchOpen')
            page.wait_for_timeout(150)
            page.keyboard.type('alpha')
            wait('window.scribe.state().searchMatches === 2')
            page.keyboard.press('Escape')
            wait('!window.scribe.state().searchOpen')
        test('compiled find-bar bindings and keyboard focus', find_ui)
        def find_pointer():
            reset('one two one')
            click('FindButton')
            click('find-query')
            page.keyboard.press('Control+a')
            page.keyboard.type('one')
            wait('window.scribe.state().searchMatches === 2')
        test('pointer-operated search chrome', find_pointer)
        def options():
            reset('cat Cat scatter cat')
            command('find', {'pattern': 'cat', 'case': True, 'word': True})
            check(state()['searchMatches'] == 2, 'Whole-word/case filtering failed')
        test('case-sensitive whole-word search', options)
        def regex_replace():
            reset('item12 item34')
            command('replace-all', {'pattern': r'item(\d+)', 'replacement': 'value-$1', 'regex': True})
            text('value-12 value-34')
            command('undo')
            text('item12 item34')
        test('regex captures and transactional replace-all', regex_replace)
        def scoped_search():
            reset('cat cat cat')
            command('select', '4:7')
            command('replace-all', {'pattern': 'cat', 'replacement': 'dog', 'selection': True})
            text('cat dog cat')
        test('anchored selection-only replacement', scoped_search)
        def invalid_regex():
            reset('abc')
            command('find', {'pattern': '[', 'regex': True})
            check(state()['searchMatches'] == 0, 'Invalid regex yielded results')
            check('matches' not in state()['searchStatus'], 'Invalid regex was not reported')
        test('invalid regular expression is surfaced', invalid_regex)
        def zero_width():
            reset('aaa')
            command('find', {'pattern': '(?=a)', 'regex': True})
            carets = []
            for _ in range(3):
                command('next-match')
                carets.append(state()['caret'])
            check(len(set(carets)) == 3, 'Zero-width navigation is stuck')
        test('zero-width navigation wraps and progresses', zero_width)
        def multi_caret():
            reset('abcd\nefgh')
            click_offset(1)
            page.keyboard.down('Alt')
            click_offset(6)
            page.keyboard.up('Alt')
            check(len(state()['carets']) == 2, 'Alt+click did not add a caret')
            page.keyboard.type('X')
            text('aXbcd\neXfgh')
            page.keyboard.press('Control+z')
            text('abcd\nefgh')
            check(len(state()['carets']) == 2, 'Multi-caret undo lost selections')
        test('real Alt+click multiple carets and atomic undo', multi_caret)
        def next_occurrence():
            reset('cat dog cat')
            command('select', '0:3')
            page.keyboard.press('Control+d')
            check(len(state()['carets']) == 2, 'Next occurrence was not added')
            page.keyboard.type('tiger')
            text('tiger dog tiger')
        test('Ctrl+D multi-selection editing', next_occurrence)
        def box():
            reset('abcd\nefgh\nijkl')
            a, b = point(1), point(13)
            page.keyboard.down('Alt')
            page.keyboard.down('Shift')
            page.mouse.move(a['x']+.1, a['y'])
            page.mouse.down()
            page.mouse.move(b['x']+.1, b['y'], steps=8)
            page.mouse.up()
            page.keyboard.up('Shift')
            page.keyboard.up('Alt')
            check(len(state()['carets']) == 3, 'Box selection did not create three ranges')
            page.keyboard.type('X')
            text('aXd\neXh\niXl')
            screenshot('rectangular-selection.png')
        test('real rectangular selection and replacement', box)
        def snippet():
            reset()
            command('snippet', '${1:name} = $1; ${2:value}$0')
            check(len(state()['carets']) == 2, 'Linked snippet occurrences were not selected')
            page.keyboard.type('counter')
            text('counter = counter; value')
            page.keyboard.press('Tab')
            check(state()['lengths'] == [5], 'Tab did not select the second placeholder')
            page.keyboard.type('42')
            text('counter = counter; 42')
            page.keyboard.press('Tab')
            check(not state()['snippetActive'], 'Final tab stop did not finish the snippet')
            screenshot('linked-snippet.png')
        test('linked snippet mirrors and keyboard tab stops', snippet)
        def indent():
            reset('a\nb\nc')
            command('select', '0:4')
            page.keyboard.press('Tab')
            text('    a\n    b\nc')
            page.keyboard.press('Shift+Tab')
            text('a\nb\nc')
        test('multi-line indentation through keyboard', indent)
        def line_commands():
            reset('a \t\r\n b \n')
            command('trim')
            text('a\r\n b\n')
            reset('first\nsecond', 'C#')
            command('select', '0:12')
            command('comment')
            text('// first\n// second')
            command('undo')
            text('first\nsecond')
            reset('abc')
            command('duplicate')
            text('abc\nabc')
        test('trim, comments and duplicate use original transactions', line_commands)
        def pair():
            reset()
            command('pairs', 'true')
            page.keyboard.type('(')
            text('()')
            check(state()['caret'] == 1, 'Pair did not place caret inside')
            page.keyboard.type(')')
            text('()')
            check(state()['caret'] == 2, 'Tracked closing delimiter not skipped')
        test('auto-pair insertion and tracked closing skip', pair)
        def folds():
            reset('class A\n{\n  void M()\n  {\n    return;\n  }\n}', 'C#')
            command('fold')
            check(state()['collapsedCount'] >= 2, 'Nested fold regions missing')
            check(state()['visualRows'] < state()['lines'], 'Folded lines still occupy visual rows')
            command('goto', '5:3')
            check(state()['line'] == 5, 'Navigation into folded content failed')
            check(state()['collapsedCount'] == 0, 'Caret reveal did not unfold ancestors')
        test('nested folding and reveal inside collapsed content', folds)
        def diagnostics():
            reset('// TODO: implement', 'C#')
            command('analysis')
            check(state()['diagnosticCount'] == 1, 'Diagnostic provider result missing')
            screenshot('diagnostics.png')
        test('diagnostic provider and rendered underline', diagnostics)
        def completion():
            reset('Con', 'C#')
            command('caret', '3')
            page.keyboard.press('Control+Space')
            wait('window.scribe.state().completionCount === 2')
            screenshot('completion.png')
            page.keyboard.press('Tab')
            text('Console')
        test('original completion popup and keyboard acceptance', completion)
        def clipboard():
            reset('')
            page.evaluate('() => navigator.clipboard.writeText("clipboard 🌍")')
            page.keyboard.press('Control+v')
            text('clipboard 🌍')
            page.keyboard.press('Control+a')
            page.keyboard.press('Control+c')
            page.wait_for_timeout(200)
            check(page.evaluate('() => navigator.clipboard.readText()') == 'clipboard 🌍', 'Copy did not reach browser clipboard')
        test('native clipboard copy and paste', clipboard)
        def split():
            reset('shared')
            if not state()['split']:
                click('SplitButton')
            command('focus-secondary')
            page.keyboard.press('Control+End')
            page.keyboard.type(' view')
            text('shared view')
            check(state()['sharedDocument'], 'Views have different document instances')
            page.keyboard.press('Control+z')
            text('shared')
        test('split views share original document and history', split)
        def template():
            reset('base')
            command('caret', '4')
            page.keyboard.type(' edit')
            command('select', '1:3')
            before = state()
            command('template')
            after = state()
            check(before['viewportId'] == after['viewportId'], 'Template recreated original viewport')
            check(after['text'] == before['text'] and after['starts'] == before['starts'] and after['lengths'] == before['lengths'], 'Template lost document/selection state')
            screenshot('minimal-template.png')
            command('undo')
            text('base')
            command('template')
        test('ControlTheme replacement preserves original viewport identity', template)
        def theme():
            reset(original, 'C#')
            command('theme')
            page.wait_for_timeout(300)
            text(original)
            screenshot('preview-light.png')
            command('theme')
        test('light and dark themes preserve text', theme)
        def wrap():
            reset('wrap this long line around the viewport. ' * 35)
            if state()['split']:
                command('split')
            command('wrap', 'true')
            page.wait_for_timeout(200)
            check(state()['visualRows'] > state()['lines'], 'Soft wrap did not create physical rows')
            page.keyboard.press('Control+End')
            page.keyboard.type('END')
            check(state()['text'].endswith('END'), 'Wrapped editing failed')
            page.keyboard.press('ArrowUp')
            check(state()['caret'] < state()['length'], 'Physical vertical navigation did not move up')
        test('shaped soft wrapping and physical vertical navigation', wrap)
        def idle():
            reset('idle')
            page.wait_for_timeout(400)
            before = state()['renderCount']
            page.wait_for_timeout(1300)
            delta = state()['renderCount'] - before
            metrics['idleRenderCountIn1_3Seconds'] = delta
            check(delta <= 12, f'Idle viewport is continuously repainting: {delta}')
        test('idle viewport does not have an invalidation loop', idle)
        def large():
            reset()
            if state()['split']:
                command('split')
            start = time.perf_counter()
            command('large', '100000')
            wait('window.scribe.state().visualLines > 0')
            current = state()
            check(current['lines'] == 100000, 'Large document line count mismatch')
            check(0 < current['visualLines'] < 200, 'Entire document was realized')
            check(current['cacheCount'] <= 128, 'Unbounded text layout cache')
            metrics['largeDocumentAssignmentMs'] = current['loadMs']
            metrics['largeDocumentReadySeconds'] = round(time.perf_counter()-start, 3)
            metrics['largeDocumentLength'] = current['length']
            metrics['largeDocumentRealizedLines'] = current['visualLines']
            command('goto', '99999:1')
            wait('window.scribe.state().firstVisibleLine > 99000')
            page.keyboard.type('X')
            check(state()['dirty'] and state()['lines'] == 100000, 'Large document cannot be edited')
            check(state()['cacheCount'] <= 128, 'Cache grew without bound after navigation')
            screenshot('large-document.png')
        test('100,000 lines: independent buffer, virtualization and native editing', large)
        def narrow():
            reset(original, 'C#')
            page.set_viewport_size({'width': 760, 'height': 900})
            page.wait_for_timeout(250)
            check(bounds('editor')['width'] > 450, 'Responsive viewport collapsed')
            command('focus')
            page.keyboard.press('Control+End')
            page.keyboard.type('// narrow')
            check(state()['text'].endswith('// narrow'), 'Narrow viewport input failed')
            screenshot('preview-narrow.png')
        test('narrow responsive layout and editing', narrow)
        def mobile():
            page.set_viewport_size({'width': 440, 'height': 860})
            page.wait_for_timeout(250)
            b = bounds('editor')
            check(b['width'] > 230 and b['height'] > 250, 'Mobile-sized viewport unusable')
            screenshot('preview-mobile.png')
        test('mobile-sized viewport layout', mobile)
        def errors_test():
            check(not errors, 'Uncaught browser errors: ' + '\n'.join(errors))
            check(not resource_failures, 'Failed resources: ' + '\n'.join(resource_failures))
        test('no uncaught exceptions or failed application resources', errors_test)
    except Exception:
        results.append({'name': 'startup/harness', 'passed': False, 'error': traceback.format_exc()})
        print(traceback.format_exc(), flush=True)
        try:
            screenshot('startup-failure.png')
        except Exception:
            pass
    finally:
        (OUTPUT / 'browser-console.txt').write_text('\n'.join(console), encoding='utf-8')
        report = {'commit': os.environ.get('GITHUB_SHA'), 'browser': browser.version, 'tests': len(results), 'passed': sum(r['passed'] for r in results), 'failures': sum(not r['passed'] for r in results), 'uncaughtErrors': errors, 'resourceFailures': resource_failures, 'metrics': metrics, 'results': results}
        (OUTPUT / 'browser.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
        print('SCRIBE_BROWSER:', json.dumps({k:v for k,v in report.items() if k != 'results'}, ensure_ascii=False), flush=True)
        context.close()
        browser.close()
server.shutdown()
raise SystemExit(0 if results and all(r['passed'] for r in results) else 1)
