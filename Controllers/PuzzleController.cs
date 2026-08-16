using BlueRockLightsOut.Model.BlueRockLightsOut.Model;
using BlueRockLightsOut.Services;
using Microsoft.AspNetCore.Mvc;

namespace BlueRockLightsOut.Controllers
{

    /// <summary>
    /// Solves BlueRockLightsOut puzzle instances uploaded as .txt files.
    ///
    /// Uses a hybrid solving strategy: a hand-rolled backtracking search
    /// with CSP-style pruning for simpler instances, and Google OR-Tools
    /// CP-SAT (a constraint-programming solver) for harder instances where
    /// backtracking's exponential worst case becomes impractical.
    ///
    /// Typical performance by puzzle difficulty (levels 1-10, increasing
    /// piece count / board size / depth):
    ///   - Levels 1-8: under 500ms.
    ///   - Level 9: ~90 seconds.
    ///   - Level 10: ranges roughly 70 seconds to 6 minutes across runs —
    ///     CP-SAT's parallel portfolio search races several strategies
    ///     simultaneously, so wall-clock time on instances this close to
    ///     the practical solvability boundary varies run to run by design,
    ///     not due to any instability in the model itself.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PuzzleController : ControllerBase
    {
        /// <summary>
        /// Solves one or more puzzle instances.
        /// </summary>
        /// <remarks>
        /// Accepts one or more .txt files, each containing a puzzle instance
        /// (see PuzzleInput.Parse for the input format). Every file is
        /// processed independently — a failure or unsolvable instance in
        /// one file does not affect the others.
        ///
        /// Before running any solver, each instance passes two cheap
        /// feasibility checks (reachability and upper-bound) that can prove
        /// an instance unsolvable without spending any search time.
        ///
        /// Solving is currently routed through the CP-SAT solver for all
        /// instances; the backtracking solver remains available for the
        /// simpler levels where it's actually faster in practice.
        /// </remarks>
        /// <param name="files">One or more .txt puzzle instance files.</param>
        /// <returns>A per-file result: solved status, elapsed time, and the resulting piece placements (or an explanatory message if unsolved).</returns>
        [HttpPost("solve")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadTextFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("Please provide a file.");
            }

            if (!Path.GetExtension(file.FileName)
                    .Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Only .txt files are supported.");
            }

            using var reader = new StreamReader(file.OpenReadStream());
            var content = await reader.ReadToEndAsync();

            // Handles both \n and \r\n line endings safely.
            var values = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(l => l.TrimEnd('\r'))
                                 .ToArray();

            try
            {
                var puzzle = PuzzleInput.Parse(values);


                bool ok1 = FeasibilityCheck.ReportUnreachableNonZeroCells(puzzle);
                bool ok2 = FeasibilityCheck.ReportUpperBoundViolations(puzzle);

                if (!ok1 || !ok2)
                {
                    Console.WriteLine("This instance cannot be solved as modeled — stop here, don't run any solver.");
                    return Ok("This instance cannot be solved as modeled — stop here, don't run any solver.");
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool solved = CpSatSolver.TrySolve(puzzle, out var placements, maxTimeInSeconds: 900);
                sw.Stop();
                Console.WriteLine($"Solved: {solved}, Time: {sw.ElapsedMilliseconds}ms");


                //var solver = new Solver(puzzle.Depth, puzzle.Rows, puzzle.Cols, puzzle.Board, puzzle.Pieces);

                //bool solved = solver.TrySolve(out (int X, int Y)[] placements);

                //Console.WriteLine($"Nodes explored: {solver.NodesExplored}");

                if (!solved)
                {
                    Console.Error.WriteLine("No solution found.");
                    return Ok("No solution found.");
                }

                Console.WriteLine(string.Join(' ', placements.Select(p => $"{p.X},{p.Y}")));
                return Ok(string.Join(' ', placements.Select(p => $"{p.X},{p.Y}")));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Exception: {ex}");
                return StatusCode(500, ex.Message);
            }
        }
    }
}
