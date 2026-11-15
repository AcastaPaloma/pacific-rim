// T-189: the feel-check layer of the slice (TEMPORARY, behind combat.* / hangar.stub). Mounts the real Combat weapons on the placeholder body,
// gives the kaiju stand-in hit points (Combat's LinearEntityHealth), runs the hangar repair-and-redeploy cycle, counts what happened for the
// smoke summary, and holds the on-screen control help. SliceBootstrap only calls into it; with every flag off it is never created.
using System;
using System.Globalization;
using System.Text;
using Game.Combat;
using Game.Composition;
using Game.Core;
using Game.Hangar;
using Game.Slice;
using Game.Units.Placeholder;
using Game.Units.Placeholder.Unity;
using UnityEngine;
using EntityId = Game.Core.EntityId;

namespace Game.Slice
{
    public sealed class SliceFeelRig : IEventHandler<WeaponFired>, IEventHandler<ImpactImpulse>
    {
        /// <summary>ASSUMED (feel placeholder): kaiju stand-in hit points. One 1225 kg shell at 762 m/s deals ~50 hp through LinearEntityHealth, so ~8 hits.</summary>
        public const double KaijuHitPoints = 400.0;
        public const double KaijuMassKg = 2.5e7, KaijuRadiusM = 15.0, KaijuHeightM = 100.0;
        public const double RespawnDelayS = 4.0, KaijuAheadM = 340.0;

        public const string Help = "WASD move   Space stop   F cannon   R arm swing   Q quake   I impactor   H hangar (repair + redeploy)";

        readonly GameCompositionResult _comp;
        readonly IEventBus _bus;
        readonly SliceCombatWorld _world;
        readonly Func<double, double, double> _groundLocal;   // terrain height at scene-local (x, z)
        readonly Action<string> _log;
        PlaceholderBodyBehaviour _body; SliceKaijuAgent _kaiju; SliceTarget _mech;
        CannonMount _cannon; MeleeMount _melee;
        HangarService _hangar; PlaceholderDamageAdapter _damage;
        double _respawnAt = -1; double _originX, _originZ;

        public int Shots, Swings, WeaponFiredEvents, ImpactImpulseEvents, BuildingImpacts, KaijuImpacts, GroundImpacts, KaijuDowns, HangarCycles;
        public double LastKaijuHp = KaijuHitPoints, MinKaijuHp = KaijuHitPoints;
        public bool CombatOn => _comp.Combat != null;
        public bool HangarOn => _hangar != null;
        public int Ammo => _cannon != null ? _cannon.Cannon.Ammo : 0;
        public int MaxAmmo => _cannon != null ? _cannon.Cannon.Spec.MaxAmmo : 0;
        public double KaijuHpFraction => Math.Max(0.0, Math.Min(1.0, LastKaijuHp / KaijuHitPoints));
        public string SpawnNote = "";

        public SliceFeelRig(GameCompositionResult comp, IEventBus bus, SliceCombatWorld world, Func<double, double, double> groundLocal, Action<string> log)
        { _comp = comp; _bus = bus; _world = world; _groundLocal = groundLocal; _log = log ?? (s => { }); }

        /// <summary>Mounts the enabled weapons on the placeholder body, registers the kaiju's health and counts the combat events. Call once the actors exist.</summary>
        public void Attach(PlaceholderBodyBehaviour body, SliceKaijuAgent kaiju, SliceTarget mech, double originX, double originZ)
        {
            _body = body; _kaiju = kaiju; _mech = mech; _originX = originX; _originZ = originZ;
            if (_comp.Combat != null && body != null)
            {
                var combat = _comp.Combat;
                var mount = new PlaceholderWeaponMount(body.Body);
                var env = BallisticEnvironment.Standard(); env.GroundY = double.NaN;   // the scene's terrain is sampled by SliceCombatWorld, no flat plane
                if (combat.CannonEnabled) _cannon = combat.System.AddCannon(mount, CannonSpec.Heavy(), 5, body.Body.Limits.HeightM * 0.7f, env);
                if (combat.MeleeEnabled) _melee = combat.System.AddMelee(mount, new ArmSpec { ShoulderHeightM = body.Body.Limits.HeightM * 0.7 });
                combat.Health?.Register(kaiju.Id, KaijuHitPoints);
                _bus?.Subscribe<WeaponFired>(this); _bus?.Subscribe<ImpactImpulse>(this);
                _log("combat: cannon " + (_cannon != null) + " melee " + (_melee != null) + ", kaiju " + KaijuHitPoints.ToString("F0", CultureInfo.InvariantCulture) + " hp");
            }
            if (_comp.Hangar != null && body != null)
            {
                _hangar = _comp.Hangar;
                _damage = new PlaceholderDamageAdapter(body.Body.Damage, PlayerSlot.FromIndex(0), () => _comp.Combat != null ? _comp.Combat.System.StepIndex : 0);
                _log("hangar: stub on, site " + _hangar.Site.Name);
            }
        }

