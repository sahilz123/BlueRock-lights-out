using BlueRockLightsOut.Model;
using BlueRockLightsOut.Model.BlueRockLightsOut.Model;
using Google.OrTools.Sat;

namespace BlueRockLightsOut.Services
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

            // Symmetry breaking: pieces with the IDENTICAL shape produce
            // identical Positions/PositionMasks lists (same generation
            // logic, same board), so they're fully interchangeable — one
            // "swapped" solution is just as valid as another. Left
            // unconstrained, the solver wastes time re-exploring these
            // redundant permutations. For each group of duplicate-shape
            // pieces, force their chosen position indices into non-
            // decreasing order (i < j => index_i <= index_j), which
            // eliminates the redundant swaps from the search space.
            var shapeGroups = new Dictionary<string, List<int>>();
            for (int i = 0; i < n; i++)
            {
                string key = ShapeKey(pieces[i]);
                if (!shapeGroups.TryGetValue(key, out var list))
                    shapeGroups[key] = list = new List<int>();
                list.Add(i);
            }

            foreach (var group in shapeGroups.Values)
            {
                if (group.Count < 2)
                    continue; // no duplicates, nothing to break

                // index_i = weighted sum of k * (is piece i at position k),
                // i.e. an IntVar representing "which position index piece i
                // ended up at" (valid since exactly one position-var is 1).
                var indexVars = new IntVar[group.Count];
                for (int g = 0; g < group.Count; g++)
                {
                    int i = group[g];
                    int maxIdx = pieces[i].Positions.Count - 1;
                    var idx = model.NewIntVar(0, maxIdx, $"idx_piece{i}");

                    var terms = new List<LinearExpr>();
                    for (int k = 0; k < pieceVars[i].Length; k++)
                        if (pieceVars[i][k] is not null)
                            terms.Add(k * pieceVars[i][k]);

                    model.Add(idx == LinearExpr.Sum(terms));
                    indexVars[g] = idx;
                }

                for (int g = 0; g + 1 < indexVars.Length; g++)
                    model.Add(indexVars[g] <= indexVars[g + 1]);
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
                            if (pieceVars[i][k] is not null && (masks[k] & 1UL << bit) != 0)
                                covering.Add(pieceVars[i][k]);
                        }
                    }

                    // total = initial value + (however many covering pieces
                    // end up placed here). Loose upper bound on q: total can
                    // be at most initialValue + covering.Count.
                    int initialValue = input.Board[r, c];
                    int maxTotal = initialValue + covering.Count;
                    int maxQ = maxTotal / depth + 1;

                    IntVar q = model.NewIntVar(0, maxQ, $"q_{r}_{c}");

                    // sum(covering) + initialValue == depth * q
                    // i.e. total is an exact multiple of depth (== 0 mod depth).
                    model.Add(LinearExpr.Sum(covering) + initialValue == depth * q);
                }
            }

            var solver = new CpSolver();
            int workers = Math.Max(1, Environment.ProcessorCount);
            solver.StringParameters =
                $"max_time_in_seconds:{maxTimeInSeconds} " +
                $"num_search_workers:{workers} " +
                "log_search_progress:true " +
                "linearization_level:2 " +
                "cp_model_probing_level:2 " +
                "symmetry_level:2";

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

        // Produces an identical string for two pieces iff their masks are
        // pixel-for-pixel identical (same dimensions, same X/. pattern) —
        // used to group fully-interchangeable pieces for symmetry breaking.
        private static string ShapeKey(Piece piece)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(piece.Height).Append('x').Append(piece.Width).Append(':');
            for (int r = 0; r < piece.Height; r++)
                for (int c = 0; c < piece.Width; c++)
                    sb.Append(piece.Mask[r, c] ? 'X' : '.');
            return sb.ToString();
        }
    }
}