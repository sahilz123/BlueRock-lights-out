using BlueRockLightsOut.Model;
using BlueRockLightsOut.Model.BlueRockLightsOut.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlueRockLightsOut.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FileParserController : ControllerBase
    {
        [HttpPost("upload")]
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
                var solver = new Solver(puzzle.Depth, puzzle.Rows, puzzle.Cols, puzzle.Board, puzzle.Pieces);

                bool solved = solver.TrySolve(out (int X, int Y)[] placements);

                Console.WriteLine($"Nodes explored: {solver.NodesExplored}");

                if (!solved)
                {
                    Console.Error.WriteLine("No solution found.");
                    return Ok(1);
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
