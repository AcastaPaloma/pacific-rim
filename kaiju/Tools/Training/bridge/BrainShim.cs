extern alias scripted;
using System;

namespace Game.AI
{
    /// <summary>
    /// Bridge-only stand-in for <c>Game.AI.ScriptedKaijuBrain</c> (the harness source does <c>new ScriptedKaijuBrain()</c> per kaiju).
    /// The worker thread sets <see cref="Factory"/> before constructing a harness; kaiju index i gets <c>Factory(i)</c>, or the real
    /// scripted brain when no factory is set. Remove once the harness exposes a brain factory (R-028 5.3, T-041 progress "open").
    /// </summary>
    public sealed class ScriptedKaijuBrain : IKaijuBrain
    {
        [ThreadStatic] public static Func<int, IKaijuBrain> Factory;
        [ThreadStatic] public static int NextIndex;

        private readonly IKaijuBrain _inner;

        public ScriptedKaijuBrain()
        {
            int i = NextIndex++;
            _inner = Factory != null ? Factory(i) : new scripted::Game.AI.ScriptedKaijuBrain();
        }

        public IKaijuBrain Inner => _inner;
        public int ContractVersion => _inner.ContractVersion;
        public void Reset(ulong seed, in BrainConfig cfg) => _inner.Reset(seed, cfg);
        public void Decide(in BrainObservation obs, ref BrainIntent intent) => _inner.Decide(obs, ref intent);

        public static IKaijuBrain NewScripted() => new scripted::Game.AI.ScriptedKaijuBrain();
    }
}
