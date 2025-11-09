using MemoryScramble.Core;

namespace MemoryScramble;

/// <summary>
/// Simple glue code connecting Board operations to HTTP protocol.
/// Each function is intentionally kept to max 3 lines per MIT spec.
/// </summary>
public static class Commands
{
    /// <summary>
    /// Looks at the current state of the board.
    /// </summary>
    /// <param name="board">A Memory Scramble board</param>
    /// <param name="playerId">ID of player looking at the board</param>
    /// <returns>The state of the board from the player's perspective</returns>
    public static string Look(Board board, string playerId)
    {
        return board.ViewBy(playerId).ToProtocolString();
    }

    /* ===== PROBLEM 2: SYNCHRONOUS VERSION (commented out for Problem 3) =====
    /// <summary>
    /// Tries to flip over a card on the board, following the game rules.
    /// THIS WAS THE SYNCHRONOUS VERSION - replaced with async version in Problem 3
    /// </summary>
    public static string Flip(Board board, string playerId, int row, int column)
    {
        var outcome = board.Flip(playerId, new Position(row, column));
        if (outcome == FlipOutcome.FailNoCard || outcome == FlipOutcome.FailControlled)
            throw new InvalidOperationException($"Flip failed: {outcome}");
        return board.ViewBy(playerId).ToProtocolString();
    }
    ===== END SYNCHRONOUS VERSION ===== */

    // ===== PROBLEM 3: ASYNCHRONOUS VERSION WITH WAITING SUPPORT =====
    /// <summary>
    /// Tries to flip over a card on the board, following the game rules.
    /// ASYNC VERSION: Properly waits when cards are controlled by other players.
    /// </summary>
    /// <param name="board">A Memory Scramble board</param>
    /// <param name="playerId">ID of player making the flip</param>
    /// <param name="row">Row number of card to flip</param>
    /// <param name="column">Column number of card to flip</param>
    /// <returns>The state of the board after the flip</returns>
    /// <exception cref="InvalidOperationException">If the flip operation fails</exception>
    public static async Task<string> FlipAsync(Board board, string playerId, int row, int column)
    {
        var outcome = await board.FlipAsync(playerId, new Position(row, column));
        if (outcome == FlipOutcome.FailNoCard || outcome == FlipOutcome.FailControlled)
            throw new InvalidOperationException($"Flip failed: {outcome}");
        return board.ViewBy(playerId).ToProtocolString();
    }
}

