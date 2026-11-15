// Shared fakes for the Combat tests.
using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Combat.Tests
{
    sealed class RecordingBus : IEventBus
    {
        public readonly List<object> Events = new List<object>();
        public void Subscribe<T>(IEventHandler<T> handler) where T : unmanaged, IEvent { }
        public bool Unsubscribe<T>(IEventHandler<T> handler) where T : unmanaged, IEvent { return false; }
        public void Publish<T>(in T evt) where T : unmanaged, IEvent { Events.Add(evt); }
        public int SubscriberCount<T>() where T : unmanaged, IEvent { return 0; }
        public int HandlerExceptionCount => 0;
        public int Count<T>() { int n = 0; foreach (var e in Events) if (e is T) n++; return n; }
        public List<T> Of<T>() { var l = new List<T>(); foreach (var e in Events) if (e is T t) l.Add(t); return l; }
    }

    sealed class FakeMount : IWeaponMount
    {
        public EntityId Id { get; set; } = EntityId.FromValue(1);
        public CVec3 Position, Velocity;
        public double Heading;
        public void GetPose(out CVec3 position, out double heading, out CVec3 velocity) { position = Position; heading = Heading; velocity = Velocity; }
    }

    sealed class RecordingListener : IImpactListener
    {
        public readonly List<ImpactRecord> Records = new List<ImpactRecord>();
        public void OnImpact(in ImpactRecord impact) { Records.Add(impact); }
    }
}
