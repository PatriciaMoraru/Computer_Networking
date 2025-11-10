# Memory Scramble - Fuzz Testing Simulation

## Purpose

This simulation performs **randomized fuzz testing** to stress-test the Memory Scramble game under concurrent load. It simulates multiple players making random moves with random delays to detect race conditions, deadlocks, and crashes.

## Specifications

- **Players**: 4 concurrent players
- **Moves per player**: 100 (400 total moves)
- **Delay range**: 0.1ms - 2ms (random per move)
- **Goal**: Complete hundreds of moves in under 1 second
- **Target**: Zero crashes/errors

## Why These Parameters?

### Small Random Delays (0.1ms - 2ms)
The random timeouts ensure realistic race conditions:
- Some players make multiple moves while others make just one
- Creates unpredictable interleaving of concurrent operations
- Maximizes chance of exposing thread-safety bugs

### No Shuffling
The board layout stays consistent, making bugs reproducible if they occur.

### High Move Count
400 moves provide good statistical coverage without taking too long.

## Usage

### Start the server first:
```bash
dotnet run --project src/MemoryScramble.Server 8080 boards/zoom.txt
```

### Run the simulation:
```bash
dotnet run --project src/MemoryScramble.Simulation
```

### Custom server URL:
```bash
dotnet run --project src/MemoryScramble.Simulation http://localhost:8080 boards/ab.txt
```

## Interpreting Results

### Overall Statistics

**✓ Successes**  
Flip operations that successfully flipped a card (first or second card in a turn).

**✗ Failures**  
**NOT bugs!** These are expected game behaviors:
- `409 Conflict`: Card controlled by another player
- `409 Conflict`: No card at position (already matched/removed)
- Valid rule violations

**⚠ Errors**  
**These ARE bugs!** Unexpected errors:
- `500 Internal Server Error`: Server crash
- Network exceptions
- Assertion failures

### Per-Player Statistics

The simulation tracks individual player performance:

**Moves:** Total number of moves attempted (should always be 100)

**Duration:** How long this player took to complete all moves
- Varies based on random delays (0.1-2ms per move)
- Shows how much time player spent waiting for cards
- Players sorted from fastest to slowest

**Speed:** Individual throughput (moves/second)
- Higher is better
- Variance shows realistic concurrent behavior
- Players with more conflicts will be slower

**Successes/Failures/Errors:** Breakdown per player
- Success rate varies based on timing and collisions
- Lower success rate = more concurrent conflicts (good for testing!)

**Key Insights:**
- **Small duration variance** (e.g., 404ms - 484ms) = efficient parallelization
- **Similar speeds** = no player is starving or dominating
- **All players complete** = no deadlocks
- **Zero errors across all players** = stable under concurrent load

## Success Criteria

✅ **Errors = 0** (No crashes)  
✅ **Duration < 1 second** (Performance)  
✅ **All players complete** (No deadlocks)

## Example Output

```
╔════════════════════════════════════════════════════════════════╗
║           Memory Scramble - Fuzz Testing Simulation           ║
╚════════════════════════════════════════════════════════════════╝

Server URL:        http://localhost:8080
Board:             boards/zoom.txt
Players:           4
Moves per player:  100
Total moves:       400
Delay range:       0,1ms - 2ms

Checking server... ✓ Server is running
Reading board dimensions... ✓ 5x5 board

Starting simulation...
═══════════════════════════════════════════════════════════════

[sim_player_0] Starting simulation...
[sim_player_1] Starting simulation...
[sim_player_2] Starting simulation...
[sim_player_3] Starting simulation...
[sim_player_2] Completed 100 moves in 404ms: 19 successes, 81 failures, 0 errors
[sim_player_0] Completed 100 moves in 452ms: 16 successes, 84 failures, 0 errors
[sim_player_3] Completed 100 moves in 482ms: 22 successes, 78 failures, 0 errors
[sim_player_1] Completed 100 moves in 484ms: 21 successes, 79 failures, 0 errors

═══════════════════════════════════════════════════════════════
Simulation Complete!
═══════════════════════════════════════════════════════════════

Total duration:    487ms (0,49s)
Total moves:       400
Moves per second:  821

✓ Successes:       78
✗ Failures:        322
⚠ Errors:          0

───────────────────────────────────────────────────────────────
Per-Player Statistics:
───────────────────────────────────────────────────────────────

[sim_player_2]
  Moves:      100
  Duration:   404ms (0,40s)
  Speed:      248 moves/sec
  Successes:  19
  Failures:   81
  Errors:     0

[sim_player_0]
  Moves:      100
  Duration:   452ms (0,45s)
  Speed:      221 moves/sec
  Successes:  16
  Failures:   84
  Errors:     0

[sim_player_3]
  Moves:      100
  Duration:   482ms (0,48s)
  Speed:      207 moves/sec
  Successes:  22
  Failures:   78
  Errors:     0

[sim_player_1]
  Moves:      100
  Duration:   484ms (0,48s)
  Speed:      207 moves/sec
  Successes:  21
  Failures:   79
  Errors:     0

───────────────────────────────────────────────────────────────
╔════════════════════════════════════════════════════════════════╗
║                    ✓ ALL TESTS PASSED ✓                       ║
║                   No crashes detected!                         ║
╚════════════════════════════════════════════════════════════════╝
```

## Testing Different Scenarios

### Different Boards
```bash
# Test with small board (3x3)
dotnet run --project src/MemoryScramble.Simulation http://localhost:8080 boards/perfect.txt

# Test with simple board (5x5)
dotnet run --project src/MemoryScramble.Simulation http://localhost:8080 boards/ab.txt

# Test with complex board (5x5)
dotnet run --project src/MemoryScramble.Simulation http://localhost:8080 boards/zoom.txt
```

### Visual Testing (Slower)
To watch the game unfold in the browser, you can temporarily modify the constants in `Program.cs`:
```csharp
private const double MIN_DELAY_MS = 500;  // 500ms - 1s delays
private const double MAX_DELAY_MS = 1000; // Makes moves visible in UI
```

But remember: **the goal is speed, not visualization!** Keep delays at 0.1-2ms for actual testing.

## What This Tests

### Concurrency Issues
- ✓ Race conditions when multiple players flip simultaneously
- ✓ Deadlocks when players wait for controlled cards
- ✓ Thread-safety of board state access
- ✓ Proper lock ordering

### Game Rules Under Stress
- ✓ Card control management
- ✓ Match detection
- ✓ Card removal (cleanup)
- ✓ Face-up/face-down state transitions
- ✓ Waiting queue (FIFO fairness)

### Performance
- ✓ System handles hundreds of moves per second
- ✓ No performance degradation over time
- ✓ Efficient lock contention handling

## Related MIT Course Material

This simulation implements **randomized fuzz testing** as described in:
- MIT 6.102 Chapter 2: Testing
- Specifically: `simulation.ts` example

The approach:
1. **Generate random inputs** (random positions, random timing)
2. **Run many iterations** (400 moves)
3. **Check for crashes** (errors = 0)
4. **Verify invariants** (checkRep() assertions)

This is more effective than manual testing because:
- Tests unpredictable interleaving patterns
- Finds bugs that only occur under specific timing
- Provides statistical confidence (no bugs in 400 moves)

