using System;
using System.IO;
using System.Numerics;
using System.Text;

namespace Game.AI.Policy
{
    /// <summary>
    /// Allocation-free MLP actor evaluator (R-028 section 8 / T-041b step 2). Weights come from a flat "KPW1" file written by
    /// <c>kaiju_training/export.py</c>: magic "KPW1", int32 layout version, int32 id length + UTF-8 network id, int32 layer count,
    /// then per layer int32 in, int32 out, float32 W[out*in] (row-major, out-major), float32 b[out]. All layers but the last are
    /// followed by tanh; the last layer is the head (logits: targets, maneuvers, speeds). Sampling is seeded (SplitMix64) and
    /// masked like the trainer: target first, then the maneuver masked by <see cref="PolicyLayout.ManeuverAllowed"/>, then speed.
    /// </summary>
    public sealed class MlpPolicyNetwork : IPolicyNetwork
    {
        private readonly int[] _in, _out;
        private readonly float[][] _w, _b, _act;
        private readonly float[] _logits = new float[PolicyLayout.MaskDim];
        private readonly float[] _p = new float[PolicyLayout.NTargetChoices];
        private readonly string _id;
        private ulong _s;
        public bool Greedy;

        public string NetworkId => _id;
        public int Macs { get; }
        public ReadOnlySpan<float> LastLogits => _logits;

        public MlpPolicyNetwork(string id, int[] sizes, float[][] weights, float[][] biases, bool greedy = false)
        {
            if (sizes == null || sizes.Length < 2 || sizes[0] != PolicyLayout.ObsDim || sizes[sizes.Length - 1] != PolicyLayout.MaskDim)
                throw new ArgumentException("MLP must map ObsDim -> MaskDim");
            int n = sizes.Length - 1;
            _id = id; Greedy = greedy;
            _in = new int[n]; _out = new int[n]; _w = weights; _b = biases; _act = new float[n][];
            int macs = 0;
            for (int l = 0; l < n; l++)
            {
                _in[l] = sizes[l]; _out[l] = sizes[l + 1];
                if (weights[l].Length != _in[l] * _out[l] || biases[l].Length != _out[l]) throw new ArgumentException("layer " + l + " shape mismatch");
                _act[l] = new float[_out[l]];
                macs += _in[l] * _out[l];
            }
            Macs = macs;
        }

        public static MlpPolicyNetwork Load(string path, bool greedy = false)
        {
            using (var fs = File.OpenRead(path)) return Load(fs, greedy);
        }

        public static MlpPolicyNetwork Load(Stream s, bool greedy = false)
        {
            using (var r = new BinaryReader(s, Encoding.UTF8, true))
            {
                if (r.ReadByte() != 'K' || r.ReadByte() != 'P' || r.ReadByte() != 'W' || r.ReadByte() != '1') throw new InvalidDataException("bad KPW1 magic");
                int ver = r.ReadInt32();
                if (ver != PolicyLayout.LayoutVersion) throw new InvalidDataException("layout version " + ver + " != " + PolicyLayout.LayoutVersion);
                int idLen = r.ReadInt32();
                string id = Encoding.UTF8.GetString(r.ReadBytes(idLen));
                int n = r.ReadInt32();
                var sizes = new int[n + 1]; var w = new float[n][]; var b = new float[n][];
                for (int l = 0; l < n; l++)
                {
                    int i = r.ReadInt32(), o = r.ReadInt32();
                    if (l == 0) sizes[0] = i; else if (sizes[l] != i) throw new InvalidDataException("layer chain mismatch");
                    sizes[l + 1] = o;
                    w[l] = new float[i * o]; b[l] = new float[o];
                    for (int k = 0; k < w[l].Length; k++) w[l][k] = r.ReadSingle();
                    for (int k = 0; k < o; k++) b[l][k] = r.ReadSingle();
                }
                return new MlpPolicyNetwork(id, sizes, w, b, greedy);
            }
        }

        public void Reset(ulong seed) { _s = seed * 0x9E3779B97F4A7C15UL + 0x2545F4914F6CDD1DUL; }

        /// <summary>Raw (unmasked) logits for an observation: targets [0..9), maneuvers [9..16), speeds [16..19).</summary>
        public void Forward(float[] obs, float[] logits)
        {
            float[] x = obs; int xl = _in[0];
            int n = _in.Length;
            for (int l = 0; l < n; l++)
            {
                float[] y = l == n - 1 ? logits : _act[l];
                int ni = _in[l], no = _out[l]; float[] w = _w[l], b = _b[l];
                int vs = Vector<float>.Count;
                for (int o = 0; o < no; o++)
                {
                    int row = o * ni, i = 0;
                    var acc = Vector<float>.Zero;
                    for (; i <= ni - vs; i += vs) acc += new Vector<float>(w, row + i) * new Vector<float>(x, i);
                    float sum = b[o] + Vector.Sum(acc);
                    for (; i < ni; i++) sum += w[row + i] * x[i];
                    y[o] = l == n - 1 ? sum : MathF.Tanh(sum);
                }
                x = y; xl = no;
            }
        }

        private ulong Next()
        {
            ulong z = (_s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private float U01() => (Next() >> 40) * (1f / 16777216f);

        /// <summary>Pick from logits[off..off+n) among allowed(i); greedy argmax or softmax sample. Returns 0 if nothing is allowed.</summary>
        private int Pick(int off, int n, byte[] mask, int mode, int target)
        {
            float max = float.NegativeInfinity; int best = -1;
            for (int i = 0; i < n; i++)
            {
                if (!Allowed(mode, i, off, mask, target)) continue;
                float v = _logits[off + i];
                if (v > max) { max = v; best = i; }
            }
            if (best < 0) return 0;
            if (Greedy) return best;
            float tot = 0f;
            for (int i = 0; i < n; i++)
            {
                float e = Allowed(mode, i, off, mask, target) ? MathF.Exp(_logits[off + i] - max) : 0f;
                _p[i] = e; tot += e;
            }
            float u = U01() * tot, c = 0f; int last = best;
            for (int i = 0; i < n; i++)
            {
                if (_p[i] <= 0f) continue;
                last = i; c += _p[i];
                if (u < c) return i;
            }
            return last;
        }

        private static bool Allowed(int mode, int i, int off, byte[] mask, int target)
        {
            if (mode == 1) return PolicyLayout.ManeuverAllowed(target, (Maneuver)i, mask);
            return mask[off + i] != 0;
        }

        public void Act(float[] obs, byte[] mask, ref PolicyAction action)
        {
            Forward(obs, _logits);
            action.Target = Pick(PolicyLayout.MaskTargetOffset, PolicyLayout.NTargetChoices, mask, 0, 0);
            action.Maneuver = Pick(PolicyLayout.MaskManeuverOffset, PolicyLayout.NManeuvers, mask, 1, action.Target);
            action.Speed = Pick(PolicyLayout.MaskSpeedOffset, PolicyLayout.NSpeeds, mask, 2, 0);
        }
    }
}
