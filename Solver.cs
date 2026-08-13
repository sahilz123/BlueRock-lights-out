using System.Numerics;
using BlueRockLightsOut.Model;

namespace BlueRockLightsOut
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
                        _nonzeroMask |= 1UL << (r * cols + c);
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

            // Most-constrained-variable heuristic: place the piece with the
            // fewest legal positions next, to prune the search tree faster.
            int chosen = unplaced[0];
            int bestCount = _pieces[chosen].Positions.Count;
            foreach (int idx in unplaced)
            {
                if (_pieces[idx].Positions.Count < bestCount)
                {
                    chosen = idx;
                    bestCount = _pieces[idx].Positions.Count;
                }
            }

            var others = unplaced.Where(i => i != chosen).ToList();
            Piece piece = _pieces[chosen];

            // Remove `chosen` from the unplaced set so reachCount reflects
            // `others` for the duration of this piece's placement attempts.
            AdjustReach(piece.ReachMask, -1);

            bool found = false;
            for (int p = 0; p < piece.Positions.Count && !found; p++)
            {
                (int x, int y) = piece.Positions[p];
                ulong posMask = piece.PositionMasks[p];

                Apply(posMask, +1);

                // Only cells `piece` could reach need checking here — any
                // other frozen cell was already validated by an earlier
                // recursive step and can't have changed.
                if (FrozenCellsOk(piece.ReachMask))
                {
                    _result[chosen] = (x, y);
                    if (Backtrack(others))
                        found = true;
                }

                if (!found)
                    Apply(posMask, -1);
            }

            AdjustReach(piece.ReachMask, +1); // restore for caller / sibling branches
            return found;
        }

        // Generalized version of the old "frozen cell" check. Each remaining
        // unplaced piece can contribute at most +1 to a cell (if it covers
        // it). So a cell needs (depth - board[cell]) more +1s to reach zero
        // (or 0 more if it's already zero) — and that requirement can never
        // exceed the number of pieces still able to reach it. If it does,
        // this branch is already dead, regardless of how those pieces get
        // placed. This subsumes the old reachCount == 0 check (that's just
        // the case where needed > 0 = reachCount).
        private bool FrozenCellsOk(ulong pieceReachMask)
        {
            ulong mask = pieceReachMask;
            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;

                int r = bit / _cols;
                int c = bit % _cols;
                int cellValue = _board[r, c];

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