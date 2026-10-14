#!/usr/bin/env python3
"""Serve an actual published Scribe WASM bundle over local HTTP."""
import argparse
import functools
import http.server
from pathlib import Path
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('directory', nargs='?', default='artifacts/browser')
parser.add_argument('--port', type=int, default=8080)
args = parser.parse_args()
root = Path(args.directory).resolve()
roots = [p.parent for p in root.rglob('index.html') if (p.parent / '_framework/dotnet.js').is_file()]
if not roots:
    parser.error('No published WASM app found. Publish samples/Scribe.Browser first.')
class Handler(http.server.SimpleHTTPRequestHandler):
    extensions_map = {**http.server.SimpleHTTPRequestHandler.extensions_map, '.wasm': 'application/wasm', '.js': 'text/javascript', '.mjs': 'text/javascript'}
    def end_headers(self):
        self.send_header('Cross-Origin-Opener-Policy', 'same-origin')
        self.send_header('Cross-Origin-Embedder-Policy', 'require-corp')
        super().end_headers()
server = http.server.ThreadingHTTPServer(('127.0.0.1', args.port), functools.partial(Handler, directory=str(roots[0])))
print(f'Scribe: http://127.0.0.1:{server.server_port}/', flush=True)
try:
    server.serve_forever()
except KeyboardInterrupt:
    pass
finally:
    server.server_close()
