using System;
using System.Numerics;
using Game.Control.Contracts;

namespace Game.Net.ControlBridge
{
    /// <summary>
    /// PilotInput &lt;-&gt; IntentPayload (16 bytes). Layout: Lo = bits(target.x) | bits(target.y) &lt;&lt; 32;
    /// Hi = bits(target.z) | effort16 &lt;&lt; 32 | move8 &lt;&lt; 48 | flags8 &lt;&lt; 56 (bit 0 of flags = valid).
    /// target is bit-exact (NaN payloads and signed zero included). move keeps its low 8 bits (MoveId is append-only, well under 256).
    /// effort is quantised to 16 bits over [-1, 3] (step 4/65535): error &lt;= 3.1e-5 (EffortMaxError adds float slack) for any value in [-1, 3]; values outside
    /// saturate at -1 / 3 (so "above 1, clamp logged downstream" still reads as above 1); NaN encodes as 0.
    /// An invalid input encodes as the all-zero payload, which is also Net's neutral payload, so neutral decodes to valid = false.
    /// </summary>
    public static class IntentCodec
    {
        public const float EffortMin = -1f, EffortMax = 3f;
        public const float EffortMaxError = (EffortMax - EffortMin) / 65535f / 2f + 2e-6f;
        private const int Levels = 65535;

        public static IntentPayload Encode(in PilotInput p)
        {
            if (!p.valid) return default(IntentPayload);
            ulong lo = Bits(p.target.X) | (Bits(p.target.Y) << 32);
            ulong hi = Bits(p.target.Z) | ((ulong)QuantiseEffort(p.effort) << 32) | ((ulong)(byte)(int)p.move << 48) | (1UL << 56);
            return new IntentPayload(lo, hi);
        }

        public static PilotInput Decode(in IntentPayload pl)
        {
            if (((pl.Hi >> 56) & 1UL) == 0) return default(PilotInput);
            return new PilotInput
            {
                move = (MoveId)(byte)(pl.Hi >> 48),
                target = new Vector3(Float((uint)pl.Lo), Float((uint)(pl.Lo >> 32)), Float((uint)pl.Hi)),
                effort = DequantiseEffort((ushort)(pl.Hi >> 32)),
                valid = true,
            };
        }

        public static ushort QuantiseEffort(float e)
        {
            if (float.IsNaN(e)) e = 0f;
            if (e < EffortMin) e = EffortMin; else if (e > EffortMax) e = EffortMax;
            return (ushort)(int)Math.Round((e - EffortMin) / (EffortMax - EffortMin) * Levels);
        }

        public static float DequantiseEffort(ushort q) => EffortMin + q * ((EffortMax - EffortMin) / Levels);

        private static ulong Bits(float f) => (uint)BitConverter.SingleToInt32Bits(f);
        private static float Float(uint u) => BitConverter.Int32BitsToSingle((int)u);
    }
}
