# BlueRockLightsOut

A C# backtracking solver for a "Lights Out"–style puzzle: given a board of cells with values in `0..depth-1` and a set of pieces (polyomino-shaped masks), find a placement — one position per piece — such that toggling the covered cells (mod `depth`) drives every cell on the board to `0`.

## Problem

- The board is a `Rows x Cols` grid, where each cell holds an integer in the range `[0, Depth)`.
- Each **piece** is a 2D boolean mask (a polyomino) that can be placed anywhere it fits fully within the board bounds (no rotation, no wraparound).
- Placing a piece at position `(x, y)` increments every board cell under an `X` in its mask by `1`, modulo `Depth`.
- **Every piece must be placed exactly once.**
- The puzzle is solved when, after all pieces are placed, every board cell equals `0`.

The solver's job is to find *where* each piece goes.

## Input Format

Three lines:

```
<depth>
<board rows, comma-separated>
<piece tokens, space-separated>
```

- **Line 1** — `Depth`: the modulus for cell values (e.g. `3` means cells cycle `0 -> 1 -> 2 -> 0`).
- **Line 2** — Board rows separated by commas, e.g. `010,101,010`. Each character is a digit representing that cell's starting value.
- **Line 3** — Piece definitions separated by spaces. Each piece is comma-separated rows using `X` (filled) and `.` (empty), e.g. `X.,.X` describes a 2x2 diagonal piece.

**Example:**
```
2
010,101,010
X.,.X XX
```

## Project Structure

```
BlueRockLightsOut/
├── Model/
│   └── PuzzleInput.cs   # Parses input into board + piece data
└── Solver.cs            # Backtracking search that finds piece placements
```

### `Model/PuzzleInput.cs`

- `PuzzleInput.Parse(string[] lines)` — parses the 3-line input format described above into a `Depth`, `Board`, and `List<Piece>`.
- `Piece` — represents one piece's shape (`Mask`) and precomputes, for the given board size:
  - `Positions`: every valid top-left `(x, y)` coordinate where the piece fits on the board.
  - `ReachCells`: the union of every board cell the piece could ever cover, across all its valid positions.

### `Solver.cs`

Backtracking search with two optimizations to prune the search space:

1. **Most-constrained-variable heuristic** — at each step, the unplaced piece with the *fewest* legal positions is chosen next. This surfaces dead ends as early (and as shallow) as possible, instead of discovering an unsolvable branch only after several pieces are already committed.
2. **Frozen-cell pruning** — after each placement, the solver checks whether any board cell that *none of the remaining unplaced pieces can ever reach* is still non-zero. If so, that branch can never reach an all-zero board, so it's abandoned immediately rather than searching deeper.

Board cell values are updated with modular arithmetic (`Apply`), so placing and undoing a piece (`+1` / `-1`) is symmetric and reversible during backtracking.

The result array is indexed by each piece's **original position in the input**, not the order in which the solver happens to place them during search — so placements come back in the same order the pieces were listed on line 3 of the input.

## Complexity

This is exhaustive backtracking search, not a polynomial algorithm:

- **Worst case:** exponential — roughly `O(P^n x A)`, where `n` is the number of pieces, `P` is the max number of legal positions for any single piece, and `A` is the board area (`Rows x Cols`).
- **Per search node:** `O(n x A)`, dominated by rebuilding the "unreachable cells" set and re-scanning the full board for the frozen-cell check.
- In practice, the most-constrained-variable ordering and frozen-cell pruning cut the tree down significantly versus brute force, but there is no polynomial-time guarantee — this is fundamentally a constraint-satisfaction search problem.


## Usage

```csharp
var input = PuzzleInput.Parse(lines);
var solver = new Solver(input.Depth, input.Rows, input.Cols, input.Board, input.Pieces);

if (solver.TrySolve(out var placements))
{
    // placements[i] = (X, Y) position for input.Pieces[i]
    for (int i = 0; i < placements.Length; i++)
        Console.WriteLine($"Piece {i}: ({placements[i].X}, {placements[i].Y})");
}
else
{
    Console.WriteLine("No solution found.");
}
```