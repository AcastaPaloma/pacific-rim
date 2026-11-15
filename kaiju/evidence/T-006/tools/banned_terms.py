"""T-006 C7: banned-terms check on file names. Scans every file and directory name under evidence/T-006 and checkpoints/M0
for franchise terms (case-insensitive, separators ignored). Writes evidence/T-006/checks/banned_terms.json.
Run from repo root: python3 evidence/T-006/tools/banned_terms.py   (exit 1 when a hit is found)
Scope is names only; text inside images is checked separately (the frames contain no text by construction)."""
import json, os, re, sys

ROOTS = ["evidence/T-006", "checkpoints/M0"]
BANNED = ["pacific rim", "pacificrim", "jaeger", "gipsy", "gipsy danger", "striker eureka", "crimson typhoon", "cherno alpha", "coyote tango",
          "kaiju", "otachi", "leatherback", "knifehead", "slattern", "raiju", "scunner", "godzilla", "gojira", "evangelion", "eva-01", "gundam",
          "mechagodzilla", "ultraman", "voltron", "transformers", "optimus", "del toro", "legendary pictures"]
# "kaiju" is the project's own repo and tool name (kaiju_metrics, env kaiju): the word alone is reported separately as project_name, not as a hit
PROJECT_NAME_ALLOWED = {"kaiju"}

def norm(s):
    return re.sub(r"[^a-z0-9]+", "", s.lower())

terms = [(t, norm(t)) for t in BANNED]
hits, allowed, scanned = [], [], 0
for root in ROOTS:
    if not os.path.exists(root):
        continue
    for dp, dn, fn in os.walk(root):
        for name in dn + fn:
            scanned += 1
            path = os.path.join(dp, name)
            n = norm(path)
            for t, nt in terms:
                if nt and nt in n:
                    (allowed if t in PROJECT_NAME_ALLOWED else hits).append({"path": path, "term": t})
out = {"schema": "banned_terms@v1", "criterion": "C7", "roots": ROOTS, "banned_terms": BANNED, "names_scanned": scanned,
       "hits": hits, "hit_count": len(hits), "project_name_matches": allowed, "result": "PASS" if not hits else "FAIL",
       "scope": "file and directory names only"}
os.makedirs("evidence/T-006/checks", exist_ok=True)
json.dump(out, open("evidence/T-006/checks/banned_terms.json", "w"), indent=1)
print(out["result"], "scanned", scanned, "hits", len(hits), "project_name_matches", len(allowed))
for h in hits[:20]:
    print(" ", h["term"], h["path"])
sys.exit(1 if hits else 0)
