"""Web demo check in real Chrome over CDP (local only): load time, fps, WASD/F/R/Q/I input, screenshots, console log.
Usage: python web_check.py URL OUTDIR   (Chrome is started by web_check.sh with --remote-debugging-port=9333)."""
import base64, json, os, sys, time, urllib.request, websocket
url, out = sys.argv[1], sys.argv[2]; os.makedirs(out, exist_ok=True)
tabs = json.load(urllib.request.urlopen("http://127.0.0.1:9333/json"))
ws = websocket.create_connection([t for t in tabs if t["type"] == "page"][0]["webSocketDebuggerUrl"], timeout=120)
mid = [0]
def cdp(method, **p):
    mid[0] += 1; ws.send(json.dumps({"id": mid[0], "method": method, "params": p}))
    while True:
        m = json.loads(ws.recv())
        if m.get("id") == mid[0]: return m.get("result", m.get("error"))
def ev(js): r = cdp("Runtime.evaluate", expression=js, returnByValue=True, awaitPromise=True); return r.get("result", {}).get("value")
def shot(name): open(os.path.join(out, name), "wb").write(base64.b64decode(cdp("Page.captureScreenshot", format="png")["data"]))
KEYS = {"w": 87, "a": 65, "s": 83, "d": 68, "f": 70, "r": 82, "q": 81, "i": 73, " ": 32}
def key(k, typ):
    code = "Space" if k == " " else "Key" + k.upper()
    cdp("Input.dispatchKeyEvent", type=typ, key=k, code=code, windowsVirtualKeyCode=KEYS[k], nativeVirtualKeyCode=KEYS[k])
def tap(k, hold=0.15): key(k, "keyDown"); time.sleep(hold); key(k, "keyUp")
def logs(pat): return ev("window.__kaijuLog?window.__kaijuLog.filter(e=>/%s/.test(e[2])).map(e=>e[0]+' '+e[1]+' '+e[2].slice(0,300)):[]" % pat) or []
def fps(sec):
    return ev("new Promise(r=>{let n=0,t0=performance.now();function f(){n++;if(performance.now()-t0<%d)requestAnimationFrame(f);else r(n*1000/(performance.now()-t0));}requestAnimationFrame(f);})" % (sec * 1000))
def hud(): return ev("(()=>{const l=window.__kaijuLog.filter(e=>/\\[Slice\\]/.test(e[2]));return l.slice(-6).map(e=>e[2].slice(0,200));})()")
cdp("Page.enable"); cdp("Runtime.enable")
t0 = time.time(); cdp("Page.navigate", url=url)
res = {"url": url}
ready = None
while time.time() - t0 < 240:
    time.sleep(1)
    r = logs("\\[Slice\\] ready in")
    if r: ready = time.time() - t0; res["ready_log"] = r[0]; break
res["load_to_playable_s"] = ready
res["preload_log"] = logs("WebPreload")
res["errors"] = logs("rror|xception|Out of memory|abort")[:30]
if ready is None: shot("timeout.png"); json.dump(res, open(os.path.join(out, "result.json"), "w"), indent=1); print(json.dumps(res, indent=1)); sys.exit(1)
cdp("Runtime.evaluate", expression="document.querySelector('#unity-canvas').focus()")
time.sleep(2); shot("01_ready.png"); res["fps_idle"] = fps(5)
key("w", "keyDown"); time.sleep(1); res["fps_walking"] = fps(5); time.sleep(2); shot("02_walking.png")
key("d", "keyDown"); time.sleep(1.0); key("d", "keyUp"); time.sleep(2); key("w", "keyUp"); tap(" "); shot("03_after_walk.png")
for _ in range(3): tap("f"); time.sleep(0.8)
shot("04_cannon.png"); tap("r"); time.sleep(1.0); shot("05_melee.png")
tap("i"); time.sleep(4); shot("06_impact.png")
tap("q"); time.sleep(1.5); shot("07_quake.png"); res["fps_quake"] = fps(4); time.sleep(4); shot("08_after_quake.png")
res["slice_log_tail"] = logs("\\[Slice\\]")[-25:]
res["errors"] = logs("rror|xception|Out of memory|abort")[:30]
res["heap_mb"] = ev("(()=>{try{return Math.round(performance.memory.usedJSHeapSize/1048576)}catch(e){return null}})()")
open(os.path.join(out, "console.log"), "w").write("".join(e if e.endswith("\n") else e + "\n" for e in logs(".")))
json.dump(res, open(os.path.join(out, "result.json"), "w"), indent=1); print(json.dumps(res, indent=1)[:4000])
