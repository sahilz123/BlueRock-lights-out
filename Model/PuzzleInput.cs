namespace BlueRockLightsOut.Model
{
    using System.IO.Pipelines;
    namespace BlueRockLightsOut.Model
    {
        public class PuzzleInput
        {
            public int Depth;
            public int Rows;
            public int Cols;
            public int[,] Board;
            public List<Piece> Pieces;
            public static PuzzleInput Parse(string[] lines)
            {
                if (lines.Length < 3)
                    throw new FormatException("Expected 3 lines: depth, board, pieces.");
                int depth = int.Parse(lines[0].Trim());
                string[] boardRows = lines[1].Trim().Split(',');
                int rows = boardRows.Length;
                int cols = boardRows[0].Trim().Length;
                var board = new int[rows, cols];
                for (int r = 0; r < rows; r++)
                {
                    string row = boardRows[r].Trim();
                    if (row.Length != cols)
                        throw new FormatException($"Board row {r} has length {row.Length}, expected {cols}.");
                    for (int c = 0; c < cols; c++)
                        board[r, c] = row[c] - '0';
                }
                string[] pieceTokens = lines[2].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (pieceTokens.Length == 0)
                    throw new FormatException("No pieces found on line 3.");
                var pieces = new List<Piece>();
                foreach (string token in pieceTokens)
                {
                    string[] pieceRows = token.Split(',');
                    int height = pieceRows.Length;
                    int width = pieceRows[0].Length;
                    var mask = new bool[height, width];
                    for (int r = 0; r < height; r++)
                    {
                        if (pieceRows[r].Length != width)
                            throw new FormatException($"Piece '{token}' has inconsistent row widths.");
                        for (int c = 0; c < width; c++)
                        {
                            char ch = pieceRows[r][c];
                            if (ch != '.' && ch != 'X')
                                throw new FormatException($"Piece '{token}' contains invalid character '{ch}'.");
                            mask[r, c] = ch == 'X';
                        }
                    }
                    pieces.Add(new Piece(mask, height, width, rows, cols));
                }
                return new PuzzleInput
                {
                    Depth = depth,
                    Rows = rows,
                    Cols = cols,
                    Board = board,
                    Pieces = pieces
                };
            }
        }       
    }

}
