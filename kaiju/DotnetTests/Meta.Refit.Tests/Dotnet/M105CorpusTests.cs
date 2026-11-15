// T-123: cross-language check of M-105. Tools/economy/m105_refit.py (an independent Python oracle written against parts@v1 / chassis@v1) judged a seeded corpus of
// builds; the shipped RefitService must reach the same verdict (valid, or the same first violation) on every non-borderline build. New file: M105RefitTests.cs is unchanged.
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Game.Core;
using Game.Meta.Refit;

namespace Game.Meta.Refit.Tests
{
    public class M105CorpusTests
    {
        [Test]
        public void PythonOracleCorpus_AgreesWithRefitService()
        {
            string path = Path.Combine(Kit.Root(), "evidence", "T-123", "m105_corpus.json");
            if (!File.Exists(path)) Assert.Ignore("corpus not present: run Tools/economy/m105_refit.py --corpus evidence/T-123/m105_corpus.json");
            var doc = Json.Parse(File.ReadAllText(path));
            var slots = new Dictionary<string, List<string>>();
            foreach (var m in doc.Get("slots").Members) { var l = new List<string>(); foreach (var s in m.Value.Items) l.Add(s.Text); slots[m.Key] = l; }
            var rig = Kit.NewRig();
            int checkedBuilds = 0, valid = 0, invalid = 0, borderline = 0;
            var wrong = new List<string>();
            foreach (var b in doc.Get("builds").Items)
            {
                if (b.Get("b").Bool) { borderline++; continue; }
                string cid = b.Get("c").Text, want = b.Get("v").Text;
                var a = new Dictionary<string, string>();
                var names = slots[cid]; var ps = b.Get("p").Items;
                Assert.AreEqual(names.Count, ps.Count);
                for (int i = 0; i < names.Count; i++) a[names[i]] = ps[i].Text;
                var rep = rig.Svc.Validate(cid, a);
                string got = rep.Ok ? "ok" : rep.FirstViolation.ToString();
                checkedBuilds++;
                if (want == "ok") valid++; else invalid++;
                if (got != want && wrong.Count < 10) wrong.Add(cid + " " + string.Join(",", ps.ConvertAll(p => p.Text)) + " python=" + want + " csharp=" + got);
            }
            TestContext.Out.WriteLine("M-105 corpus: " + checkedBuilds + " builds replayed (" + valid + " valid, " + invalid + " invalid), " + borderline + " borderline skipped");
            Assert.IsEmpty(wrong, "disagreements between the Python oracle and RefitService:\n" + string.Join("\n", wrong));
            Assert.Greater(valid, 100); Assert.Greater(invalid, 100);
        }
    }
}
