using BlueRockLightsOut.Model;
using BlueRockLightsOut.Model.BlueRockLightsOut.Model;
using System.Numerics;

namespace BlueRockLightsOut.Services
{
    /// <summary>
    /// Static, solver-independent domain reduction. For every candidate
    /// position of every piece, checks whether placing the piece there
    /// could EVER be part of any solution — using the same "needed
    /// increments can't exceed remaining reachable pieces" logic the
    /// backtracking solver uses dynamically, but applied once upfront as a
    /// fixpoint iteration (classic arc-consistency style preprocessing).
    ///
    /// Positions proven infeasible are removed from consideration entirely,
    /// which can cascade (removing one piece's bad position shrinks its
    /// reach, which can make another piece's positions newly infeasible
    /// too) — hence the loop-to-fixpoint.
    ///
    /// Feeding the survivors into CP-SAT means fewer BoolVars, fewer terms
    /// per constraint, and a tighter starting point before CP-SAT's own
    /// search/propagation even begins.
    /// </summary>
    public static class DomainPreprocessor
    {
        /// <summary>
        /// Returns alive[i][k] = true iff piece i's k-th position
        /// (Pieces[i].Positions[k] / PositionMasks[k]) survives pruning.
        /// Returns false overall if any piece ends up with zero alive
        /// positions (the puzzle is provably unsolvable).
        /// </summary>
        public static bool Prune(PuzzleInput input, out bool[][] aliveOut)
        {
            int rows = input.Rows, cols = input.Cols, depth = input.Depth;
            var pieces = input.Pieces;
            int n = pieces.Count;

            // Local functions below close over this variable; `out`
            // parameters can't be captured directly, so `alive` is a plain
            // local and gets assigned to `aliveOut` right before returning.
            var alive = new bool[n][];
            for (int i = 0; i < n; i++)
                alive[i] = Enumerable.Repeat(true, pieces[i].Positions.Count).ToArray();

            var reachCount = new int[rows * cols];

            void RecomputeReachCount()
            {
                Array.Clear(reachCount, 0, reachCount.Length);
                for (int i = 0; i < n; i++)
                {
                    ulong agg = AggregateAliveReach(i);
                    ulong m = agg;
                    while (m != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(m);
                        m &= m - 1;
                        reachCount[bit]++;
                    }
                }
            }

            ulong AggregateAliveReach(int pieceIdx)
            {
                ulong agg = 0UL;
                var masks = pieces[pieceIdx].PositionMasks;
                for (int k = 0; k < alive[pieceIdx].Length; k++)
                    if (alive[pieceIdx][k])
                        agg |= masks[k];
                return agg;
            }

            bool changed = true;
            int iterations = 0;

            while (changed)
            {
                changed = false;
                iterations++;
                RecomputeReachCount();

                for (int i = 0; i < n; i++)
                {
                    ulong pieceAggReach = AggregateAliveReach(i);
                    var masks = pieces[i].PositionMasks;

                    for (int k = 0; k < alive[i].Length; k++)
                    {
                        if (!alive[i][k])
                            continue;

                        ulong posMask = masks[k];
                        bool feasible = true;

                        ulong mask = pieceAggReach;
                        while (mask != 0)
                        {
                            int bit = BitOperations.TrailingZeroCount(mask);
                            mask &= mask - 1;

                            int r = bit / cols, c = bit % cols;
                            int cellValue = input.Board[r, c];
                            if ((posMask & 1UL << bit) != 0)
                                cellValue = (cellValue + 1) % depth;

                            if (cellValue == 0)
                                continue;

                            int needed = depth - cellValue;
                            int available = reachCount[bit] - 1; // exclude piece i itself
                            if (needed > available)
                            {
                                feasible = false;
                                break;
                            }
                        }

                        if (!feasible)
                        {
                            alive[i][k] = false;
                            changed = true;
                        }
                    }
                }
            }

            bool anyDeadPiece = false;
            int totalRemoved = 0, totalPositions = 0;
            for (int i = 0; i < n; i++)
            {
                int removed = alive[i].Count(a => !a);
                totalRemoved += removed;
                totalPositions += alive[i].Length;

                if (removed > 0)
                    Console.WriteLine($"Piece {i}: removed {removed}/{alive[i].Length} positions as provably infeasible.");

                if (alive[i].All(a => !a))
                {
                    Console.WriteLine($"Piece {i} has ZERO surviving positions — puzzle is unsolvable.");
                    anyDeadPiece = true;
                }
            }

            Console.WriteLine(
                $"Domain preprocessing: {iterations} iteration(s), removed {totalRemoved}/{totalPositions} " +
                $"candidate positions ({(totalPositions == 0 ? 0 : 100.0 * totalRemoved / totalPositions):F1}%).");

            aliveOut = alive;
            return !anyDeadPiece;
        }
    }
}