# BlueRockLightsOut

A C# backtracking solver for a "Lights Out"–style puzzle: given a board of cells with values in `0..depth-1` and a set of pieces (polyomino-shaped masks), find a placement — one position per piece — such that toggling the covered cells (mod `depth`) drives every cell on the board to `0`.

## Status

**8 of 10 test levels passing.** Levels 9 and 10 involve significantly larger search spaces (more pieces / larger boards) and are still being optimized for runtime. See [Complexity](#complexity) and [Known Limitations](#known-limitations--possible-next-steps) below for details on where the remaining bottleneck is and what the next steps look like.

## Problem

- The board is a `Rows x Cols` grid, where each cell holds an integer in the range `[0, Depth)`.
- Each **piece** is a 2D boolean mask (a polyomino) that can be placed anywhere it fits fully within the board bounds (no rotation, no wraparound).
- Placing a piece at position `(x, y)` increments every board cell under an `X` in its mask by `1`, modulo `Depth`.
- **Every piece must be placed exactly once.**
- The puzzle is solved when, after all pieces are placed, every board cell equals `0`.

The solver's job is to find *where* each piece goes. This is a constraint-satisfaction / assignment problem: each piece is a variable, its domain is the set of positions it geometrically fits, and the cross-cutting constraint is that the sum of contributions to every cell must be `≡ 0 mod depth`.

## Input Format

Three lines:

```
<depth>
<board rows, comma-separated>
<piece tokens, space-separated>
```

- **Line 1** — `Depth`: the modulus for cell values (e.g. `4` means cells cycle `0 -> 1 -> 2 -> 3 -> 0`).
- **Line 2** — Board rows separated by commas, e.g. `010,101,010`. Each character is a digit representing that cell's starting value.
- **Line 3** — Piece definitions separated by spaces. Each piece is comma-separated rows using `X` (filled) and `.` (empty), e.g. `X.,.X` describes a 2x2 diagonal piece.

**Example:**
```
2
010,101,010
X.,.X XX
```

**Note on line endings:** if lines are read from an uploaded file that may use Windows-style `\r\n` endings, strip trailing `\r` from each line before calling `Parse` (a naive `Split('\n')` alone will leave a stray `\r` on the last piece token, which `Parse` will reject as an invalid character).

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
  - `Positions` — every valid top-left `(x, y)` coordinate where the piece fits on the board.
  - `PositionMasks` — a `ulong` bitmask per position (parallel to `Positions`) of exactly which board cells that placement covers. Bit index = `row * Cols + col`.
  - `ReachMask` — the union of every `PositionMask`: every cell this piece could possibly ever cover, from any position.

  **Board size constraint:** cells are packed into a `ulong`, so `Rows x Cols` must be `<= 64`. The constructor throws a clear `NotSupportedException` if exceeded — for larger boards this would need to move to `ulong[]` (multi-word) or `BigInteger` masks.

### `Solver.cs`

Backtracking search with three layers of pruning:

1. **Dynamic most-constrained-variable (MCV) selection with forward checking.** At each node, instead of picking the unplaced piece with the fewest *geometrically* valid positions (a static, parse-time number), the solver counts how many of each piece's positions are still *feasible* against the current board state, and branches on whichever piece is most constrained right now. If any unplaced piece has **zero** currently-feasible positions, the branch is dead and the solver bails out immediately — this is standard CSP "empty domain" detection, and it catches doomed branches far earlier than waiting for an unrecoverable cell.
2. **Feasibility bound per cell.** Each remaining unplaced piece can contribute at most `+1` to any cell it covers. So a cell needs `depth - value` more increments to reach zero, and that requirement can never exceed the number of currently-unplaced pieces that can still reach it. If it does, no assignment of the remaining pieces can possibly zero that cell, so the branch is pruned — checked incrementally, only over the cells the piece in question can reach, not a full board rescan.
3. **Pre-apply position filtering.** Candidate positions are checked for feasibility *before* mutating the board, so infeasible placements are skipped without the cost of an apply/undo cycle.

State is tracked incrementally rather than recomputed from scratch at each node:
- `_reachCount[cell]` — how many currently-unplaced pieces can still reach that cell; adjusted by `±1` as pieces are chosen/undone, not rebuilt per call.
- `_nonzeroMask` — a `ulong` bitmask of which cells are currently non-zero, updated bit-by-bit as cells change; the terminal "is the board all zero" check is an `O(1)` mask comparison instead of a full board scan.
- `Solver.NodesExplored` — a public diagnostic counter (how many times `Backtrack` was entered) for gauging whether a given puzzle instance's slowness is a pruning gap or a genuinely large search space.

The result array is indexed by each piece's **original position in the input** (not the order the solver happens to place them during search), so placements are returned in the same order the pieces were listed on line 3 of the input.

## Complexity

This is exhaustive backtracking search over a constraint-satisfaction problem, not a polynomial-time algorithm:

- **Worst case:** exponential — roughly `O(P^n x A)`, where `n` is the number of pieces, `P` is the max number of legal positions for any single piece, and `A` is the board area (`Rows x Cols`).
- **Per search node:** dominated by the dynamic MCV scan, roughly `O(sum of positions across unplaced pieces x reach-mask size)` in the worst case, though the early-exit-on-count-cap optimization bounds most of that work to `O(current best feasible count)` per piece rather than its full position list.
- In practice, dynamic MCV + forward checking + the feasibility bound prune the tree far more aggressively than a static ordering or a "check only at the leaf" approach — but there is no polynomial-time guarantee. This is fundamentally a hard combinatorial search problem, and larger instances (more pieces, larger boards, tighter constraints) can still take a long time despite the pruning.

## Known Limitations / Possible Next Steps

- **Levels 9-10 are not yet solving in reasonable time.** Node counts for harder instances (e.g. level 8: ~1M nodes) show the search space grows quickly with piece count and board size. The next lever under consideration is replacing hand-rolled backtracking with a real constraint solver — Google OR-Tools CP-SAT is a strong fit, since it performs global constraint propagation across all pieces simultaneously rather than the depth-first, locally-pruned approach used here.
- No piece rotation or reflection is considered; pieces are placed in their given orientation only.
- No support for overlapping piece placements.
- Board is capped at 64 cells due to the `ulong` bitmask representation (see `Piece` constructor note above).

## Usage

```csharp
var input = PuzzleInput.Parse(lines);
var solver = new Solver(input.Depth, input.Rows, input.Cols, input.Board, input.Pieces);

var sw = System.Diagnostics.Stopwatch.StartNew();
bool solved = solver.TrySolve(out var placements);
sw.Stop();

Console.WriteLine($"Solved: {solved}, Nodes: {solver.NodesExplored}, Time: {sw.ElapsedMilliseconds}ms");

if (solved)
{
    // placements[i] = (X, Y) position for input.Pieces[i]
    for (int i = 0; i < placements.Length; i++)
        Console.WriteLine($"Piece {i}: ({placements[i].X}, {placements[i].Y})");
}
```