#!/usr/bin/env python3
"""Exercise the published Avalonia WASM sample with native canvas input and a state-observation bridge.
Requires: pip install playwright pillow; python -m playwright install chromium.
"""
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
OUTPUT = ROOT / 'artifacts' / 'validation'
OUTPUT.mkdir(parents=True, exist_ok=True)
PUBLISH = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / 'artifacts' / 'browser'
roots = [path.parent for path in PUBLISH.rglob('index.html') if (path.parent / '_framework').is_dir()]
if not roots:
    raise SystemExit(f'No published index.html with _framework found under {PUBLISH}')

class Handler(http.server.SimpleHTTPRequestHandler):
    extensions_map = {**http.server.SimpleHTTPRequestHandler.extensions_map, '.wasm': 'application/wasm', '.js': 'text/javascript', '.mjs': 'text/javascript'}
    def end_headers(self):
        self.send_header('Cross-Origin-Opener-Policy', 'same-origin')
        self.send_header('Cross-Origin-Embedder-Policy', 'require-corp')
        self.send_header('Cache-Control', 'no-cache')
        super().end_headers()
    def log_message(self, *_args):
        pass

server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(Handler, directory=str(roots[0])))
threading.Thread(target=server.serve_forever, daemon=True).start()
results: list[dict] = []
errors: list[str] = []
console: list[str] = []
metrics: dict = {}

