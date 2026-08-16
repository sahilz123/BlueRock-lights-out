using System.Numerics;
using BlueRockLightsOut.Model;

namespace BlueRockLightsOut.Services
{
    /// <summary>
    /// Backtracking solver. Places pieces one at a time (most-constrained piece
    /// first), incrementing/decrementing the board mod `depth` as pieces are
    /// tried and undone.
    ///
    /// Reachability bookkeeping (which cells can still be touched by unplaced
    /// pieces) and the "is the board all zero" check are both maintained
    /// incrementally via ulong bitmasks, rather than rebuilt / rescanned from
    /// scratch at every node of the search tree.
    /// </summary>
    public sealed class Solver
    {
        private readonly int _depth;
        private readonly int _rows;
        private readonly int _cols;
        private readonly int[,] _board;
        private readonly List<Piece> _pieces;
        private readonly (int X, int Y)[] _result;

        // reachCount[cell] = how many pieces in the *currently unplaced* set
        // can still reach that cell. A cell is "frozen" (unreachable by any
        // remaining piece) exactly when this hits 0.
        private readonly int[] _reachCount;

        // Bit i is set iff board cell i (row * Cols + col) is currently non-zero.
        // Board dimensions are validated (<= 64 cells) in the Piece constructor.
        private ulong _nonzeroMask;

        // Diagnostic only — counts how many times Backtrack is entered, so
        // you can see whether slowness is "algorithm issue" or "genuinely
        // huge search space" for a given instance.
        public long NodesExplored { get; private set; }

        public Solver(int depth, int rows, int cols, int[,] board, List<Piece> pieces)
        {
            _depth = depth;
            _rows = rows;
            _cols = cols;
            _board = board;
            _pieces = pieces;
            _result = new (int, int)[pieces.Count];
            _reachCount = new int[rows * cols];

            _nonzeroMask = 0UL;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (board[r, c] != 0)
                        _nonzeroMask |= 1UL << r * cols + c;
                }
            }
        }

        public bool TrySolve(out (int X, int Y)[] placements)
        {
            var unplaced = Enumerable.Range(0, _pieces.Count).ToList();

            // Seed reachCount to reflect the full unplaced set up front.
            foreach (int idx in unplaced)
                AdjustReach(_pieces[idx].ReachMask, +1);

            bool solved = Backtrack(unplaced);
            placements = _result;
            return solved;
        }

        private void AdjustReach(ulong mask, int sign)
        {
            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                _reachCount[bit] += sign;
                mask &= mask - 1; // clear lowest set bit
            }
        }

        private bool Backtrack(List<int> unplaced)
        {
            NodesExplored++;

            if (unplaced.Count == 0)
                return _nonzeroMask == 0UL;

            // Dynamic most-constrained-variable + forward checking.
            //
            // For each unplaced piece, count how many of its positions are
            // still feasible against the *current* board (not just whether
            // they geometrically fit — that was the old, weaker heuristic).
            // Two payoffs:
            //   1. Branch on whichever piece is most constrained right now,
            //      which tightens pruning as the search gets deeper.
            //   2. If any unplaced piece has ZERO feasible positions, the
            //      whole branch is already dead — bail immediately instead
            //      of wasting time on the other pieces.
            int chosen = -1;
            int bestFeasibleCount = int.MaxValue;

            // Cheapest-first ordering (by static geometric count) tends to
            // establish a tight bestFeasibleCount early, so later, more
            // "open" pieces short-circuit their counting sooner below.
            var orderedCandidates = unplaced.OrderBy(idx => _pieces[idx].Positions.Count);

            foreach (int idx in orderedCandidates)
            {
                Piece candidate = _pieces[idx];

                // Temporarily exclude this piece from reachCount so the
                // check reflects "if this piece gets placed, can the
                // remaining pieces still cover everything else" — the same
                // semantics used when actually placing a piece below.
                AdjustReach(candidate.ReachMask, -1);

                int feasibleCount = 0;
                for (int p = 0; p < candidate.Positions.Count; p++)
                {
                    if (PositionFeasible(candidate.PositionMasks[p], candidate.ReachMask))
                    {
                        feasibleCount++;

                        // Once this candidate can no longer beat the current
                        // best, its exact count doesn't matter — stop early.
                        if (feasibleCount >= bestFeasibleCount)
                            break;
                    }
                }

                AdjustReach(candidate.ReachMask, +1);

                if (feasibleCount == 0)
                    return false; // empty domain — this branch cannot succeed

                if (feasibleCount < bestFeasibleCount)
                {
                    bestFeasibleCount = feasibleCount;
                    chosen = idx;
                }
            }

            var others = unplaced.Where(i => i != chosen).ToList();
            Piece piece = _pieces[chosen];

            AdjustReach(piece.ReachMask, -1);

            bool found = false;
            for (int p = 0; p < piece.Positions.Count && !found; p++)
            {
                ulong posMask = piece.PositionMasks[p];

                // Skip known-infeasible positions without ever mutating the
                // board (no wasted Apply/Undo cycle).
                if (!PositionFeasible(posMask, piece.ReachMask))
                    continue;

                (int x, int y) = piece.Positions[p];

                Apply(posMask, +1);
                _result[chosen] = (x, y);
                if (Backtrack(others))
                    found = true;

                if (!found)
                    Apply(posMask, -1);
            }

            AdjustReach(piece.ReachMask, +1); // restore for caller / sibling branches
            return found;
        }

        // Checks, WITHOUT mutating board state, whether placing a piece at
        // the given position would keep every cell in its reach mask
        // satisfiable. Each remaining unplaced piece can contribute at most
        // +1 to a cell (if it covers it), so a cell needs
        // (depth - hypothetical value) more +1s to reach zero — and that
        // can never exceed the number of pieces still able to reach it.
        private bool PositionFeasible(ulong posMask, ulong pieceReachMask)
        {
            ulong mask = pieceReachMask;
            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;

                int r = bit / _cols;
                int c = bit % _cols;
                int cellValue = _board[r, c];

                if ((posMask & 1UL << bit) != 0)
                    cellValue = (cellValue + 1) % _depth;

                if (cellValue == 0)
                    continue;

                int needed = _depth - cellValue;
                if (needed > _reachCount[bit])
                    return false;
            }
            return true;
        }

        private void Apply(ulong posMask, int sign)
        {
            ulong mask = posMask;
            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;

                int r = bit / _cols;
                int c = bit % _cols;

                int cell = _board[r, c];
                cell = ((cell + sign) % _depth + _depth) % _depth;
                _board[r, c] = cell;

                if (cell == 0)
                    _nonzeroMask &= ~(1UL << bit);
                else
                    _nonzeroMask |= 1UL << bit;
            }
        }
    }
}