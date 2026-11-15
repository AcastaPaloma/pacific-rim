// T-122: GameComposition.Install and the meta flags (Unity EditMode; GameComposition references the Unity adapters, so this file is not in the dotnet project).
using System.Collections.Generic;
using System.IO;
using Game.Core;
using NUnit.Framework;

namespace Game.Composition.Tests
{
    public class MetaGameCompositionTests
    {
        private sealed class Errors : ICoreErrorSink
        {
            public readonly List<CoreError> List = new List<CoreError>();
            public void Report(CoreError e) => List.Add(e);
        }

        private static FeatureFlagSnapshot Flags(bool on) => new FeatureFlagSnapshot(new List<KeyValuePair<string, bool>>
        {
            new KeyValuePair<string, bool>("meta.economy", on), new KeyValuePair<string, bool>("meta.salvage", on),
            new KeyValuePair<string, bool>("meta.permadeath", on), new KeyValuePair<string, bool>("meta.refit", on),
        });

        private static string ConfigRoot() => Path.Combine(UnityEngine.Application.dataPath, "Game", "Config");

        [Test]
        public void MetaFlagsOff_NoMetaInstall_NoFileRead()
        {
            int reads = 0;
            var saves = new SaveSystem("t");
            var reg = new ServiceRegistry();
            var r = GameComposition.Install(reg, Flags(false), new Errors(), new GameCompositionInputs { Meta = new MetaInputs { ReadConfig = p => { reads++; return null; } } }, saves);
            Assert.That(r.Meta, Is.Null); Assert.That(reads, Is.EqualTo(0));
            Assert.That(reg.IsRegistered<IAssetValueProvider>(), Is.False);
            Assert.That(saves.Capture().TryGetSection("economy", out _), Is.False);
        }

        [Test]
        public void MetaFlagsOn_InstallsServices_AndRegistersSaveSections()
        {
            var saves = new SaveSystem("t");
            var err = new Errors();
            var r = GameComposition.Install(new ServiceRegistry(), Flags(true), err, new GameCompositionInputs { Meta = MetaInputs.FromDirectory(ConfigRoot()) }, saves);
            Assert.That(r.Meta, Is.Not.Null); Assert.That(r.Meta.Economy, Is.Not.Null); Assert.That(r.Meta.Refit, Is.Not.Null);
            var doc = saves.Capture();
            foreach (var s in new[] { "economy", "salvage", "roster", "refit" }) Assert.That(doc.TryGetSection(s, out _), Is.True, s);
            // no hangar in this install (hangar.stub off): reported, refits refused
            Assert.That(err.List.Exists(e => e.Message.Contains("no hangar")), Is.True);
        }

        [Test]
        public void MetaFlagsOn_WithoutInputs_IsReportedNotInstalled()
        {
            var err = new Errors();
            var r = GameComposition.Install(new ServiceRegistry(), Flags(true), err, new GameCompositionInputs(), new SaveSystem("t"));
            Assert.That(r.Meta, Is.Not.Null); Assert.That(r.Meta.Economy, Is.Null);
            Assert.That(err.List.Count, Is.EqualTo(1));
        }
    }
}
