using Game.Units.Mech.Contracts;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>DEF-T005r2-arch-2: the imp = 1 stance schedule lives in walk_tuning.json; the loaded rows must equal the literals it replaced.</summary>
    public class StanceScheduleConfigTests
    {
        static readonly double[][] Expected =
        {
            new[] { 0.100, 0.6401, 41.147, 0.7026, -0.2374, -0.4100, 0.2271, 23.71, 0.2776, 0.7240, 10.81, 10.00, 8.0 },
            new[] { 0.125, 0.6145, 41.053, 0.6866, -0.3409, -0.3309, 0.3318, 24.98, 0.2503, 0.6967, 12.87, 10.00, 8.0 },
            new[] { 0.150, 0.5777, 40.983, 0.5941, -0.4104, -0.1699, 0.3948, 25.00, 0.2161, 0.6625, 15.84, 10.00, 8.0 },
            new[] { 0.175, 0.5732, 40.875, 0.6021, -0.4769, -0.1361, 0.4114, 25.00, 0.2101, 0.6565, 17.31, 10.00, 8.0 },
            new[] { 0.200, 0.5614, 41.060, 0.1521, -0.5277, 0.0488, 0.4258, 25.00, 0.2063, 0.6527, 20.37, 10.00, 8.0 },
        };

        [Test]
        public void LoadedRowsEqualOriginalLiterals()
        {
            var rows = Repo.Walk().imp_stance_schedule;
            Assert.IsNotNull(rows); Assert.AreEqual(Expected.Length, rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                var got = new[] { r.froude, r.reach_frac, r.hip_height, r.hip_c1, r.hip_s1, r.hip_c2, r.hip_s2, r.heel_strike_deg, r.foot_flat_at, r.heel_rise_at, r.toe_off_deg, r.tilt_deg, r.yaw_deg };
                for (int k = 0; k < 13; k++) Assert.AreEqual(Expected[i][k], got[k], 0.0, $"row {i} col {k}");
            }
        }

        [Test]
        public void MissingScheduleFailsLoudly()
        {
            var t = Repo.Walk(); t.imp = 1; t.imp_stance_schedule = null;
            var geo = Game.Units.Mech.GaitGeometry.From(Repo.Load<Game.Physics.Body.Contracts.MechFrameData>("Mech/mech_frame_balanced.json"));
            var gait = new ReferenceGait(geo, t, 9.81);
            Assert.Throws<System.InvalidOperationException>(() => gait.Configure(3.0, 0));
        }
    }
}
