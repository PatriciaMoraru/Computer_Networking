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
    /// Pure glue code (2 lines) - all logic delegated to Board ADT.
    /// </summary>
    /// <param name="board">A Memory Scramble board</param>
    /// <param name="playerId">ID of player making the flip</param>
    /// <param name="row">Row number of card to flip</param>
    /// <param name="column">Column number of card to flip</param>
    /// <returns>The state of the board after the flip</returns>
    /// <exception cref="InvalidOperationException">If the flip operation fails</exception>
    public static async Task<string> FlipAsync(Board board, string playerId, int row, int column)
    {
        var boardState = await board.FlipAndViewAsync(playerId, new Position(row, column));
        return boardState.ToProtocolString();
    }

    /// <summary>
    /// Modifies board by replacing every card with f(card), without affecting other state.
    /// This operation can interleave with other operations. The board remains observably 
    /// pairwise consistent: if two cards match before map(), they will always be observed 
    /// as matching during and after map().
    /// </summary>
    /// <param name="board">A Memory Scramble board</param>
    /// <param name="playerId">ID of player applying the map</param>
    /// <param name="transformer">Pure function from cards to cards (async)</param>
    /// <returns>The state of the board after replacement from playerId's perspective</returns>
    public static async Task<string> MapAsync(Board board, string playerId, Func<string, Task<string>> transformer)
    {
        var boardState = await board.MapAsync(playerId, transformer);
        return boardState.ToProtocolString();
    }

    // ===== PROBLEM 5: WATCH FUNCTIONALITY =====
    /// <summary>
    /// Watches the board for a change, waiting until any cards turn face up or face down,
    /// are removed from the board, or change from one string to a different string.
    /// Pure glue code (2 lines) - all logic delegated to Board ADT.
    /// </summary>
    /// <param name="board">A Memory Scramble board</param>
    /// <param name="playerId">ID of player watching the board</param>
    /// <returns>The updated state of the board from playerId's perspective</returns>
    public static async Task<string> WatchAsync(Board board, string playerId)
    {
        await board.WaitForChangeAsync();
        return board.ViewBy(playerId).ToProtocolString();
    }
}

