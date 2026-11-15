// T-040: mounts the engine-free combat weapons on the TEMPORARY placeholder body behind combat.melee / combat.cannon.
// Delete the placeholder mount (not Combat) when T-005's walking body replaces the placeholder: it only adapts pose.
using System;
using Game.Combat;
using Game.Core;
using Game.Units.Placeholder;
using Game.World.Destruction;

namespace Game.Composition
{
    /// <summary>Adapts a placeholder body to <see cref="IWeaponMount"/> (pose and velocity on the ground plane).</summary>
    public sealed class PlaceholderWeaponMount : IWeaponMount
    {
        private readonly PlaceholderBody _body;
        public PlaceholderWeaponMount(PlaceholderBody body) { _body = body ?? throw new ArgumentNullException(nameof(body)); }
        public EntityId Id => _body.Id;
        public void GetPose(out CVec3 position, out double heading, out CVec3 velocity)
        {
            position = new CVec3(_body.X, _body.Y, _body.Z); heading = _body.Heading; velocity = new CVec3(_body.VelX, 0, _body.VelZ);
        }
    }

    /// <summary>Registered as itself. The scene host calls <see cref="Step"/> with its fixed step; recoil output is left in <see cref="CombatSystem.RecoilOutbox"/> for the real body (the placeholder is kinematic and ignores it).</summary>
    public sealed class CombatHandle
    {
        public CombatSystem System { get; }
        public LinearEntityHealth Health { get; }
        public bool CannonEnabled { get; }
        public bool MeleeEnabled { get; }

        internal CombatHandle(CombatSystem system, LinearEntityHealth health, bool cannon, bool melee)
        { System = system; Health = health; CannonEnabled = cannon; MeleeEnabled = melee; }

        /// <summary>Mounts the enabled weapons on <paramref name="body"/>; either result is null when its flag is off.</summary>
        public void Mount(PlaceholderBody body, out CannonMount cannon, out MeleeMount melee, CannonSpec cannonSpec = null, ArmSpec armSpec = null)
        {
            var m = new PlaceholderWeaponMount(body);
            cannon = CannonEnabled ? System.AddCannon(m, cannonSpec ?? CannonSpec.Heavy(), 5, body.Limits.HeightM * 0.7f) : null;
            melee = MeleeEnabled ? System.AddMelee(m, armSpec ?? new ArmSpec { ShoulderHeightM = body.Limits.HeightM * 0.7 }) : null;
        }

        public void Step(double dt) => System.Step(dt);
    }

    public static class CombatInstaller
    {
        /// <summary>Null when both flags are off (nothing registered). Uses the registered IEventBus, IDamageModel, ICombatTargetQuery and IEntityHealth when present.</summary>
        public static CombatHandle Install(IServiceRegistry registry, IFeatureFlags flags, ICoreErrorSink errors)
        {
            if (registry == null || flags == null) return null;
            bool cannon = flags.IsOn(CombatFlags.Cannon), melee = flags.IsOn(CombatFlags.Melee);
            if (!cannon && !melee) return null;
            registry.TryResolve(out IEventBus bus);
            registry.TryResolve(out IDamageModel damage);
            ICombatTargetQuery query;
            if (!registry.TryResolve(out query))
            {
                errors?.Report(new CoreError(CoreErrorKind.ConfigMissing, "[Combat] no ICombatTargetQuery registered; weapons run against an empty world (no hits)."));
                query = new SimpleTargetWorld();
            }
            LinearEntityHealth linear = null;
            if (!registry.TryResolve(out IEntityHealth health)) { linear = new LinearEntityHealth(); health = linear; registry.Register<IEntityHealth>(health); }
            var handle = new CombatHandle(new CombatSystem(bus, query, new ImpactRouter(bus, damage, health)), linear, cannon, melee);
            registry.Register<CombatHandle>(handle);
            return handle;
        }
    }
}