        public void Dispose() { if (_bus != null) { _bus.Unsubscribe<WeaponFired>(this); _bus.Unsubscribe<ImpactImpulse>(this); } }

        public void Handle(in WeaponFired e) { WeaponFiredEvents++; }
        public void Handle(in ImpactImpulse e)
        {
            ImpactImpulseEvents++;
            switch ((TargetKind)e.TargetKind) { case TargetKind.Building: BuildingImpacts++; break; case TargetKind.Kaiju: KaijuImpacts++; break; case TargetKind.Ground: GroundImpacts++; break; }
        }

        /// <summary>Cannon shot along the mech heading, with a soft aim assist onto the kaiju when it is within 35 degrees and 2 km.</summary>
        public bool FireCannon()
        {
            if (_cannon == null || _body == null) return false;
            var b = _body.Body;
            double yaw = 0, pitch = 0.02;
            double dx = _kaiju.X - b.X, dz = _kaiju.Y - b.Z, dist = Math.Sqrt(dx * dx + dz * dz);
            if (dist > 20 && dist < 2000)
            {
                double rel = Wrap(Math.Atan2(dx, dz) - b.Heading);
                if (Math.Abs(rel) < 35.0 * Math.PI / 180.0)
                {
                    double kbase = _groundLocal(_kaiju.X, _kaiju.Y), dy = (kbase + KaijuHeightM * 0.6) - (b.Y + b.Limits.HeightM * 0.7);
                    yaw = rel; pitch = Math.Atan2(dy, dist) + 0.5 * 9.81 * dist / (762.0 * 762.0);   // flat-fire drop compensation
                }
            }
            bool ok = _comp.Combat.System.Fire(_cannon, yaw, pitch);
            if (ok) Shots++;
            return ok;
        }

        /// <summary>Overhead arm swing (50 deg down to -30 deg) along the mech heading.</summary>
        public bool SwingArm()
        {
            if (_melee == null) return false;
            bool ok = _comp.Combat.System.Swing(_melee, new SwingCommand { YawRelative = 0, StartElevation = 50 * Math.PI / 180, EndElevation = -30 * Math.PI / 180, Duration = 1.5 });
            if (ok) Swings++;
            return ok;
        }

        /// <summary>One fixed step (after the kaiju moved): refresh the kaiju volume, step Combat, mirror kaiju health, handle death and respawn.</summary>
        public void FixedStep(double dt, double timeS)
        {
            if (_comp.Combat == null) return;
            if (_respawnAt < 0)
            {
                double gy = _groundLocal(_kaiju.X, _kaiju.Y);
                _world.SetKaiju(_kaiju.Id, _kaiju.X, _kaiju.Y, gy, KaijuRadiusM, KaijuHeightM, KaijuMassKg, new CVec3(Math.Cos(_kaiju.HeadingRad) * _kaiju.Speed, 0, Math.Sin(_kaiju.HeadingRad) * _kaiju.Speed));
            }
            _comp.Combat.Step(dt);
            if (_respawnAt < 0 && _comp.Combat.Health != null && _comp.Combat.Health.TryGetHitPoints(_kaiju.Id, out double hp))
            {
                LastKaijuHp = hp; if (hp < MinKaijuHp) MinKaijuHp = hp; _kaiju.Hp = (float)KaijuHpFraction;
                if (hp <= 0.0) { KaijuDowns++; _respawnAt = timeS + RespawnDelayS; _world.ClearKaiju(); _log("kaiju down (" + KaijuDowns + "); a new one walks in after " + RespawnDelayS.ToString("F0", CultureInfo.InvariantCulture) + " s"); }
            }
            if (_respawnAt >= 0 && timeS >= _respawnAt) { _respawnAt = -1; RespawnKaiju(); }
        }

        /// <summary>Smoke only: stands the kaiju 90 m straight ahead of the mech so the scripted cannon fire proves the kaiju hit and health path (the stand-in is otherwise 340 m away behind city blocks).</summary>
        public void ScriptedDuel()
        {
            if (_body == null || _respawnAt >= 0) return;
            var b = _body.Body;
            _kaiju.X = b.X + (float)(Math.Sin(b.Heading) * 90.0); _kaiju.Y = b.Z + (float)(Math.Cos(b.Heading) * 90.0); _kaiju.Speed = 0f;
            _log("smoke duel: kaiju placed 90 m ahead");
        }

        public bool KaijuDown => _respawnAt >= 0;

