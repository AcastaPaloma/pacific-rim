using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public class FlagTests
    {
        private CollectingErrorSink _errors;
        [SetUp] public void SetUp() { _errors = new CollectingErrorSink(); }

        // C23
        [Test]
        public void Flags_Unknown_IsOff()
        {
            var flags = FeatureFlagLoader.Parse("{\"schema\":1,\"flags\":{\"a.on\":true,\"b.off\":false}}", "inline", _errors);
            Assert.That(flags.IsOn("a.on"), Is.True);
            Assert.That(flags.IsOn("b.off"), Is.False);
            Assert.That(flags.IsOn("never.listed"), Is.False);
            Assert.That(flags.IsOn("A.ON"), Is.False, "names are case-sensitive; a typo reads off");
            Assert.That(flags.IsOn(null), Is.False);
            Assert.That(_errors.Count, Is.EqualTo(0));
        }

        [Test]
        public void Flags_MissingFile_AllOffOneWarning()
        {
            var flags = FeatureFlagLoader.LoadFolder(Path.Combine(ProjectPaths.NewTempDir(), "FeatureFlags"), _errors);
            Assert.That(flags.Count, Is.EqualTo(0));
            Assert.That(flags.IsOn("core.verboseBoot"), Is.False);
            Assert.That(_errors.Count, Is.EqualTo(1));
            Assert.That(_errors.Errors[0].Kind, Is.EqualTo(CoreErrorKind.ConfigMissing));
            Assert.That(_errors.Errors[0].IsWarning, Is.True);

            _errors.Clear();
            var empty = FeatureFlagLoader.LoadFolder(ProjectPaths.NewTempDir(), _errors);
            Assert.That(empty.Count, Is.EqualTo(0));
            Assert.That(_errors.CountOf(CoreErrorKind.ConfigMissing), Is.EqualTo(1), "an empty folder also warns once");
            Assert.That(_errors.Count, Is.EqualTo(1));
        }

        [Test]
        public void Flags_Folder_MergesFilesInOrdinalOrder_DuplicateIsError()
        {
            string dir = ProjectPaths.NewTempDir();
            File.WriteAllText(Path.Combine(dir, "Core.json"), "{\"schema\":1,\"flags\":{\"core.a\":false}}");
            File.WriteAllText(Path.Combine(dir, "Weather.json"), "{\"schema\":1,\"flags\":{\"weather.gusts\":true}}");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "not a flag file");
            var flags = FeatureFlagLoader.LoadFolder(dir, _errors);
            Assert.That(_errors.Count, Is.EqualTo(0));
            Assert.That(flags.Count, Is.EqualTo(2));
            Assert.That(flags.IsOn("weather.gusts"), Is.True);

            File.WriteAllText(Path.Combine(dir, "Audio.json"), "{\"schema\":1,\"flags\":{\"weather.gusts\":true}}");
            var dup = FeatureFlagLoader.LoadFolder(dir, _errors);
            Assert.That(dup.Count, Is.EqualTo(0), "a duplicate name turns everything off");
            Assert.That(dup.IsOn("weather.gusts"), Is.False);
            Assert.That(_errors.CountOf(CoreErrorKind.ConfigMalformed), Is.EqualTo(1));
            StringAssert.Contains("Weather.json", _errors.Errors[0].Message);   // second file in ordinal order is the offender
            StringAssert.Contains("Audio.json", _errors.Errors[0].Message);

            _errors.Clear();
            File.Delete(Path.Combine(dir, "Audio.json"));
            File.WriteAllText(Path.Combine(dir, "Broken.json"), "{\"schema\":1,\n\"flags\":{\"x\":tru}}");
            var broken = FeatureFlagLoader.LoadFolder(dir, _errors);
            Assert.That(broken.Count, Is.EqualTo(0));
            Assert.That(broken.IsOn("weather.gusts"), Is.False, "one malformed file turns everything off");
            StringAssert.Contains("Broken.json", _errors.Errors[0].Message);
            StringAssert.Contains("line 2, column", _errors.Errors[0].Message);
        }

        [TestCase("{\"schema\":1,\"flags\":{\"x\":true,}}", 1)]
        [TestCase("{\"schema\":1,\n\"flags\":{\"x\":tru}}", 2)]
        [TestCase("{\"schema\":1,\"flags\":{\"x\":\"yes\"}}", 0)]
        [TestCase("{\"schema\":2,\"flags\":{\"x\":true}}", 0)]
        [TestCase("{\"flags\":{\"x\":true}}", 0)]
        [TestCase("[true]", 0)]
        [TestCase("{\"schema\":1,\"flags\":{\"x\":true,\"x\":true}}", 1)]
        public void Flags_Malformed_ReportsAndAllOff(string json, int expectLine)
        {
            var flags = FeatureFlagLoader.Parse(json, "Config/FeatureFlags/Bad.json", _errors);
            Assert.That(flags.IsOn("x"), Is.False);
            Assert.That(_errors.Count, Is.EqualTo(1));
            _errors.Clear();
            flags = FeatureFlagLoader.Merge(new[]
            {
                new KeyValuePair<string, string>("Config/FeatureFlags/A.json", "{\"schema\":1,\"flags\":{\"y\":true}}"),
                new KeyValuePair<string, string>("Config/FeatureFlags/Bad.json", json),
            }, "Config/FeatureFlags", _errors);
            Assert.That(flags.IsOn("y"), Is.False, "a malformed file turns every file's flags off");
            Assert.That(flags.IsOn("x"), Is.False, "a malformed file never turns anything on");
            Assert.That(flags.Count, Is.EqualTo(0));
            Assert.That(_errors.Count, Is.EqualTo(1));
            Assert.That(_errors.Errors[0].Kind, Is.EqualTo(CoreErrorKind.ConfigMalformed));
            StringAssert.Contains("Config/FeatureFlags/Bad.json", _errors.Errors[0].Message);
            if (expectLine > 0) StringAssert.Contains("line " + expectLine + ", column", _errors.Errors[0].Message);
        }

        // C24: the committed file is the one tested.
        public static string CommittedFlagsFolder => ProjectPaths.Of("Assets", "Game", "Config", "FeatureFlags");

        [Test]
        public void Flags_RepoFile_NewFeaturesDefaultOff()
        {
            var files = Directory.GetFiles(CommittedFlagsFolder, "*.json");
            Assert.That(files.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(File.Exists(Path.Combine(CommittedFlagsFolder, "Core.json")), Is.True);
            var flags = FeatureFlagLoader.LoadFolder(CommittedFlagsFolder, _errors);
            Assert.That(_errors.Count, Is.EqualTo(0), "every committed flag file must parse cleanly with no duplicates");
            Assert.That(flags.Count, Is.GreaterThan(0));
            Evidence.Line("grep-counts.txt", "committed flag files=" + files.Length + " flags=" + flags.Count + "\n" + flags.ToCanonicalString().TrimEnd('\n'));
            for (int i = 0; i < flags.Count; i++)
                Assert.That(flags.ValueAt(i), Is.False, flags.NameAt(i) + " must default off (MODULARITY Rule 5)");
        }

        // C25: one immutable snapshot per session; stable order; serialisable.
        [Test]
        public void Flags_Snapshot_StableAndSerialisable()
        {
            var a = FeatureFlagLoader.Parse("{\"schema\":1,\"flags\":{\"zeta\":true,\"alpha\":false,\"Mid\":true,\"mid\":false}}", "a", _errors);
            var b = FeatureFlagLoader.Parse("{\"schema\":1,\"flags\":{\"mid\":false,\"Mid\":true,\"alpha\":false,\"zeta\":true}}", "b", _errors);
            var names = new List<string>();
            for (int i = 0; i < a.Count; i++) names.Add(a.NameAt(i));
            Assert.That(names, Is.EqualTo(new[] { "Mid", "alpha", "mid", "zeta" }), "ordinal order, independent of file order and culture");
            Assert.That(a.ToCanonicalString(), Is.EqualTo(b.ToCanonicalString()));
            Assert.That(a.ToCanonicalString(), Is.EqualTo("Mid=1\nalpha=0\nmid=0\nzeta=1\n"));
            Assert.That(typeof(IFeatureFlags).GetMethod("Set"), Is.Null, "no runtime toggling in M0");
            Assert.That(typeof(FeatureFlagSnapshot).GetMethod("Set"), Is.Null);
        }
    }
}
