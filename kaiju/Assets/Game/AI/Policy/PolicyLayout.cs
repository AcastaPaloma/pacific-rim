using System;

namespace Game.AI.Policy
{
    /// <summary>
    /// Fixed observation / action layout of the RL tactical policy (R-028 sections 2 and 3). The C# encoder and decoder
    /// are the single source of truth: the training bridge sends these sizes in its hello message and Python reads them.
    /// Any change to a size or a feature order bumps <see cref="LayoutVersion"/> (a trained policy is tied to one layout).
    /// </summary>
    public static class PolicyLayout
    {
        public const int LayoutVersion = 1;
        public const int NT = BrainContracts.MaxTargets; // 8 target slots
        public const int NA = BrainContracts.MaxAllies;  // 7 ally slots

        public const int SelfDim = 25;
        public const int TargetDim = 15;
        public const int AllyDim = 24;
        public const int TargetsOffset = SelfDim;
        public const int AlliesOffset = SelfDim + NT * TargetDim;
        public const int ObsDim = SelfDim + NT * TargetDim + NA * AllyDim; // 313

        public const int NTargetChoices = NT + 1; // 0 = none, 1 + slot
        public const int NManeuvers = 7;
        public const int NSpeeds = 3;
        public const int MaskTargetOffset = 0;
        public const int MaskManeuverOffset = NTargetChoices;
        public const int MaskSpeedOffset = NTargetChoices + NManeuvers;
        public const int MaskDim = NTargetChoices + NManeuvers + NSpeeds; // 19

        public const int StateOneHot = 6; // BrainState 0..5

        /// <summary>True when maneuver <paramref name="m"/> needs a target (the decoder downgrades it to Hold otherwise).</summary>
        public static bool NeedsTarget(Maneuver m) =>
            m == Maneuver.Approach || m == Maneuver.FlankLeft || m == Maneuver.FlankRight || m == Maneuver.Charge;

        /// <summary>Conditional mask: maneuver <paramref name="m"/> is allowed given the chosen target choice and the base mask.</summary>
        public static bool ManeuverAllowed(int targetChoice, Maneuver m, byte[] mask)
        {
            if (mask[MaskManeuverOffset + (int)m] == 0) return false;
            return targetChoice != 0 || !NeedsTarget(m);
        }
    }

    /// <summary>Maneuver primitives (R-028 section 3). The policy picks one per decision; the decoder turns it into an intent.</summary>
    public enum Maneuver : byte
    {
        Hold = 0,       // speed 0, face the target (or keep heading)
        Approach = 1,   // close to the standoff radius, then hold
        FlankLeft = 2,  // orbit the target at standoff, counter-clockwise step
        FlankRight = 3, // orbit the target at standoff, clockwise step
        Charge = 4,     // close at the speed tier; melee when in reach (the only maneuver that strikes)
        Retreat = 5,    // move away from the nearest visible threat
        Regroup = 6,    // move toward the live-ally centroid
    }

    /// <summary>One factored decision: target choice (0 none, 1+slot), maneuver, speed tier.</summary>
    public struct PolicyAction
    {
        public int Target;
        public int Maneuver;
        public int Speed;
        public PolicyAction(int target, int maneuver, int speed) { Target = target; Maneuver = maneuver; Speed = speed; }
    }

    /// <summary>Policy-side constants that are not in <see cref="BrainConfig"/> (all Assumed, R-028 section 3).</summary>
    public struct PolicyConfig
    {
        public float SpeedTier0, SpeedTier1, SpeedTier2; // R-024 speed fractions 0.6 / 0.8 / 1.0
        public float FlankStepRad;      // orbit waypoint step around the target
        public float RegroupRadiusM;    // regroup stops inside this distance of the ally centroid
        public float CooldownNormSeconds; // melee cooldown normaliser (harness MeleeCooldownSeconds 2 s)
        public float EgoClamp;          // ego coordinates are clamped to +-EgoClamp * SightRangeM

        public static PolicyConfig Default() => new PolicyConfig
        {
            SpeedTier0 = 0.6f, SpeedTier1 = 0.8f, SpeedTier2 = 1.0f,
            FlankStepRad = 60f * 0.017453292519943295f, RegroupRadiusM = 150f, CooldownNormSeconds = 2f, EgoClamp = 2f,
        };

        public float Tier(int s) => s <= 0 ? SpeedTier0 : (s == 1 ? SpeedTier1 : SpeedTier2);
    }

    /// <summary>Brain-side memory the encoder feeds back and the decoder updates (no allocation).</summary>
    public struct PolicyMemory
    {
        public ulong CurrentTargetId;
        public int LastManeuver;
        public double LastSwitchTime;   // last time the target changed to a non-zero id
        public ulong LastDroppedId;     // last usable target released to "none"
        public double LastDropTime;

        public static PolicyMemory Initial() => new PolicyMemory
        {
            CurrentTargetId = 0, LastManeuver = 0, LastSwitchTime = double.NegativeInfinity, LastDroppedId = 0, LastDropTime = double.NegativeInfinity,
        };
    }

    /// <summary>Flag names of the RL policy brain. Nothing installs the policy yet (no module); the flag is reserved, default off.</summary>
    public static class PolicyFlags
    {
        public const string Policy = "kaiju.policyBrain";
    }

    internal static class PolicyMath
    {
        public static float Wrap(float a)
        {
            const float TwoPi = 6.2831853071795864f;
            if (float.IsNaN(a) || float.IsInfinity(a)) return 0f;
            if (a > 64f || a < -64f) a -= TwoPi * MathF.Floor(a / TwoPi);
            while (a > MathF.PI) a -= TwoPi;
            while (a <= -MathF.PI) a += TwoPi;
            return a;
        }

        public static float Dist(Float2 a, Float2 b) { float dx = b.X - a.X, dy = b.Y - a.Y; return MathF.Sqrt(dx * dx + dy * dy); }
        public static float Dir(Float2 from, Float2 to) => MathF.Atan2(to.Y - from.Y, to.X - from.X);
        public static float Clamp(float v, float lo, float hi) => float.IsNaN(v) ? (lo > 0f ? lo : (hi < 0f ? hi : 0f)) : v < lo ? lo : (v > hi ? hi : v);
    }
}
