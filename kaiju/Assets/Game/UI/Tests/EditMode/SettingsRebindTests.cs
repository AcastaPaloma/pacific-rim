using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Game.UI.Tests
{
    public class SettingsDataTests
    {
        [Test]
        public void Defaults_match_r122()
        {
            var d = SettingsData.Defaults();
            Assert.AreEqual(TextScaleStep.Small, d.TextScale);
            Assert.AreEqual(100, d.ShakePct); Assert.AreEqual(100, d.FlashPct);
            var a = d.ToAccessibility();
            Assert.AreEqual(1f, a.ShakeGain); Assert.AreEqual(TextScaleStep.Small, a.TextScale);
        }

        [Test]
        public void Json_roundtrip_is_byte_identical()
        {
            var d = SettingsData.Defaults();
            d.PixelProfile = "slate_teal"; d.TextScale = TextScaleStep.Large; d.ShakePct = 0; d.FlashPct = 35; d.HighContrast = true;
            d.PlayerName = "Pilot \"Q\"";
            d.Rebinds.Add(new RebindOverride { ActionId = "jump", BindingIndex = 0, Path = "<Keyboard>/e" });
            string j1 = d.ToJson();
            var r = SettingsData.FromJson(j1);
            Assert.AreEqual(SettingsLoadStatus.Ok, r.Status);
            Assert.AreEqual(j1, r.Data.ToJson());
            Assert.AreEqual("slate_teal", r.Data.PixelProfile);
            Assert.AreEqual(0, r.Data.ShakePct);
            Assert.AreEqual(1, r.Data.Rebinds.Count);
        }

        [Test]
        public void Missing_and_corrupt_give_defaults()
        {
            Assert.AreEqual(SettingsLoadStatus.Missing, SettingsData.FromJson("").Status);
            var c = SettingsData.FromJson("{ not json");
            Assert.AreEqual(SettingsLoadStatus.Corrupt, c.Status);
            Assert.AreEqual(SettingsData.Defaults().ToJson(), c.Data.ToJson());
            Assert.AreEqual(SettingsLoadStatus.Corrupt, SettingsData.FromJson("[1,2]").Status);
        }

        [Test]
        public void Unversioned_file_migrates_and_keeps_values()
        {
            var r = SettingsData.FromJson("{\"audio\": {\"master\": 40}}");
            Assert.AreEqual(SettingsLoadStatus.Migrated, r.Status);
            Assert.AreEqual(0, r.FromVersion);
            Assert.AreEqual(40, r.Data.MasterPct);
            Assert.AreEqual(100, r.Data.ShakePct);
        }

        [Test]
        public void Newer_version_reads_known_fields_and_blocks_overwrite()
        {
            var r = SettingsData.FromJson("{\"version\": 9, \"audio\": {\"master\": 10}, \"future\": {\"x\": 1}}");
            Assert.AreEqual(SettingsLoadStatus.NewerVersion, r.Status);
            Assert.IsTrue(r.ShouldNotOverwrite);
            Assert.AreEqual(10, r.Data.MasterPct);
            string p = Path.Combine(Path.GetTempPath(), "kaiju_settings_" + Guid.NewGuid().ToString("N") + ".json");
            Assert.IsFalse(SettingsStore.Save(p, r.Data, r));
            Assert.IsFalse(File.Exists(p));
        }

        [Test]
        public void Out_of_range_values_are_clamped()
        {
            var r = SettingsData.FromJson("{\"version\":1,\"audio\":{\"master\":500,\"music\":-3},\"accessibility\":{\"shake_pct\":250,\"text_scale\":9},\"graphics\":{\"profile\":\"nope\",\"internal_scale\":5,\"res_w\":3}}");
            Assert.AreEqual(100, r.Data.MasterPct); Assert.AreEqual(0, r.Data.MusicPct);
            Assert.AreEqual(100, r.Data.ShakePct); Assert.AreEqual(TextScaleStep.Small, r.Data.TextScale);
            Assert.AreEqual("lavender_peach", r.Data.PixelProfile); Assert.AreEqual(0, r.Data.InternalScale); Assert.AreEqual(1920, r.Data.ResolutionW);
        }

        [Test]
        public void File_store_roundtrip_and_corrupt_backup()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kaiju_settings_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string p = Path.Combine(dir, "settings.json");
                Assert.AreEqual(SettingsLoadStatus.Missing, SettingsStore.Load(p).Status);
                var d = SettingsData.Defaults(); d.MusicPct = 12;
                Assert.IsTrue(SettingsStore.Save(p, d, null));
                Assert.IsTrue(SettingsStore.Save(p, d, null)); // overwrite
                var r = SettingsStore.Load(p);
                Assert.AreEqual(SettingsLoadStatus.Ok, r.Status); Assert.AreEqual(12, r.Data.MusicPct);
                File.WriteAllText(p, "garbage");
                Assert.AreEqual(SettingsLoadStatus.Corrupt, SettingsStore.Load(p).Status);
                Assert.IsTrue(File.Exists(p + ".bad"));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void Settings_flags_default_off_in_config()
        {
            string j = DataFiles.Read("Assets/Game/Config/FeatureFlags/UI.json");
            Assert.IsTrue(j.Contains("\"" + UiFlags.HudData + "\": false"));
            Assert.IsTrue(j.Contains("\"" + UiFlags.Settings + "\": false"));
            Assert.IsTrue(j.Contains("\"" + UiFlags.Hud + "\": false"));
        }
    }

    public class RebindModelTests
    {
        [Test]
        public void All_actions_rebindable_and_ids_stable()
        {
            var m = RebindModel.CreateDefault();
            foreach (var a in m.Actions) Assert.IsTrue(a.Rebindable, a.Id);
            Assert.AreEqual(RebindModel.StableBindingId("jump", 0), m.Find("jump").Bindings[0].Id);
            Assert.AreNotEqual(m.Find("jump").Bindings[0].Id, m.Find("jump").Bindings[1].Id);
        }

        [Test]
        public void Defaults_have_no_conflicts()
        {
            Assert.IsEmpty(RebindModel.CreateDefault().Conflicts());
        }

        [Test]
        public void Override_conflict_and_reset()
        {
            var m = RebindModel.CreateDefault();
            Assert.IsTrue(m.SetOverride("jump", 0, "<Keyboard>/w"));
            var c = m.Conflicts();
            Assert.AreEqual(1, c.Count);
            CollectionAssert.AreEquivalent(new[] { "jump#0", "move_forward#0" }, c[0]);
            m.ResetAction("jump");
            Assert.IsEmpty(m.Conflicts());
            Assert.AreEqual("<Keyboard>/space", m.EffectivePath("jump", 0));
        }

        [Test]
        public void Invalid_overrides_are_refused()
        {
            var m = RebindModel.CreateDefault();
            Assert.IsFalse(m.SetOverride("nope", 0, "<Keyboard>/x"));
            Assert.IsFalse(m.SetOverride("jump", 5, "<Keyboard>/x"));
            Assert.IsFalse(m.SetOverride("jump", 0, ""));
            Assert.IsFalse(m.SetOverride("jump", 0, "<Gamepad>/buttonNorth"), "pad path on a keyboard binding");
            Assert.AreEqual(0, m.OverrideCount);
            Assert.IsTrue(m.SetOverride("jump", 0, "<Keyboard>/e"));
            Assert.IsTrue(m.SetOverride("jump", 0, "<Keyboard>/space"), "setting back to default removes the override");
            Assert.AreEqual(0, m.OverrideCount);
        }

        [Test]
        public void Settings_roundtrip_keeps_overrides_byte_identical()
        {
            var m = RebindModel.CreateDefault();
            m.SetOverride("fire", 0, "<Mouse>/rightButton"); m.SetOverride("fire", 1, "<Gamepad>/leftTrigger"); m.SetOverride("pause", 0, "<Keyboard>/p");
            var s = SettingsData.Defaults(); s.Rebinds = m.GetOverrides();
            string j = s.ToJson();
            var m2 = RebindModel.CreateDefault();
            Assert.AreEqual(3, m2.ApplyOverrides(SettingsData.FromJson(j).Data.Rebinds));
            Assert.AreEqual(m.GetOverrides().Count, m2.GetOverrides().Count);
            var s2 = SettingsData.Defaults(); s2.Rebinds = m2.GetOverrides();
            Assert.AreEqual(j, s2.ToJson());
        }

        [Test]
        public void Unity_override_json_roundtrip()
        {
            var m = RebindModel.CreateDefault();
            m.SetOverride("melee", 0, "<Keyboard>/g"); m.SetOverride("move_back", 1, "<Gamepad>/dpad/down");
            string u = m.ToUnityOverridesJson();
            StringAssert.Contains("\"bindings\"", u);
            StringAssert.Contains(m.Find("melee").Bindings[0].Id, u);
            var m2 = RebindModel.CreateDefault();
            Assert.AreEqual(2, m2.LoadUnityOverridesJson(u));
            Assert.AreEqual("<Keyboard>/g", m2.EffectivePath("melee", 0));
            Assert.AreEqual(u, m2.ToUnityOverridesJson());
            Assert.AreEqual(-1, m2.LoadUnityOverridesJson("oops"));
            Assert.AreEqual(2, m2.OverrideCount, "state unchanged by a failed load");
        }
    }
}
