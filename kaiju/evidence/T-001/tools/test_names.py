"""Extract sorted test names from a dotnet TRX and a Unity NUnit XML and diff them (T-001 C2).
Usage: python test_names.py <dotnet.trx> <unity-editmode.xml> <out.txt>
Unity-only tests are those in the Game.Core.Unity.Tests assembly (namespace Game.Core.Unity.Tests)."""
import sys, xml.etree.ElementTree as ET

def trx_names(path):
    root = ET.parse(path).getroot()
    ns = {"t": root.tag.split("}")[0].strip("{")}
    cls = {u.get("id"): u.find("t:TestMethod", ns).get("className") for u in root.iter("{%s}UnitTest" % ns["t"])}
    out = {}
    for r in root.iter("{%s}UnitTestResult" % ns["t"]):
        out[cls[r.get("testId")] + "." + r.get("testName")] = r.get("outcome")
    return out

def nunit_names(path):
    out = {}
    for tc in ET.parse(path).getroot().iter("test-case"):
        out[tc.get("classname") + "." + tc.get("name")] = tc.get("result")
    return out

d, u = trx_names(sys.argv[1]), nunit_names(sys.argv[2])
unity_only = sorted(n for n in u if n.startswith("Game.Core.Unity.Tests."))
shared_u = {n: r for n, r in u.items() if n not in unity_only}
lines = ["# dotnet (TRX) vs Unity EditMode (NUnit XML) test names, sorted (T-001 C2)",
         "dotnet tests: %d (outcomes: %s)" % (len(d), sorted(set(d.values()))),
         "unity tests: %d = %d shared + %d Unity-only (results: %s)" % (len(u), len(shared_u), len(unity_only), sorted(set(u.values()))),
         "", "## Explicit Unity-only list (need the engine; assembly Game.Core.Unity.Tests)"] + unity_only
only_d = sorted(set(d) - set(shared_u)); only_u = sorted(set(shared_u) - set(d))
lines += ["", "## diff of shared names (empty = identical)", "only in dotnet: %d" % len(only_d)] + only_d + ["only in unity: %d" % len(only_u)] + only_u
lines += ["", "## shared names (%d)" % len(shared_u)] + sorted(shared_u)
open(sys.argv[3], "w").write("\n".join(lines) + "\n")
print("\n".join(lines[:8])); print("only_d", len(only_d), "only_u", len(only_u))
sys.exit(1 if only_d or only_u else 0)