with sync_playwright() as playwright:
    browser = playwright.chromium.launch(headless=True, args=['--no-sandbox', '--disable-dev-shm-usage', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
    context = browser.new_context(viewport={'width': 1440, 'height': 960}, device_scale_factor=1)
    page = context.new_page()
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.on('console', lambda message: console.append(f'{message.type}: {message.text}'))
    page.set_default_timeout(30000)
    def state():
        return page.evaluate('() => window.protext.state()')
    def execute(command: str, argument: str = ''):
        result = page.evaluate('async ([command, argument]) => await window.protext.execute(command, argument)', [command, argument])
        page.wait_for_timeout(80)
        return json.loads(result)
    def wait(expression: str, timeout: int = 30000):
        page.wait_for_function('() => ' + expression, timeout=timeout)
    def expect(condition: bool, message: str):
        if not condition:
            raise AssertionError(message)
    def expect_text(text: str):
        actual = state()['Text']
        expect(actual == text, f'Expected text {text!r}, got {actual!r}')
    def reset(text: str, language: str = 'Plain Text'):
        execute('document', '0')
        execute('reset', text)
        execute('language', language)
        execute('pairs', 'false')
        execute('wrap', 'false')
        execute('focus')
    def click(name: str):
        bounds = page.evaluate('(name) => window.protext.bounds(name)', name)
        expect(bounds['width'] > 0 and bounds['height'] > 0, f'{name} has empty bounds: {bounds}')
        page.mouse.click(bounds['x'] + bounds['width'] / 2, bounds['y'] + bounds['height'] / 2)
        page.wait_for_timeout(180)
    def test(name, function):
        start = time.perf_counter()
        try:
            function()
            results.append({'name': name, 'passed': True, 'milliseconds': round((time.perf_counter()-start)*1000, 2)})
            print('PASS', name, flush=True)
        except Exception:
            error = traceback.format_exc()
            result = {'name': name, 'passed': False, 'error': error}
            try:
                result['state'] = state()
                page.screenshot(path=str(OUTPUT / f'failure-{len(results)+1:02}.png'))
            except Exception:
                pass
            results.append(result)
            print('FAIL', name, error, flush=True)

    try:
        start = time.perf_counter()
        page.goto(f'http://127.0.0.1:{server.server_port}/?test=1', wait_until='domcontentloaded', timeout=120000)
        wait('window.protext?.ready || window.protextError', 120000)
        error = page.evaluate('() => window.protextError || null')
        if error:
            raise RuntimeError(error)
        wait('window.protext.state().VisualLines > 0', 60000)
        metrics['cold_start_seconds'] = round(time.perf_counter()-start, 3)
        original = state()['Text']
        def startup():
            expect(page.locator('canvas').count() >= 1, 'No native Avalonia canvas')
            expect('Hello, Avalonia!' in state()['Text'], 'Welcome document was not loaded')
            expect(state()['VisualLines'] > 10, 'Editor has no rendered document lines')
            page.screenshot(path=str(OUTPUT / 'preview-dark.png'))
            image = Image.open(OUTPUT / 'preview-dark.png').convert('RGB')
            expect(len(image.getcolors(image.width*image.height)) > 100, 'Screenshot appears blank or unrendered')
        test('native Avalonia canvas and first paint', startup)
        def typing():
            reset('')
            bounds = page.evaluate('() => window.protext.bounds("editor")')
            page.mouse.click(bounds['x'] + 130, bounds['y'] + 14)
            page.keyboard.type('Hello editor', delay=15)
            expect_text('Hello editor')
            expect(state()['IsDirty'], 'Typing did not mark the document dirty')
        test('pointer focus and native keyboard typing', typing)
        def keyboard_undo():
            reset('')
            page.keyboard.type('hello', delay=15)
            page.keyboard.press('Control+z')
            expect_text('')
            page.keyboard.press('Control+y')
            expect_text('hello')
        test('native keyboard undo and redo', keyboard_undo)
        def select_replace():
            reset('old text')
            page.keyboard.press('Control+a')
            page.keyboard.type('replacement')
            expect_text('replacement')
        test('native select all and replacement', select_replace)
        def multiline():
            reset('')
            page.keyboard.type('first')
            page.keyboard.press('Enter')
            page.keyboard.type('second')
            expect_text('first\nsecond')
            expect(state()['Line'] == 2, 'Caret line not updated')
        test('native newline and caret status', multiline)
        def unicode():
            reset('')
            text = 'Zażółć 🌍 日本語'
            # CDP's insertText alone skips the composition lifecycle used by Avalonia.Browser.
            # Drive Chromium's actual IME composition and commit, not a managed document setter.
            cdp = context.new_cdp_session(page)
            try:
                utf16_length = len(text.encode('utf-16-le')) // 2
                cdp.send('Input.imeSetComposition', {'text': text, 'selectionStart': utf16_length, 'selectionEnd': utf16_length})
                cdp.send('Input.insertText', {'text': text})
            finally:
                cdp.detach()
            page.wait_for_timeout(100)
            expect_text(text)
        test('Chromium IME composition commit with Unicode', unicode)
        def unicode_caret():
            reset('a🌍b')
            execute('caret', '3')
            page.keyboard.press('ArrowLeft')
            expect(state()['Caret'] == 1, 'Caret split a surrogate pair')
        test('Unicode scalar caret navigation', unicode_caret)
        def readonly():
            reset('locked')
            execute('readonly', 'true')
            execute('focus')
            page.keyboard.press('Control+a')
            page.keyboard.type('X')
            page.keyboard.press('Backspace')
            expect_text('locked')
            execute('readonly', 'false')
        test('read-only blocks native mutation', readonly)
        def session_readonly():
            reset('locked')
            execute('session-readonly', 'true')
            execute('duplicate')
            execute('replace-all', json.dumps({'Pattern': 'locked', 'Replacement': 'changed'}))
            expect_text('locked')
            execute('session-readonly', 'false')
        test('document read-only blocks wrapper commands', session_readonly)
        def find_keyboard():
            reset('alpha beta alpha')
            page.keyboard.press('Control+f')
            wait('window.protext.state().SearchOpen')
            page.wait_for_timeout(200)
            page.keyboard.type('alpha')
            wait('window.protext.state().SearchMatches === 2')
            page.keyboard.press('Escape')
            wait('!window.protext.state().SearchOpen')
        test('keyboard find bar binding and Escape', find_keyboard)
        def find_pointer():
            reset('one two one')
            click('FindButton')
            click('find-query')
            page.keyboard.press('Control+a')
            page.keyboard.type('one')
            wait('window.protext.state().SearchMatches === 2')
        test('pointer-opened find field', find_pointer)
        def search_options():
            reset('cat Cat scatter cat')
            execute('find', json.dumps({'Pattern': 'cat', 'Case': True, 'Word': True}))
            expect(state()['SearchMatches'] == 2, 'Case / whole-word filtering failed')
        test('case-sensitive whole-word search', search_options)
        def regex_replace():
            reset('item12 item34')
            execute('replace-all', json.dumps({'Pattern': r'item(\d+)', 'Replacement': 'value-$1', 'Regex': True}))
            expect_text('value-12 value-34')
            execute('undo')
            expect_text('item12 item34')
        test('regex capture replacement and grouped undo', regex_replace)
        def scoped_replace():
            reset('cat cat cat')
            execute('select', '4:3')
            execute('replace-all', json.dumps({'Pattern': 'cat', 'Replacement': 'dog', 'Selection': True}))
            expect_text('cat dog cat')
        test('replace only in anchored selection', scoped_replace)
        def invalid_regex():
            reset('abc')
            execute('find', json.dumps({'Pattern': '[', 'Regex': True}))
            expect(state()['SearchMatches'] == 0, 'Invalid regex should not yield matches')
            expect(state()['SearchMessage'] not in ('No results', ''), 'No invalid-regex feedback')
        test('invalid regex is surfaced without crashing', invalid_regex)
        def zero_width():
            reset('aaa')
            execute('find', json.dumps({'Pattern': '(?=a)', 'Regex': True}))
            positions = []
            for _ in range(3):
                execute('next-match')
                positions.append(state()['SelectionStart'])
            expect(len(set(positions)) == 3, f'Zero-width navigation is stuck: {positions}')
        test('zero-width match navigation progresses', zero_width)
        def go_to():
            reset('one\ntwo\nthree')
            execute('goto', '3:3')
            expect(state()['Line'] == 3 and state()['Column'] == 3, 'Go-to position mismatch')
            execute('goto', 'not a line')
            expect(state()['Line'] == 3, 'Invalid go-to changed the caret')
        test('go-to line, column, and invalid-input handling', go_to)
        def comments():
            reset('first\nsecond', 'C#')
            execute('select', '0:12')
            execute('comment')
            expect_text('// first\n// second')
            execute('undo')
            expect_text('first\nsecond')
        test('line comments and undo', comments)
        def indent():
            reset('a\nb')
            execute('select', '0:3')
            execute('indent')
            expect_text('    a\n    b')
            execute('select', '0:11')
            execute('unindent')
            expect_text('a\nb')
        test('multi-line indentation and unindentation', indent)
        def duplicate():
            reset('abc')
            execute('caret', '1')
            execute('duplicate')
            expect_text('abc\nabc')
            expect(state()['Caret'] <= state()['Length'], 'Caret escaped document bounds')
        test('duplicate final line without trailing newline', duplicate)
        def trim():
            reset('a \t\r\n b \n')
            execute('trim')
            expect_text('a\r\n b\n')
        test('trim trailing whitespace preserves line endings', trim)
        def case_transform():
            reset('Abc')
            execute('select', '0:3')
            execute('upper')
            expect_text('ABC')
            execute('lower')
            expect_text('abc')
        test('selection case conversion', case_transform)
        def endings():
            reset('a\r\nb')
            execute('line-ending', 'Lf')
            expect_text('a\nb')
            execute('undo')
            expect_text('a\r\nb')
        test('line-ending conversion is undoable', endings)
        def bookmark():
            reset('a\nb\nc')
            execute('goto', '2')
            execute('bookmark')
            execute('goto', '1')
            page.keyboard.type('x')
            execute('next-bookmark')
            expect(state()['Line'] == 2 and state()['Bookmarks'] == 1, 'Bookmark navigation failed')
        test('anchored bookmarks and navigation', bookmark)
        def pairs():
            reset('')
            execute('pairs', 'true')
            page.keyboard.type('(')
            expect_text('()')
            expect(state()['Caret'] == 1, 'Caret is not inside pair')
            page.keyboard.type(')')
            expect_text('()')
            expect(state()['Caret'] == 2, 'Confirmed closing pair was not skipped')
        test('auto-pair insertion and closing skip', pairs)
        def folding():
            reset('class A\n{\n  void M()\n  {\n    return;\n  }\n}', 'C#')
            execute('analysis')
            expect(state()['Foldings'] >= 2, 'Nested fold regions missing')
            execute('fold')
            expect(state()['Folded'] >= 2, 'Fold-all failed')
            execute('fold', 'false')
            expect(state()['Folded'] == 0, 'Unfold-all failed')
        test('nested folding and unfolding', folding)
        def diagnostic():
            reset('// TODO: finish', 'C#')
            execute('analysis')
            expect(state()['Diagnostics'] == 1, 'Diagnostic provider did not publish')
            page.screenshot(path=str(OUTPUT / 'diagnostics.png'))
        test('diagnostic provider and rendered marker', diagnostic)
        def completion():
            reset('Con', 'C#')
            execute('caret', '3')
            page.keyboard.press('Control+Space')
            wait('window.protext.state().Completions >= 2')
            page.screenshot(path=str(OUTPUT / 'completion.png'))
            page.keyboard.press('Tab')
            expect(state()['Text'].startswith('Console'), 'Completion insertion did not replace prefix: '+state()['Text'])
        test('keyboard completion popup and insertion', completion)
        def split():
            reset('shared')
            if not state()['IsSplit']:
                click('SplitButton')
            execute('focus-secondary')
            page.keyboard.press('Control+End')
            page.keyboard.type(' view')
            expect_text('shared view')
            expect(state()['SecondaryText'] == state()['Text'], 'Shared views diverged')
            page.keyboard.press('Control+z')
            expect_text('shared')
        test('split views share native edits and undo', split)
        def template():
            reset('base')
            execute('caret', '4')
            page.keyboard.type(' edit')
            execute('select', '1:2')
            before = state()
            execute('template')
            wait('window.protext.state().VisualLines > 0')
            after = state()
            expect(after['Text'] == before['Text'], 'Template replacement changed text')
            expect(after['SelectionStart'] == before['SelectionStart'] and after['SelectionLength'] == before['SelectionLength'], 'Template replacement lost selection')
            expect(after['CanUndo'], 'Template replacement lost undo history')
            page.screenshot(path=str(OUTPUT / 'minimal-template.png'))
            execute('undo')
            expect_text('base')
            execute('template')
        test('ControlTheme replacement preserves text, selection, undo', template)
        def theme():
            reset(original, 'C#')
            before = state()['Text']
            execute('theme')
            page.wait_for_timeout(300)
            expect(state()['Text'] == before, 'Theme switch changed document')
            page.screenshot(path=str(OUTPUT / 'preview-light.png'))
            execute('theme')
        test('light and dark theme switching', theme)
        def wrap():
            reset(('A deliberately long line with wrap opportunities. ' * 40))
            execute('wrap', 'true')
            expect(state()['WordWrap'], 'Wrapping option not applied')
            page.wait_for_timeout(250)
            page.keyboard.press('Control+End')
            page.keyboard.type('END')
            expect(state()['Text'].endswith('END'), 'Wrapped editor failed to edit')
        test('word-wrapped document remains editable', wrap)
        def diff():
            execute('diff')
            expect(state()['DiffVisible'] and state()['DiffHunks'] >= 2, 'Side-by-side changes missing')
            page.screenshot(path=str(OUTPUT / 'diff.png'))
        test('side-by-side diff and change groups', diff)
        def large():
            reset('')
            if state()['IsSplit']:
                execute('split')
            start = time.perf_counter()
            execute('large', '100000')
            wait('window.protext.state().VisualLines > 0')
            current = state()
            expect(current['Lines'] == 100000, f'Wrong large document count: {current["Lines"]}')
            expect(current['LargeFile'], 'Large-file analysis limit did not activate')
            expect(0 < current['VisualLines'] < 500, f'Visible-line virtualization failed: {current["VisualLines"]}')
            metrics['large_document_assignment_ms'] = current['LastLoadMs']
            metrics['large_document_ready_seconds'] = round(time.perf_counter()-start, 3)
            metrics['large_document_utf16_length'] = current['Length']
            metrics['large_document_visible_lines'] = current['VisualLines']
            execute('goto', '99999:1')
            wait('window.protext.state().VisibleFirstLine > 99000')
            expect(state()['Line'] == 99999, 'Large-file go-to failed')
            page.keyboard.type('X')
            expect(state()['IsDirty'], 'Large-file typing did not update document')
            page.screenshot(path=str(OUTPUT / 'large-document.png'))
        test('100,000 lines, bounded visual lines, navigation and typing', large)
        def narrow():
            reset(original, 'C#')
            page.set_viewport_size({'width': 760, 'height': 900})
            page.wait_for_timeout(300)
            bounds = page.evaluate('() => window.protext.bounds("editor")')
            expect(bounds['width'] > 400, f'Narrow editor has unusable width: {bounds}')
            execute('focus')
            page.keyboard.press('Control+End')
            page.keyboard.type('// narrow')
            expect(state()['Text'].endswith('// narrow'), 'Narrow layout is not editable')
            page.screenshot(path=str(OUTPUT / 'preview-narrow.png'))
        test('narrow responsive layout and editing', narrow)
        def mobile():
            page.set_viewport_size({'width': 440, 'height': 860})
            page.wait_for_timeout(300)
            bounds = page.evaluate('() => window.protext.bounds("editor")')
            expect(bounds['width'] > 200 and bounds['height'] > 200, f'Mobile-sized editor collapsed: {bounds}')
            page.screenshot(path=str(OUTPUT / 'preview-mobile.png'))
        test('mobile-sized viewport remains usable', mobile)
        def clean_errors():
            expect(not errors, 'Uncaught browser errors: ' + '\n'.join(errors))
        test('no uncaught browser exceptions', clean_errors)
    except Exception:
        results.append({'name': 'browser startup or harness', 'passed': False, 'error': traceback.format_exc()})
        print(traceback.format_exc(), flush=True)
        try:
            page.screenshot(path=str(OUTPUT / 'startup-failure.png'))
        except Exception:
            pass
    finally:
        (OUTPUT / 'browser-console.txt').write_text('\n'.join(console), encoding='utf-8')
        report = {'commit': os.environ.get('GITHUB_SHA'), 'browser': browser.version, 'tests': len(results), 'passed': sum(r['passed'] for r in results), 'failures': sum(not r['passed'] for r in results), 'uncaughtErrors': errors, 'metrics': metrics, 'results': results}
        (OUTPUT / 'browser-results.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
        print('PROTEXT_BROWSER:', json.dumps({k:v for k,v in report.items() if k not in ('results',)}, ensure_ascii=False), flush=True)
        context.close()
        browser.close()
server.shutdown()
raise SystemExit(0 if results and all(r['passed'] for r in results) else 1)
