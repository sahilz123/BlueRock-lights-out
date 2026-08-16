using BlueRockLightsOut.Model;
using BlueRockLightsOut.Model.BlueRockLightsOut.Model;
using Google.OrTools.Sat;

namespace BlueRockLightsOut
{
    /// <summary>
    /// Constraint-programming solver using Google OR-Tools CP-SAT.
    ///
    /// Model:
    ///   - One BoolVar per (piece, candidate position). Exactly one of a
    ///     piece's position-vars must be true ("exactly-one" constraint) —
    ///     this is the "every piece placed exactly once" rule.
    ///   - For every board cell, sum up the initial value plus every
    ///     position-var that covers that cell. That total must be an exact
    ///     multiple of `depth` (i.e. total mod depth == 0), expressed as
    ///     total == depth * q for an auxiliary integer q.
    ///
    /// CP-SAT does global constraint propagation across every piece and
    /// every cell simultaneously, which is why it scales far better than
    /// depth-first backtracking on harder instances (more pieces, higher
    /// depth) — at the cost of being a black box you don't control the
    /// internals of.
    /// </summary>
    public static class CpSatSolver
    {
        public static bool TrySolve(
            PuzzleInput input,
            out (int X, int Y)[] placements,
            int maxTimeInSeconds = 60)
        {
            var model = new CpModel();
            int rows = input.Rows;
            int cols = input.Cols;
            int depth = input.Depth;
            var pieces = input.Pieces;
            int n = pieces.Count;

            // Static domain reduction pass — remove provably-infeasible
            // positions before CP-SAT ever sees them. Cheap (milliseconds)
            // and shrinks the model CP-SAT has to search over.
            if (!DomainPreprocessor.Prune(input, out bool[][] alive))
            {
                Console.WriteLine("Preprocessing proved the puzzle unsolvable — skipping CP-SAT entirely.");
                placements = new (int, int)[n];
                return false;
            }

            // pieceVars[i][k] = true iff piece i is placed at its k-th
            // candidate position. Only created for positions that survived
            // preprocessing; null for pruned-out positions.
            var pieceVars = new BoolVar[n][];
            for (int i = 0; i < n; i++)
            {
                var positions = pieces[i].Positions;
                var vars = new BoolVar[positions.Count];
                var liveVars = new List<BoolVar>();

                for (int k = 0; k < positions.Count; k++)
                {
                    if (!alive[i][k])
                        continue; // leave vars[k] null — pruned, never assignable

                    vars[k] = model.NewBoolVar($"p{i}_pos{k}");
                    liveVars.Add(vars[k]);
                }

                pieceVars[i] = vars;

                // Every piece must be placed at exactly one of its
                // surviving positions.
                model.AddExactlyOne(liveVars);
            }

            // Per-cell modular sum constraint.
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int bit = r * cols + c;

                    var covering = new List<BoolVar>();
                    for (int i = 0; i < n; i++)
                    {
                        var masks = pieces[i].PositionMasks;
                        for (int k = 0; k < masks.Count; k++)
                        {
                            if (pieceVars[i][k] is not null && (masks[k] & (1UL << bit)) != 0)
                                covering.Add(pieceVars[i][k]);
                        }
                    }

                    // total = initial value + (however many covering pieces
                    // end up placed here). Loose upper bound on q: total can
                    // be at most initialValue + covering.Count.
                    int initialValue = input.Board[r, c];
                    int maxTotal = initialValue + covering.Count;
                    int maxQ = (maxTotal / depth) + 1;

                    IntVar q = model.NewIntVar(0, maxQ, $"q_{r}_{c}");

                    // sum(covering) + initialValue == depth * q
                    // i.e. total is an exact multiple of depth (== 0 mod depth).
                    model.Add(LinearExpr.Sum(covering) + initialValue == depth * q);
                }
            }

            var solver = new CpSolver();
            solver.StringParameters =
                $"max_time_in_seconds:{maxTimeInSeconds} " +
                "num_search_workers:8 " +          // parallel portfolio search — use your machine's core count
                "log_search_progress:true " +
                "linearization_level:2 " +          // more aggressive LP relaxation, often helps on assignment-style models
                "cp_model_probing_level:2";          // more presolve probing to tighten constraints upfront

            CpSolverStatus status = solver.Solve(model);
            Console.WriteLine($"CP-SAT status: {status}");
            placements = new (int, int)[n];

            if (status == CpSolverStatus.Infeasible)
            {
                Console.WriteLine("PROVEN infeasible — no assignment of pieces can ever zero this board.");
                return false;
            }

            if (status == CpSolverStatus.Unknown)
            {
                Console.WriteLine("Timed out with no verdict either way — increase maxTimeInSeconds and retry.");
                return false;
            }

            if (status != CpSolverStatus.Optimal && status != CpSolverStatus.Feasible)
                return false;

            for (int i = 0; i < n; i++)
            {
                var positions = pieces[i].Positions;
                for (int k = 0; k < positions.Count; k++)
                {
                    if (pieceVars[i][k] is not null && solver.Value(pieceVars[i][k]) == 1)
                    {
                        placements[i] = positions[k];
                        break;
                    }
                }
            }

            return true;
        }
    }
}