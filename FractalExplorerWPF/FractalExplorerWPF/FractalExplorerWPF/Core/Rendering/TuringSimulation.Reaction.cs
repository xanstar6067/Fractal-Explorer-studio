using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering;

public sealed partial class TuringSimulation
{
    private TuringReactionModel _reactionModel;
    private float[] _u = [], _v = [];
    private float[] _reactionA = [], _reactionB = [], _reactionC = [], _reactionD = [];

    private void InitializeReaction(TuringState state)
    {
        _reactionModel = state.Reaction.Model;
        _u = new float[Size * Size]; _v = new float[_u.Length];
        var random = new Random(state.RandomSeed);
        var equilibrium = state.Reaction.Equilibrium;
        for (int i = 0; i < _u.Length; i++)
        {
            _u[i] = (float)(equilibrium.U * (1 + .05 * (random.NextDouble() * 2 - 1)));
            _v[i] = (float)(equilibrium.V * (1 + .05 * (random.NextDouble() * 2 - 1)));
        }
        Symmetrize(_u, _symmetric, state, CancellationToken.None); _symmetric.CopyTo(_u, 0);
        Symmetrize(_v, _symmetric, state, CancellationToken.None); _symmetric.CopyTo(_v, 0);
        for (int i = 0; i < _u.Length; i++) _field[i] = TuringReactionKinetics.Display(_u[i], equilibrium.U);
    }

    private void AdvanceReaction(int steps, TuringState state, CancellationToken token)
    {
        if (_reactionA.Length == 0)
        {
            _reactionA = new float[_u.Length]; _reactionB = new float[_u.Length];
            _reactionC = new float[_u.Length]; _reactionD = new float[_u.Length];
        }
        var integration = state.Reaction.Integration(Size, 256, state.DetailSize, state.EvolutionRate, 2);
        double equilibriumU = state.Reaction.Equilibrium.U;
        double du = state.Reaction.DiffusionU * integration.DiffusionScale, dv = state.Reaction.DiffusionV * integration.DiffusionScale;
        for (int step = 0; step < steps; step++)
        {
            float[] u = _u, v = _v, nu = _reactionA, nv = _reactionB;
            for (int sub = 0; sub < integration.Count; sub++)
            {
                token.ThrowIfCancellationRequested();
                for (int y = 0; y < Size; y++)
                {
                    if ((y & 15) == 0) token.ThrowIfCancellationRequested();
                    int above = Edge(y - 1, Size, state.Boundary) * Size, below = Edge(y + 1, Size, state.Boundary) * Size;
                    for (int x = 0; x < Size; x++)
                    {
                        int i = y * Size + x, left = y * Size + Edge(x - 1, Size, state.Boundary), right = y * Size + Edge(x + 1, Size, state.Boundary);
                        double lu = (double)u[left] + u[right] + u[above + x] + u[below + x] - 4 * u[i];
                        double lv = (double)v[left] + v[right] + v[above + x] + v[below + x] - 4 * v[i];
                        var reaction = TuringReactionKinetics.Evaluate(u[i], v[i], state.Reaction);
                        double nextU = u[i] + integration.Dt * (du * lu + reaction.U), nextV = v[i] + integration.Dt * (dv * lv + reaction.V);
                        if (!double.IsFinite(nextU + nextV) || nextU > 1000 || nextV > 1000)
                            throw new InvalidOperationException("Реакция расходится. Уменьшите A/B или измените диффузию и начните заново.");
                        nu[i] = (float)Math.Max(TuringReactionKinetics.MinConcentration, nextU);
                        nv[i] = (float)Math.Max(TuringReactionKinetics.MinConcentration, nextV);
                    }
                }
                u = nu; v = nv;
                nu = ReferenceEquals(u, _reactionA) ? _reactionC : _reactionA;
                nv = ReferenceEquals(v, _reactionB) ? _reactionD : _reactionB;
            }
            Symmetrize(u, nu, state, token); Symmetrize(v, nv, state, token);
            for (int i = 0; i < _field.Length; i++) _next[i] = TuringReactionKinetics.Display(nu[i], equilibriumU);
            token.ThrowIfCancellationRequested();
            // The committed concentrations remain untouched until the entire displayed step completes.
            float[] oldU = _u, oldV = _v;
            (_u, _v) = (nu, nv);
            if (ReferenceEquals(nu, _reactionA)) { _reactionA = oldU; _reactionB = oldV; }
            else { _reactionC = oldU; _reactionD = oldV; }
            (_field, _next) = (_next, _field); StepCount++;
        }
    }
}
