using BlueRockLightsOut.Model;

namespace BlueRockLightsOut
{
/// <summary>
/// Backtracking solver. Places pieces one at a time (most-constrained piece
/// first), incrementing/decrementing the board mod `depth` as pieces are
/// tried and undone. After each placement it checks every board cell that
/// no remaining piece could ever reach again ("frozen" cells) is already 0,
/// which prunes hopeless branches early instead of only checking at the end.
/// </summary>
public sealed class Solver
    {
        private readonly int _depth;
        private readonly int _rows;
        private readonly int _cols;
        private readonly int[,] _board;
        private readonly List<Piece> _pieces;
        private readonly (int X, int Y)[] _result;

        public Solver(int depth, int rows, int cols, int[,] board, List<Piece> pieces)
        {
            _depth = depth;
            _rows = rows;
            _cols = cols;
            _board = board;
            _pieces = pieces;
            _result = new (int, int)[pieces.Count];
        }

        public bool TrySolve(out (int X, int Y)[] placements)
        {
            var unplaced = Enumerable.Range(0, _pieces.Count).ToList();
            bool solved = Backtrack(unplaced);
            placements = _result;
            return solved;
        }

        private bool Backtrack(List<int> unplaced)
        {
            if (unplaced.Count == 0)
            {
                // Every cell should already be 0 thanks to the frozen-cell check
                // below, but verify explicitly for safety.
                for (int r = 0; r < _rows; r++)
                    for (int c = 0; c < _cols; c++)
                        if (_board[r, c] != 0)
                            return false;
                return true;
            }

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

            // Cells that none of the still-unplaced pieces (after this one) can
            // ever touch again. If any such cell isn't 0 once we place `chosen`,
            // this branch can never reach an all-zero board.
            var unionReach = new HashSet<(int R, int C)>();
            foreach (int idx in others)
                unionReach.UnionWith(_pieces[idx].ReachCells);

            Piece piece = _pieces[chosen];
            foreach ((int x, int y) in piece.Positions)
            {
                Apply(piece, x, y, +1);

                if (FrozenCellsAreZero(unionReach))
                {
                    _result[chosen] = (x, y);
                    if (Backtrack(others))
                        return true;
                }

                Apply(piece, x, y, -1);
            }

            return false;
        }

        private bool FrozenCellsAreZero(HashSet<(int R, int C)> unionReach)
        {
            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    if (_board[r, c] != 0 && !unionReach.Contains((r, c)))
                        return false;
                }
            }
            return true;
        }

        private void Apply(Piece piece, int x, int y, int sign)
        {
            for (int r = 0; r < piece.Height; r++)
            {
                for (int c = 0; c < piece.Width; c++)
                {
                    if (!piece.Mask[r, c])
                        continue;

                    int cell = _board[y + r, x + c];
                    cell = ((cell + sign) % _depth + _depth) % _depth;
                    _board[y + r, x + c] = cell;
                }
            }
        }
    }

}
