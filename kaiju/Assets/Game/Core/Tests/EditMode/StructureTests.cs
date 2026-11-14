using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>Static checks on Core itself: layering (C1), no mutable statics (C16), no counts (C22), no clocks (C27), no buildings in save (C33).</summary>
    public class StructureTests
    {
        private static readonly Assembly[] CoreAssemblies = { typeof(IEvent).Assembly, typeof(EventBus).Assembly };

        private static IEnumerable<string> CoreSources()
        {
            foreach (var sub in new[] { "Contracts", "Runtime" })
                foreach (var f in Directory.GetFiles(ProjectPaths.Of("Assets", "Game", "Core", sub), "*.cs", SearchOption.AllDirectories))
                    yield return f;
        }

        private static string StripComments(string code) =>
            Regex.Replace(Regex.Replace(code, @"//[^\n]*", ""), @"/\*.*?\*/", "", RegexOptions.Singleline);

        private static int CountMatches(string pattern, string file = null)
        {
            int n = 0;
            foreach (var f in CoreSources())
            {
                if (file != null && Path.GetFileName(f) != file) continue;
                n += Regex.Matches(StripComments(File.ReadAllText(f)), pattern).Count;
            }
            return n;
        }

        [Test]
        public void Layering_CoreAsmdefsAreEngineFree()
        {
            var core = ProjectPaths.Of("Assets", "Game", "Core");
            var contracts = Json.Parse(File.ReadAllText(Path.Combine(core, "Contracts", "Game.Core.Contracts.asmdef")));
            var runtime = Json.Parse(File.ReadAllText(Path.Combine(core, "Runtime", "Game.Core.asmdef")));
            Assert.That(contracts.Get("noEngineReferences").Bool, Is.True);
            Assert.That(runtime.Get("noEngineReferences").Bool, Is.True);
            Assert.That(contracts.Get("references").Items, Is.Empty);
            Assert.That(runtime.Get("references").Items.Count, Is.EqualTo(1));
            Assert.That(runtime.Get("references").Items[0].Text, Is.EqualTo("Game.Core.Contracts"));
            Assert.That(contracts.Get("precompiledReferences").Items, Is.Empty);
            Assert.That(runtime.Get("precompiledReferences").Items, Is.Empty);
            int engineAdapters = 0;
            foreach (var f in Directory.GetFiles(core, "*.asmdef", SearchOption.AllDirectories))
            {
                var a = Json.Parse(File.ReadAllText(f));
                bool isTest = a.Get("defineConstraints") != null && a.Get("defineConstraints").Items.Exists(i => i.Text == "UNITY_INCLUDE_TESTS");
                bool engineFree = a.Get("noEngineReferences") != null && a.Get("noEngineReferences").Bool;
                if (!engineFree && !isTest) engineAdapters++;
            }
            Assert.That(engineAdapters, Is.EqualTo(1), "exactly one non-test Core assembly (the adapter) may reference UnityEngine");
            foreach (var asm in CoreAssemblies)
                foreach (var r in asm.GetReferencedAssemblies())
                    Assert.That(r.Name.StartsWith("Unity", StringComparison.Ordinal), Is.False, asm.GetName().Name + " references " + r.Name);
            Evidence.Line("grep-counts.txt", "UnityEngine mentions in Core/Contracts+Runtime code: " + CountMatches(@"UnityEngine"));
            Assert.That(CountMatches(@"UnityEngine|using\s+Unity"), Is.EqualTo(0));
        }

        [Test]
        public void Layering_NoNetOrGameplayTypesInCore()
        {
            var forbidden = new Regex("Mech|Kaiju|Pilot|Net|Network|Replica|Foot|Impact|Weapon|Damage|Weather");
            var found = new List<string>();
            foreach (var asm in CoreAssemblies)
                foreach (var t in asm.GetTypes())
                    if (!t.Name.StartsWith("<", StringComparison.Ordinal) && forbidden.IsMatch(t.Name)) found.Add(t.FullName);
            Assert.That(found, Is.Empty);
        }

        [Test]
        public void NoMutableStaticFieldsInCore()
        {
            var mutable = new List<string>();
            int checkedFields = 0;
            foreach (var asm in CoreAssemblies)
                foreach (var t in asm.GetTypes())
                    foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        checkedFields++;
                        bool compilerLambdaCache = t.Name.StartsWith("<", StringComparison.Ordinal) || f.Name.StartsWith("<>9", StringComparison.Ordinal);
                        if (!f.IsLiteral && !f.IsInitOnly && !compilerLambdaCache) mutable.Add(t.FullName + "." + f.Name);
                    }
            Evidence.Line("grep-counts.txt", "mutable static fields in Game.Core + Game.Core.Contracts (reflection, " + checkedFields + " static fields checked): " + mutable.Count + " " + string.Join(",", mutable));
            Assert.That(mutable, Is.Empty);
        }

        [Test]
        public void NoHardCodedCountsInCore()
        {
            string pattern = @"(?i)\bplayer_?[0-9]+\b|\bmax_?(players?|mechs?|kaiju|pilots?|slots?)\b|\b(players?|mechs?|kaiju|pilots?)_?(count|num)\s*=\s*[0-9]|\bnum_?(players?|mechs?|kaiju|pilots?)\b|new\s+\w+\s*\[\s*2\s*\]|\[\s*1\s*\]\s*;";
            int hits = CountMatches(pattern);
            Evidence.Line("grep-counts.txt", "hard-coded player/mech/kaiju/pilot counts in Core: " + hits);
            Assert.That(hits, Is.EqualTo(0));
            foreach (var m in typeof(PlayerSlot).GetMethods())
                Assert.That(m.Name.IndexOf("Max", StringComparison.OrdinalIgnoreCase), Is.LessThan(0));
        }

        [Test]
        public void NoWallClockOrUnseededRandomInCore()
        {
            string pattern = @"DateTime\.(Utc)?Now|Stopwatch|Environment\.TickCount|\bRandom\b|Guid\.NewGuid|GetInstanceID|UnityEngine\.Time|Time\.(time|deltaTime|fixedDeltaTime)";
            int hits = CountMatches(pattern);
            Evidence.Line("grep-counts.txt", "wall-clock / Random / Guid.NewGuid / GetInstanceID / UnityEngine.Time in Core: " + hits);
            Assert.That(hits, Is.EqualTo(0));
            int idHashCalls = CountMatches(@"\.GetHashCode\(\)", "Ids.cs");
            Evidence.Line("grep-counts.txt", "GetHashCode() calls inside Ids.cs (IDs must use IdHash.Mix only): " + idHashCalls);
            Assert.That(idHashCalls, Is.EqualTo(0));
        }

        [Test]
        public void SaveCodeKnowsNothingAboutBuildings()
        {
            int hits = CountMatches(@"Building", "Save.cs");
            Evidence.Line("grep-counts.txt", "'Building' in Contracts/Save.cs + Runtime/Save.cs: " + hits);
            Assert.That(hits, Is.EqualTo(0));
        }
    }
}
