using UnityEngine;

namespace Game.Units.Mech.Art
{
    /// <summary>
    /// Marks the root of a mech art prefab and points at its part map. Data only: no behaviour,
    /// no physics (colliders and ArticulationBody belong to the Units.Mech body task, T-005).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MechArtRoot : MonoBehaviour
    {
        public MechPartMap partMap;
    }
}
