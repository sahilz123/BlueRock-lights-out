# BlueRockLightsOut

A C# solver for a "Lights Out"–style puzzle: given a board of cells with values in `0..depth-1` and a set of pieces (polyomino-shaped masks), find a placement — one position per piece — such that toggling the covered cells (mod `depth`) drives every cell on the board to `0`.

## Status

**10 of 10 test levels passing**, using two solvers depending on instance difficulty:

- **Levels 1-8**: solved by a hand-rolled backtracking search with CSP-style pruning (fast — sub-second to a few seconds).
- **Levels 9-10**: solved by a Google OR-Tools CP-SAT model (levels 9: ~100s, level 10: ~5 minutes after optimization — see [Performance Notes](#performance-notes)).

The two-solver split isn't a workaround — it reflects a real finding during development: hand-rolled backtracking, however well pruned, hits a genuine complexity wall on higher-`depth`, higher-piece-count instances, and a real constraint solver (which does global propagation across all pieces and cells simultaneously) is the correct tool past that point.

## Problem

- The board is a `Rows x Cols` grid, where each cell holds an integer in the range `[0, Depth)`.
- Each **piece** is a 2D boolean mask (a polyomino) that can be placed anywhere it fits fully within the board bounds (no rotation, no wraparound).
- Placing a piece at position `(x, y)` increments every board cell under an `X` in its mask by `1`, modulo `Depth`.
- **Every piece must be placed exactly once.**
- The puzzle is solved when, after all pieces are placed, every board cell equals `0`.

This is fundamentally a constraint-satisfaction / assignment problem: each piece is a variable, its domain is the set of positions it geometrically fits, and the constraint tying every piece together is that the sum of contributions to every cell must be `≡ 0 mod depth`.

## Input Format

Three lines:

```
<depth>
<board rows, comma-separated>
<piece tokens, space-separated>
```

- **Line 1** — `Depth`: the modulus for cell values.
- **Line 2** — Board rows separated by commas, e.g. `010,101,010`. Each character is a digit representing that cell's starting value.
- **Line 3** — Piece definitions separated by spaces. Each piece is comma-separated rows using `X` (filled) and `.` (empty), e.g. `X.,.X` describes a 2x2 diagonal piece.

**Note on line endings:** if lines are read from an uploaded file that may use Windows-style `\r\n` endings, strip trailing `\r` from each line before calling `Parse` — a naive `Split('\n')` alone will leave a stray `\r` on the last piece token, which `Parse` will reject as an invalid character.

## Project Structure

```
BlueRockLightsOut/
├── Model/
│   └── PuzzleInput.cs      # Parses input into board + piece data
├── Solver.cs                # Backtracking solver (levels 1-8)
├── CpSatSolver.cs            # CP-SAT solver (levels 9-10)
├── DomainPreprocessor.cs      # Static domain reduction, used before CP-SAT
└── FeasibilityCheck.cs         # Cheap unsolvability checks, run before either solver
```

### `Model/PuzzleInput.cs`

Parses the 3-line input into `Depth`, `Board`, and a `List<Piece>`. Each `Piece` precomputes, for the given board size:

- `Positions` — every valid top-left `(x, y)` coordinate where the piece fits on the board.
- `PositionMasks` — a `ulong` bitmask per position (parallel to `Positions`), where bit `row * Cols + col` marks a covered cell.
- `ReachMask` — the union of every `PositionMask`: every cell this piece could possibly ever cover, from any position.

**Board size constraint:** cells are packed into a `ulong`, so `Rows x Cols` must be `<= 64`. The `Piece` constructor throws a clear `NotSupportedException` if exceeded.

### `FeasibilityCheck.cs`

Two cheap, solver-independent checks run before any search, so a provably-unsolvable instance never wastes solver time:

1. **Reachability** — every non-zero board cell must be reachable by at least one piece in at least one position. If not, that cell can never be zeroed, full stop.
2. **Upper bound** — for every non-zero cell, the number of increments needed to zero it (`depth - value`) can't exceed the number of pieces that can ever reach it (each piece contributes at most `+1` if it covers that cell).

Both must pass before either solver is worth running.

### `Solver.cs` (backtracking, levels 1-8)

Backtracking search with several layers of pruning, all maintained incrementally rather than recomputed from scratch each node:

1. **Dynamic most-constrained-variable (MCV) selection with forward checking.** At each node, the solver counts how many of each unplaced piece's positions are still feasible against the *current* board state (not a static, parse-time position count), and branches on whichever piece is most constrained right now. If any unplaced piece has **zero** currently-feasible positions, the branch is dead and the solver bails out immediately (classic CSP "empty domain" detection).
2. **Feasibility bound per cell**, checked incrementally via `_reachCount[cell]` — a running count of how many still-unplaced pieces can reach each cell, adjusted by `±1` as pieces are chosen/undone rather than rebuilt per call.
3. **Bitmask state tracking** — `_nonzeroMask` (a `ulong`) tracks which cells are currently non-zero, so the terminal "is the board all zero" check is an `O(1)` mask comparison instead of a full board scan.
4. **Pre-apply position filtering** — candidate positions are checked for feasibility *before* mutating the board, so infeasible placements are skipped without the cost of an apply/undo cycle.
5. `Solver.NodesExplored` — a public diagnostic counter for gauging whether a puzzle instance's slowness is a pruning gap or a genuinely large search space.

The result array is indexed by each piece's **original position in the input** (not the order the solver places them during search), so placements come back in the same order the pieces were listed on line 3.

### `DomainPreprocessor.cs`

A static, solver-independent domain-reduction pass, run before CP-SAT builds its model. For every candidate position of every piece, checks whether placing the piece there could *ever* be part of any solution, using the same "needed increments can't exceed remaining reachable pieces" logic the backtracking solver uses dynamically — but applied once upfront, iterated to a fixpoint (removing one piece's bad position can shrink its reach and cascade into other pieces' positions becoming provably infeasible too, similar to arc-consistency propagation in CSPs).

Feeding the survivors into CP-SAT means fewer `BoolVar`s, fewer constraint terms, and a tighter starting point before CP-SAT's own search even begins.

### `CpSatSolver.cs` (CP-SAT, levels 9-10)

Models the puzzle as a constraint-satisfaction problem and hands it to Google OR-Tools CP-SAT:

- **One `BoolVar` per (piece, surviving position)** — `true` iff that piece is placed there. An `AddExactlyOne` constraint per piece enforces "placed exactly once."
- **Per-cell modular constraint** — for every board cell, the initial value plus the sum of every covering position-var must be an exact multiple of `depth` (`total == depth * q` for an auxiliary integer `q`), i.e. `total mod depth == 0`.
- **Symmetry breaking for duplicate-shaped pieces** — pieces with an identical mask produce identical `Positions`/`PositionMasks` lists and are fully interchangeable. Left alone, the solver wastes time re-exploring "swapped" solutions that are equally valid. For each group of duplicate-shape pieces, their chosen position indices are constrained into non-decreasing order, eliminating those redundant permutations from the search space.
- **Solver parameters tuned for this problem shape**: parallel search workers (matched to `Environment.ProcessorCount`), more aggressive presolve probing, and CP-SAT's own symmetry detection enabled alongside the explicit symmetry-breaking constraints above.
- **Status handling** distinguishes `Infeasible` (proven unsolvable — stop, don't retry with more time) from `Unknown` (timed out with no verdict — worth retrying with a larger time budget) rather than collapsing both into a generic "no solution found."

## Complexity

**Backtracking (`Solver.cs`):** exhaustive search over a CSP, worst case exponential — roughly `O(P^n x A)` where `n` is piece count, `P` is max positions per piece, `A` is board area. In practice, dynamic MCV + forward checking + the feasibility bound prune the tree far more aggressively than a static ordering, but there's no polynomial-time guarantee — this is fundamentally a hard combinatorial search problem, and it stops being practical on the harder instances (see Performance Notes).

**CP-SAT (`CpSatSolver.cs`):** still solving an NP-hard problem (this is exact-cover-adjacent), but CP-SAT's global constraint propagation, learned-clause search (CDCL-style), and parallel portfolio search scale meaningfully better than depth-first backtracking on the harder instances this project needed to handle.

## Performance Notes

Node counts and timings observed during development, included here because they were the actual evidence behind the two-solver decision, not just a design guess:

| Level | Solver | Depth | Result |
|---|---|---|---|
| 1 | Backtracking | 2 | 77 nodes |
| 4 | Backtracking | 2 | 36,631 nodes |
| 8 | Backtracking | 2 | ~1.05M nodes, seconds |
| 9 | CP-SAT | 4 | ~100s |
| 10 | CP-SAT | 4 | 459.5s initially → 306.2s after domain preprocessing + tuning |

The jump in node counts from level 1 → 8 (roughly 475x, then another ~30x) shows the expected combinatorial blow-up of backtracking search. The switch to CP-SAT for levels 9-10 (both `depth=4`, more permissive per-cell constraints, weaker pruning power for the backtracker) was a direct response to that data, not a preemptive choice.

The 33% improvement on level 10 (459.5s → 306.2s) came from two additions, in order of impact:
1. **Domain preprocessing** — pruning provably-infeasible positions before CP-SAT builds its model.
2. **Symmetry breaking** for any duplicate-shaped pieces, plus general parameter tuning (parallel workers, presolve probing, CP-SAT's built-in symmetry detection).

## Known Limitations / Possible Next Steps

- No piece rotation or reflection is considered; pieces are placed in their given orientation only.
- No support for overlapping piece placements.
- Board is capped at 64 cells due to the `ulong` bitmask representation used throughout (`Piece.ReachMask`, `PositionMasks`, `Solver`'s internal state tracking).
- There's no automatic solver selection yet — `Solver` and `CpSatSolver` are invoked explicitly per level. A natural next step would be a wrapper that tries backtracking with a time/node budget first (cheap for easy instances) and falls back to CP-SAT if that budget is exceeded, rather than deciding by hand which levels need which solver.
- Further CP-SAT tuning (a custom decision strategy mirroring the backtracking solver's proven MCV heuristic, warm-start hints) was considered but not pursued, since gains beyond this point looked to have diminishing returns relative to the tuning effort.

## Usage

### Backtracking solver

```csharp
var input = PuzzleInput.Parse(lines);

if (!FeasibilityCheck.ReportUnreachableNonZeroCells(input) ||
    !FeasibilityCheck.ReportUpperBoundViolations(input))
{
    Console.WriteLine("Provably unsolvable — skipping search.");
    return;
}

var solver = new Solver(input.Depth, input.Rows, input.Cols, input.Board, input.Pieces);

var sw = System.Diagnostics.Stopwatch.StartNew();
bool solved = solver.TrySolve(out var placements);
sw.Stop();

Console.WriteLine($"Solved: {solved}, Nodes: {solver.NodesExplored}, Time: {sw.ElapsedMilliseconds}ms");
```

### CP-SAT solver

Requires the `Google.OrTools` NuGet package:

```
dotnet add package Google.OrTools
```

```csharp
var input = PuzzleInput.Parse(lines);

if (!FeasibilityCheck.ReportUnreachableNonZeroCells(input) ||
    !FeasibilityCheck.ReportUpperBoundViolations(input))
{
    Console.WriteLine("Provably unsolvable — skipping search.");
    return;
}

var sw = System.Diagnostics.Stopwatch.StartNew();
bool solved = CpSatSolver.TrySolve(input, out var placements, maxTimeInSeconds: 600);
sw.Stop();

Console.WriteLine($"Solved: {solved}, Time: {sw.ElapsedMilliseconds}ms");

if (solved)
{
    for (int i = 0; i < placements.Length; i++)
        Console.WriteLine($"Piece {i}: ({placements[i].X}, {placements[i].Y})");
}
```