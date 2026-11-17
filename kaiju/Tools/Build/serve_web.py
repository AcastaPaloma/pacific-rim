"""Static server for the Unity web build: Content-Encoding for .gz/.br, wasm/js MIME types, and X.gz served for a
request of X (the gzipped StreamingAssets). Binds 127.0.0.1 only."""
import http.server, os, sys
root, port = sys.argv[1], int(sys.argv[2])
TYPES = {".wasm": "application/wasm", ".js": "application/javascript", ".data": "application/octet-stream",
         ".json": "application/json", ".html": "text/html", ".geojson": "application/json"}
class H(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k): super().__init__(*a, directory=root, **k)
    def send_head(self):
        path = self.translate_path(self.path); enc = None; logical = path
        if not os.path.exists(path) and os.path.exists(path + ".gz"): path += ".gz"
        if path.endswith(".gz"): enc = "gzip"; logical = path[:-3]
        elif path.endswith(".br"): enc = "br"; logical = path[:-3]
        if os.path.isdir(path): return super().send_head()
        try: f = open(path, "rb")
        except OSError: self.send_error(404); return None
        self.send_response(200)
        self.send_header("Content-Type", TYPES.get(os.path.splitext(logical)[1], "application/octet-stream" if enc else self.guess_type(logical)))
        if enc: self.send_header("Content-Encoding", enc)
        self.send_header("Content-Length", str(os.fstat(f.fileno()).st_size))
        self.send_header("Cache-Control", "no-cache")
        self.end_headers(); return f
http.server.ThreadingHTTPServer.allow_reuse_address = True
print("serving %s at http://localhost:%d/" % (root, port), flush=True)
http.server.ThreadingHTTPServer(("127.0.0.1", port), H).serve_forever()
