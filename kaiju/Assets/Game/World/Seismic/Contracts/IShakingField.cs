// World.Seismic shaking contract (R-022 section 3): per-site median ground motion of one quake, for Destruction fragility (R-021).
namespace Game.World.Seismic
{
    /// <summary>Median shaking at one site. PGA in g, PGV in cm/s, MMI (Worden 2012 from PGV), Rjb in metres.</summary>
    public struct ShakingSample
    {
        public float PgaG;
        public float PgvCmS;
        public float Mmi;
        public float RjbM;
    }

    public interface IShakingField
    {
        /// <summary>False when <paramref name="eventId"/> has no rupture in this field. (x, z) are BayTM metres; vs30 in m/s.</summary>
        bool TryGetShaking(long eventId, double x, double z, double vs30, out ShakingSample s);
    }
}
