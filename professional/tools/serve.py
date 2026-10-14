#!/usr/bin/env python3
"""Serve a published ProText WASM app using a local HTTP server."""
import argparse
import functools
import http.server
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('directory', nargs='?', default='artifacts/browser')
parser.add_argument('--port', type=int, default=8080)
args = parser.parse_args()
root = Path(args.directory).resolve()
candidates = [p.parent for p in root.rglob('index.html') if (p.parent / '_framework').is_dir()]
if not candidates:
    parser.error(f'No published app found in {root}; run dotnet publish first.')
class Handler(http.server.SimpleHTTPRequestHandler):
    extensions_map = {**http.server.SimpleHTTPRequestHandler.extensions_map, '.wasm': 'application/wasm', '.js': 'text/javascript', '.mjs': 'text/javascript'}
    def end_headers(self):
        self.send_header('Cross-Origin-Opener-Policy', 'same-origin')
        self.send_header('Cross-Origin-Embedder-Policy', 'require-corp')
        super().end_headers()
server = http.server.ThreadingHTTPServer(('127.0.0.1', args.port), functools.partial(Handler, directory=str(candidates[0])))
print(f'ProText: http://127.0.0.1:{server.server_port}/', flush=True)
try:
    server.serve_forever()
except KeyboardInterrupt:
    pass
finally:
    server.server_close()
