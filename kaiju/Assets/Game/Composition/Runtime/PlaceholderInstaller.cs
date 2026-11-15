// T-030c: installs the TEMPORARY placeholder body factory behind units.placeholderBody. Delete with Units.Placeholder when T-005's walk lands.
using System;
using Game.Core;
using Game.Net.ControlBridge.Unity;
using Game.Units.Placeholder;
using Game.Units.Placeholder.Unity;
using UnityEngine;
using EntityId = Game.Core.EntityId;

namespace Game.Composition
{
    /// <summary>Builds placeholder bodies and wires them to a seat. Registered as itself; T-039b resolves it.</summary>
    public sealed class PlaceholderBodyFactory
    {
        public PlaceholderLimits Limits { get; }
        public float MaxHitPoints { get; }
        public PlaceholderBodyFactory(PlaceholderLimits limits, float maxHitPoints) { Limits = limits; MaxHitPoints = maxHitPoints; }

        /// <summary>Adds a <see cref="PlaceholderBodyBehaviour"/> to <paramref name="go"/> (its transform is the start pose) and, when given, subscribes it to the seat's intent frames.</summary>
        public PlaceholderBodyBehaviour Create(GameObject go, EntityId id, MechSeatBehaviour seat = null)
        {
            var beh = go.GetComponent<PlaceholderBodyBehaviour>() ?? go.AddComponent<PlaceholderBodyBehaviour>();
            beh.Configure(new PlaceholderBody(id, Limits, MaxHitPoints));
            if (seat != null) seat.IntentReady += beh.SetIntent;
            return beh;
        }
    }

    public static class PlaceholderInstaller
    {
        /// <summary>Null when units.placeholderBody is off (nothing registered).</summary>
        public static PlaceholderBodyFactory Install(IServiceRegistry registry, IFeatureFlags flags, PlaceholderLimits? limits = null, float maxHitPoints = 1000f)
        {
            if (registry == null || flags == null || !flags.IsOn(PlaceholderFlags.Body)) return null;
            var f = new PlaceholderBodyFactory(limits ?? PlaceholderLimits.Default(), maxHitPoints);
            registry.Register<PlaceholderBodyFactory>(f);
            return f;
        }
    }
}
