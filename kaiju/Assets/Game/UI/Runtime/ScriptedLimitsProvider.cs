using System.Collections.Generic;

namespace Game.UI
{
    /// <summary>
    /// Deterministic limits provider for tests and capture. Each mech has a list of frames; <see cref="Advance"/>
    /// steps every script one frame (the last frame holds). No clock, no randomness.
    /// </summary>
    public sealed class ScriptedLimitsProvider : ILimitsProvider
    {
        readonly Dictionary<int, List<MechLimitsView>> _scripts = new Dictionary<int, List<MechLimitsView>>();
        int _frame;

        public int Frame { get { return _frame; } }

        /// <summary>Constant view for a mech.</summary>
        public void SetStatic(int mechId, MechLimitsView view)
        {
            _scripts[mechId] = new List<MechLimitsView> { view };
        }

        public void SetScript(int mechId, IEnumerable<MechLimitsView> frames)
        {
            var list = new List<MechLimitsView>(frames);
            if (list.Count == 0) { _scripts.Remove(mechId); return; }
            _scripts[mechId] = list;
        }

        /// <summary>Linear heat ramp 0..1 over <paramref name="frames"/> frames with ammo draining to zero; power falls as heat rises.</summary>
        public void SetRamp(int mechId, int frames, int ammoMax)
        {
            if (frames < 2) frames = 2;
            var list = new List<MechLimitsView>(frames);
            for (int i = 0; i < frames; i++)
            {
                float t = (float)i / (frames - 1);
                int ammo = ammoMax - (int)(t * ammoMax + 0.5f);
                list.Add(new MechLimitsView(
                    LimitsAvailability.Power | LimitsAvailability.Heat | LimitsAvailability.Ammo | LimitsAvailability.Strain,
                    1f - 0.5f * t, 1f, t, ammo, ammoMax, t));
            }
            _scripts[mechId] = list;
        }

        public void Advance() { _frame++; }
        public void Reset() { _frame = 0; }

        public bool TryGetLimits(int mechId, out MechLimitsView view)
        {
            List<MechLimitsView> list;
            if (!_scripts.TryGetValue(mechId, out list)) { view = default(MechLimitsView); return false; }
            view = list[_frame < list.Count ? _frame : list.Count - 1];
            return true;
        }
    }
}
