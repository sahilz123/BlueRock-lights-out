namespace BlueRockLightsOut.Model
{
    public sealed class Piece
    {
        public readonly bool[,] Mask;
        public readonly int Height;
        public readonly int Width;
        public readonly List<(int X, int Y)> Positions = new();
        public readonly HashSet<(int R, int C)> ReachCells = new();

        public Piece(bool[,] mask, int height, int width, int boardRows, int boardCols)
        {
            Mask = mask;
            Height = height;
            Width = width;

            for (int y = 0; y + height <= boardRows; y++)
            {
                for (int x = 0; x + width <= boardCols; x++)
                {
                    Positions.Add((x, y));
                    for (int r = 0; r < height; r++)
                    {
                        for (int c = 0; c < width; c++)
                        {
                            if (mask[r, c])
                                ReachCells.Add((y + r, x + c));
                        }
                    }
                }
            }
        }
    }
}
