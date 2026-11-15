using System; using System.Diagnostics; using System.Numerics;
static class P {
  static void Scalar(float[] W, float[] b, float[] x, float[] y, int nin, int nout, bool act) {
    for (int o = 0; o < nout; o++) { float s0 = b[o], s1 = 0, s2 = 0, s3 = 0; int r = o * nin, i = 0;
      for (; i + 3 < nin; i += 4) { s0 += W[r+i]*x[i]; s1 += W[r+i+1]*x[i+1]; s2 += W[r+i+2]*x[i+2]; s3 += W[r+i+3]*x[i+3]; }
      for (; i < nin; i++) s0 += W[r+i]*x[i]; float s = s0+s1+s2+s3; y[o] = act ? MathF.Tanh(s) : s; } }
  static void Simd(float[] W, float[] b, float[] x, float[] y, int nin, int nout, bool act) {
    int V = Vector<float>.Count; var xs = x.AsSpan(0, nin);
    for (int o = 0; o < nout; o++) { var ws = W.AsSpan(o * nin, nin); var acc = Vector<float>.Zero; int i = 0;
      for (; i + V <= nin; i += V) acc += new Vector<float>(ws.Slice(i)) * new Vector<float>(xs.Slice(i));
      float s = b[o] + Vector.Dot(acc, Vector<float>.One); for (; i < nin; i++) s += ws[i]*xs[i]; y[o] = act ? MathF.Tanh(s) : s; } }
  static void Main() {
    int[] L = {313, 128, 128, 19}; var rnd = new Random(1);
    var W = new float[3][]; var B = new float[3][]; var A = new float[4][];
    for (int l = 0; l < 4; l++) A[l] = new float[L[l]];
    for (int l = 0; l < 3; l++) { W[l] = new float[L[l]*L[l+1]]; B[l] = new float[L[l+1]]; for (int i = 0; i < W[l].Length; i++) W[l][i] = (float)(rnd.NextDouble()-0.5)*0.1f; }
    for (int i = 0; i < 313; i++) A[0][i] = (float)rnd.NextDouble();
    foreach (var simd in new[]{false,true}) {
      var t = new long[20000];
      for (int it = -2000; it < t.Length; it++) { long t0 = Stopwatch.GetTimestamp();
        for (int l = 0; l < 3; l++) { if (simd) Simd(W[l],B[l],A[l],A[l+1],L[l],L[l+1],l<2); else Scalar(W[l],B[l],A[l],A[l+1],L[l],L[l+1],l<2); }
        if (it >= 0) t[it] = Stopwatch.GetTimestamp()-t0; }
      Array.Sort(t); double us = 1e6/Stopwatch.Frequency;
      Console.WriteLine($"{(simd?"simd":"scalar")} V={Vector<float>.Count} p50={t[t.Length/2]*us:F2}us p99={t[t.Length*99/100]*us:F2}us"); } } }
