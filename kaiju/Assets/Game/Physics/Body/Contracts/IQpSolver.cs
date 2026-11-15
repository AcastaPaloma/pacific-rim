namespace Game.Physics.Body.Qp
{
    public enum QpStatus { Optimal = 0, Infeasible = 1, IterationLimit = 2, NotPositiveDefinite = 3 }

    /// <summary>
    /// Dense QP: minimise 0.5 x'Hx + g'x  s.t.  Aeq x = beq,  Ain x >= bin. Matrices row-major flat double[]; no allocation per call.
    /// Implemented by <c>GoldfarbIdnaniQp</c>; obtained through <c>IBodyWorld.CreateQpSolver</c>.
    /// </summary>
    public interface IQpSolver
    {
        double[] X { get; }
        double[] Lambda { get; }
        QpStatus Status { get; }
        int Iterations { get; }
        long FailureCount { get; }
        /// <summary>Relative Tikhonov term added to H (times the mean diagonal) before factorisation; the default suits well-scaled problems, weighted tasks spanning many decades set it near 0.</summary>
        double Regularisation { get; set; }
        QpStatus Solve(int n, int meq, int min, double[] H, double[] g, double[] Aeq, double[] beq, double[] Ain, double[] bin,
                       bool hUnchanged = false, int[] warm = null, int warmCount = 0);
    }
}