        void RespawnKaiju()
        {
            var b = _body != null ? _body.Body : null;
            float mx = b != null ? b.X : (float)_mech.X, mz = b != null ? b.Z : (float)_mech.Y, h = b != null ? b.Heading : 0f;
            _kaiju.X = mx + (float)(Math.Sin(h) * KaijuAheadM) + (float)(Math.Cos(h) * 90.0);
            _kaiju.Y = mz + (float)(Math.Cos(h) * KaijuAheadM) - (float)(Math.Sin(h) * 90.0);
            _kaiju.HeadingRad = (float)Math.Atan2(mz - _kaiju.Y, mx - _kaiju.X); _kaiju.Speed = 0f; _kaiju.Hp = 1f;
            LastKaijuHp = KaijuHitPoints; _comp.Combat?.Health?.Register(_kaiju.Id, KaijuHitPoints);
            _log("kaiju respawned 340 m ahead");
        }

        /// <summary>Hangar cycle: the fight is over, the body is repaired at the hangar and redeploys at the site (Crissy Field). Also refills the cannon and re-arms the kaiju stand-in.</summary>
        public bool HangarCycle(Func<double, double, double> groundWorld)
        {
            if (_hangar == null || _body == null) return false;
            try
            {
                var slot = _damage.Slot;
                float hpBefore = _body.Body.Damage.HitPoints;
                _hangar.EnterHangar(_damage);
                int queued = _hangar.QueueAllDamaged(slot); int done = _hangar.ProcessRepairs(slot);
                var sp = _hangar.LeaveHangar(_damage);
                HangarCycles++;
                if (_cannon != null) _cannon.Cannon.Resupply(_cannon.Cannon.Spec.MaxAmmo);
                float x = (float)(sp.X - _originX), z = (float)(sp.Z - _originZ);
                double gy = double.IsNaN(sp.Y) ? groundWorld(sp.X, sp.Z) : sp.Y;
                float y = (float)(double.IsNaN(gy) ? _body.Body.Y : gy - 0.0), hd = (float)(sp.HeadingDeg * Math.PI / 180.0);
                _body.Body.Place(x, y, z, hd);
                _body.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0f, hd * Mathf.Rad2Deg, 0f));
                _mech.X = x; _mech.Y = z; _mech.Hp = 1f; _mech.Alive = true;
                SpawnNote = "hangar cycle " + HangarCycles + ": hp " + hpBefore.ToString("F0", CultureInfo.InvariantCulture) + " -> " + _body.Body.Damage.HitPoints.ToString("F0", CultureInfo.InvariantCulture)
                    + " (" + queued + " queued, " + done + " repaired), redeployed at " + sp.Site.Name + " (" + x.ToString("F0", CultureInfo.InvariantCulture) + ", " + z.ToString("F0", CultureInfo.InvariantCulture) + "), terrain known " + sp.TerrainKnown;
                _log(SpawnNote);
                _respawnAt = -1; RespawnKaiju();
                return true;
            }
            catch (Exception ex) { _log("hangar cycle failed: " + ex.GetType().Name + ": " + ex.Message); return false; }
        }

        static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a <= -Math.PI) a += 2 * Math.PI; return a; }

        public string HudLine()
        {
            var sb = new StringBuilder();
            if (CombatOn) sb.Append("ammo ").Append(Ammo).Append('/').Append(MaxAmmo).Append("   kaiju hp ").Append((KaijuHpFraction * 100).ToString("F0", CultureInfo.InvariantCulture)).Append('%').Append(KaijuDown ? " (down)" : "").Append("   shots ").Append(Shots).Append(" hits k").Append(KaijuImpacts).Append("/b").Append(BuildingImpacts);
            if (HangarOn) sb.Append("   hangar cycles ").Append(HangarCycles);
            return sb.ToString();
        }

        public void AppendJson(StringBuilder sb)
        {
            sb.Append("  \"feel\": { \"combat\": ").Append(CombatOn ? "true" : "false").Append(", \"hangar\": ").Append(HangarOn ? "true" : "false")
              .Append(", \"cannon_shots\": ").Append(Shots).Append(", \"arm_swings\": ").Append(Swings)
              .Append(", \"weapon_fired_events\": ").Append(WeaponFiredEvents).Append(", \"impact_impulse_events\": ").Append(ImpactImpulseEvents)
              .Append(", \"impacts_building\": ").Append(BuildingImpacts).Append(", \"impacts_kaiju\": ").Append(KaijuImpacts).Append(", \"impacts_ground\": ").Append(GroundImpacts)
              .Append(", \"kaiju_min_hp\": ").Append(MinKaijuHp.ToString("F1", CultureInfo.InvariantCulture)).Append(", \"kaiju_hp\": ").Append(LastKaijuHp.ToString("F1", CultureInfo.InvariantCulture)).Append(", \"kaiju_downs\": ").Append(KaijuDowns)
              .Append(", \"hangar_cycles\": ").Append(HangarCycles).Append(", \"ammo\": ").Append(Ammo)
              .Append(", \"combat_buildings_in_world\": ").Append(_world.BuildingCount).Append(" },\n");
        }
    }
}
