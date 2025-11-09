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

    /// <summary>
    /// Tries to flip over a card on the board, following the game rules.
    /// </summary>
    /// <param name="board">A Memory Scramble board</param>
    /// <param name="playerId">ID of player making the flip</param>
    /// <param name="row">Row number of card to flip</param>
    /// <param name="column">Column number of card to flip</param>
    /// <returns>The state of the board after the flip</returns>
    /// <exception cref="InvalidOperationException">If the flip operation fails</exception>
    public static string Flip(Board board, string playerId, int row, int column)
    {
        var outcome = board.Flip(playerId, new Position(row, column));
        if (outcome == FlipOutcome.FailNoCard || outcome == FlipOutcome.FailControlled)
            throw new InvalidOperationException($"Flip failed: {outcome}");
        return board.ViewBy(playerId).ToProtocolString();
    }
}

