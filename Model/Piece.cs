namespace BlueRockLightsOut.Model
{
    public sealed class Piece
    {
        public readonly bool[,] Mask;
        public readonly int Height;
        public readonly int Width;

        // Every valid top-left (x, y) placement of this piece on the board.
        public readonly List<(int X, int Y)> Positions = new();

        // PositionMasks[i] is the bitmask of cells covered when the piece is
        // placed at Positions[i]. Bit index = row * boardCols + col.
        // Parallel array to Positions (same index = same placement).
        public readonly List<ulong> PositionMasks = new();

        // Union of every PositionMask across all valid placements: every
        // cell this piece could possibly ever cover, from any position.
        public readonly ulong ReachMask;

        public Piece(bool[,] mask, int height, int width, int boardRows, int boardCols)
        {
            if (boardRows * boardCols > 64)
                throw new NotSupportedException(
                    $"Board has {boardRows * boardCols} cells; the ulong-bitmask optimization " +
                    "supports at most 64. For larger boards, switch ReachMask/PositionMasks to " +
                    "ulong[] (multi-word) or System.Numerics.BigInteger.");

            Mask = mask;
            Height = height;
            Width = width;

            ulong reach = 0UL;
            for (int y = 0; y + height <= boardRows; y++)
            {
                for (int x = 0; x + width <= boardCols; x++)
                {
                    ulong posMask = 0UL;
                    for (int r = 0; r < height; r++)
                    {
                        for (int c = 0; c < width; c++)
                        {
                            if (mask[r, c])
                            {
                                int bit = (y + r) * boardCols + (x + c);
                                posMask |= 1UL << bit;
                            }
                        }
                    }

                    Positions.Add((x, y));
                    PositionMasks.Add(posMask);
                    reach |= posMask;
                }
            }

            ReachMask = reach;
        }
    }
}
