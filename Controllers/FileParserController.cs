using BlueRockLightsOut.Model;
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

            var values = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var puzzle = PuzzleInput.Parse(values) ;

            var solver = new Solver(puzzle.Depth, puzzle.Rows, puzzle.Cols, puzzle.Board, puzzle.Pieces);

            if (!solver.TrySolve(out (int X, int Y)[] placements))
            {
                Console.Error.WriteLine("No solution found.");
                return Ok(1);
            }

            Console.WriteLine(string.Join(' ', placements.Select(p => $"{p.X},{p.Y}")));

            return Ok( string.Join(' ', placements.Select(p => $"{p.X},{p.Y}")));
        }
    }
}
