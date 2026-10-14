#!/usr/bin/env python3
"""Focused native-browser regressions for completion focus, asynchronous providers and transaction limits."""
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

root = Path(__file__).resolve().parents[1]
out = root / 'artifacts/validation'
out.mkdir(parents=True, exist_ok=True)
publish = Path(sys.argv[1]).resolve() if len(sys.argv)>1 else root / 'artifacts/browser'
roots = [p.parent for p in publish.rglob('index.html') if (p.parent / '_framework/dotnet.js').is_file()]
if not roots:
    raise SystemExit('Actual published WASM bundle required.')
class Handler(http.server.SimpleHTTPRequestHandler):
    extensions_map = {**http.server.SimpleHTTPRequestHandler.extensions_map, '.wasm':'application/wasm','.js':'text/javascript','.mjs':'text/javascript'}
    def end_headers(self):
        self.send_header('Cross-Origin-Opener-Policy','same-origin')
        self.send_header('Cross-Origin-Embedder-Policy','require-corp')
        self.send_header('Cache-Control','no-store')
        super().end_headers()
    def log_message(self,*args):
        pass
server = http.server.ThreadingHTTPServer(('127.0.0.1',0),functools.partial(Handler,directory=str(roots[0])))
threading.Thread(target=server.serve_forever,daemon=True).start()
results,errors,console = [],[],[]
with sync_playwright() as pw:
    browser = pw.chromium.launch(headless=True,args=['--no-sandbox','--disable-dev-shm-usage','--use-angle=swiftshader','--enable-unsafe-swiftshader'])
    context = browser.new_context(viewport={'width':1440,'height':960},permissions=['clipboard-read','clipboard-write'])
    page = context.new_page()
    page.on('pageerror',lambda e:errors.append(str(e)))
    page.on('console',lambda m:console.append(m.type+': '+m.text))
    def state():
        return page.evaluate('() => window.scribe.state()')
    def command(name,arg=''):
        if not isinstance(arg,str):arg=json.dumps(arg)
        raw=page.evaluate('async ([name,arg]) => await window.scribe.execute(name,arg)',[name,arg])
        page.wait_for_timeout(60)
        return json.loads(raw)
    def probe(name):
        return page.evaluate('async name => await window.scribe.probe(name)',name)
    def check(value,message):
        if not value:raise AssertionError(message)
    def expect_text(value):
        page.wait_for_function('value => window.scribe.state().text === value',arg=value,timeout=5000)
    def reset(text='Con'):
        command('document','0')
        command('reset',text)
        command('language','C#')
        command('caret',str(len(text.encode('utf-16-le'))//2))
        command('focus')
        page.wait_for_timeout(100)
    def open_completion():
        reset()
        page.keyboard.press('Control+Space')
        page.wait_for_function('() => window.scribe.state().completionCount === 2')
        page.wait_for_timeout(100)
        b=page.evaluate('() => window.scribe.bounds("completion")')
        check(b['width']>0 and b['height']>0,'Completion list has no actual layout')
        return b
    def test(name,action):
        start=time.perf_counter()
        try:
            action();results.append({'name':name,'passed':True,'milliseconds':round((time.perf_counter()-start)*1000,2)})
            print('PASS',name,flush=True)
        except Exception:
            failure={'name':name,'passed':False,'error':traceback.format_exc()}
            try:
                failure['state']=state();page.screenshot(path=str(out/f'deep-failure-{len(results)+1}.png'))
            except Exception:pass
            results.append(failure);print('FAIL',name,failure['error'],flush=True)
    try:
        page.goto(f'http://127.0.0.1:{server.server_port}/?test=1',wait_until='domcontentloaded',timeout=120000)
        page.wait_for_function('() => window.scribe?.ready || window.scribeError',timeout=120000)
        check(not page.evaluate('() => window.scribeError || null'),'Browser startup failed')
        page.wait_for_function('() => window.scribe.state().visualLines > 0')
        def mouse_completion():
            b=open_completion()
            page.mouse.dblclick(b['x']+40,b['y']+22,delay=70)
            expect_text('Console')
            check(state()['completionCount']==0,'Popup remains after accepting')
        test('double-click accepts completion without focus dismissal',mouse_completion)
        def mouse_then_keyboard():
            b=open_completion()
            page.mouse.click(b['x']+40,b['y']+22)
            check(state()['completionCount']==2,'Selecting completion dismissed popup')
            page.keyboard.press('ArrowDown')
            page.keyboard.press('Enter')
            expect_text('Console.WriteLine("Hello");')
        test('keyboard acceptance works after pointer focus enters completion',mouse_then_keyboard)
        def completion_escape():
            b=open_completion();page.mouse.click(b['x']+40,b['y']+22)
            page.keyboard.press('Escape')
            check(state()['completionCount']==0,'Escape did not close popup')
            page.keyboard.type('X');expect_text('ConX')
        test('completion Escape restores editor focus',completion_escape)
        def stale_completion_caret():
            reset();probe('start-completion');page.keyboard.press('ArrowLeft');probe('finish-completion')
            check(state()['completionCount']==0,'Caret-stale completion was published');expect_text('Con')
        test('delayed completion is discarded after caret movement',stale_completion_caret)
        def stale_completion_revision():
            reset();probe('start-completion');page.keyboard.type('x');probe('finish-completion')
            check(state()['completionCount']==0,'Revision-stale completion was published');expect_text('Conx')
        test('provider ignoring cancellation cannot publish stale completion',stale_completion_revision)
        def stale_diagnostics():
            reset('abc');probe('start-diagnostics');page.keyboard.type('x');probe('finish-diagnostics')
            expect_text('abcx');check(state()['diagnosticCount']==0,'Stale diagnostic was published')
        test('provider ignoring cancellation cannot publish stale diagnostics',stale_diagnostics)
        def cap():
            content='a'*10001;reset(content)
            command('replace-all',{'pattern':'a','replacement':'b'})
            expect_text(content)
            check('limit' in state()['searchStatus'].lower(),'Replacement cap was not surfaced')
            check(not state()['dirty'],'Failed replacement changed save state')
        test('replacement cap refuses partial document mutation',cap)
        def native_cut():
            reset('cut me');page.keyboard.press('Control+a');page.keyboard.press('Control+x');expect_text('')
            check(page.evaluate('() => navigator.clipboard.readText()')=='cut me','Cut did not write clipboard')
            page.keyboard.press('Control+z');expect_text('cut me')
        test('native cut and undo preserve copied text',native_cut)
        def readonly_paste():
            reset('locked');command('document-readonly','true')
            page.evaluate('() => navigator.clipboard.writeText("forbidden")');page.keyboard.press('Control+v');page.wait_for_timeout(120)
            expect_text('locked');command('document-readonly','false')
        test('document-level read-only blocks asynchronous paste',readonly_paste)
        def clean():
            check(not errors,'Uncaught browser exceptions: '+'\n'.join(errors))
        test('deep interaction suite has no uncaught exceptions',clean)
    except Exception:
        results.append({'name':'deep startup/harness','passed':False,'error':traceback.format_exc()})
        print(traceback.format_exc(),flush=True)
    finally:
        report={'commit':os.environ.get('GITHUB_SHA'),'browser':browser.version,'tests':len(results),'passed':sum(r['passed'] for r in results),'failures':sum(not r['passed'] for r in results),'uncaughtErrors':errors,'results':results}
        (out/'browser-deep.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
        (out/'browser-deep-console.txt').write_text('\n'.join(console),encoding='utf-8')
        print('SCRIBE_BROWSER_DEEP:',json.dumps({k:v for k,v in report.items() if k!='results'}),flush=True)
        context.close();browser.close()
server.shutdown()
raise SystemExit(0 if results and all(r['passed'] for r in results) else 1)
