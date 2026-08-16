using BlueRockLightsOut.Model;
using BlueRockLightsOut.Model.BlueRockLightsOut.Model;

namespace BlueRockLightsOut.Services
{
    /// <summary>
    /// Cheap, solver-independent sanity checks that can prove a puzzle
    /// instance unsolvable (or flag suspicious input) before spending any
    /// time on backtracking or CP-SAT.
    /// </summary>
    public static class FeasibilityCheck
    {
        /// <summary>
        /// A cell that starts non-zero but that NO piece can ever reach (in
        /// any position) can never be zeroed. If such a cell exists, the
        /// puzzle is unsolvable, full stop — no algorithm will ever find a
        /// solution, so this should be checked before running any solver.
        /// </summary>
        public static bool ReportUnreachableNonZeroCells(PuzzleInput input)
        {
            ulong totalReach = 0UL;
            foreach (var piece in input.Pieces)
                totalReach |= piece.ReachMask;

            bool anyProblem = false;
            for (int r = 0; r < input.Rows; r++)
            {
                for (int c = 0; c < input.Cols; c++)
                {
                    if (input.Board[r, c] == 0)
                        continue;

                    int bit = r * input.Cols + c;
                    bool reachable = (totalReach & 1UL << bit) != 0;

                    if (!reachable)
                    {
                        Console.WriteLine(
                            $"Cell ({r},{c}) = {input.Board[r, c]} is UNREACHABLE by any piece — puzzle is unsolvable.");
                        anyProblem = true;
                    }
                }
            }

            if (!anyProblem)
                Console.WriteLine("Reachability check passed: every non-zero cell is reachable by at least one piece.");

            return !anyProblem; // true = OK to proceed, false = provably unsolvable
        }

        /// <summary>
        /// Weaker, cheap necessary condition: for each cell, at most
        /// (number of pieces that can ever reach it) increments are
        /// possible in total, so the value needed to zero it out
        /// (depth - value) can't exceed that count. This can catch some
        /// infeasible instances even when every cell is technically
        /// reachable — e.g. a cell needing 3 increments but only 1 piece
        /// can ever touch it.
        /// </summary>
        public static bool ReportUpperBoundViolations(PuzzleInput input)
        {
            var reachCountPerCell = new int[input.Rows * input.Cols];
            foreach (var piece in input.Pieces)
            {
                ulong mask = piece.ReachMask;
                while (mask != 0)
                {
                    int bit = System.Numerics.BitOperations.TrailingZeroCount(mask);
                    mask &= mask - 1;
                    reachCountPerCell[bit]++;
                }
            }

            bool anyProblem = false;
            for (int r = 0; r < input.Rows; r++)
            {
                for (int c = 0; c < input.Cols; c++)
                {
                    int value = input.Board[r, c];
                    if (value == 0)
                        continue;

                    int bit = r * input.Cols + c;
                    int needed = input.Depth - value;

                    if (needed > reachCountPerCell[bit])
                    {
                        Console.WriteLine(
                            $"Cell ({r},{c}) = {value} needs {needed} increments but only " +
                            $"{reachCountPerCell[bit]} piece(s) can ever reach it — puzzle is unsolvable.");
                        anyProblem = true;
                    }
                }
            }

            if (!anyProblem)
                Console.WriteLine("Upper-bound check passed: no cell needs more increments than pieces can provide.");

            return !anyProblem;
        }
    }
}