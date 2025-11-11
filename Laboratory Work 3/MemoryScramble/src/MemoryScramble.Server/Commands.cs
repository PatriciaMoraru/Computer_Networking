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
    /// <param name="board">Board, a Memory Scramble game board</param>
    /// <param name="playerId">string, ID of player looking at the board</param>
    /// <returns>string, the board state in protocol format from the player's perspective</returns>
    /// <remarks>
    /// Preconditions (requires):
    ///   • board is non-null
    ///   • playerId is non-null
    /// Postconditions (effects):
    ///   • Returns board state as protocol string (ROWSxCOLS\nspot1\nspot2\n...)
    ///   • Does not modify board state
    /// </remarks>
    public static string Look(Board board, string playerId)
    {
        return board.ViewBy(playerId).ToProtocolString();
    }

    /* PROBLEM 2: SYNCHRONOUS VERSION (commented out for Problem 3)
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
    END SYNCHRONOUS VERSION ------*/

    // PROBLEM 3: ASYNCHRONOUS VERSION WITH WAITING SUPPORT 
    /// <summary>
    /// Tries to flip over a card on the board, following the game rules.
    /// ASYNC VERSION: Properly waits when cards are controlled by other players.
    /// Pure glue code (2 lines) - all logic delegated to Board ADT.
    /// </summary>
    /// <param name="board">Board, a Memory Scramble game board</param>
    /// <param name="playerId">string, ID of player making the flip</param>
    /// <param name="row">int, row number of card to flip (0-indexed)</param>
    /// <param name="column">int, column number of card to flip (0-indexed)</param>
    /// <returns>Task&lt;string&gt;, async task resolving to the board state in protocol format after the flip</returns>
    /// <remarks>
    /// Preconditions (requires):
    ///   • board is non-null
    ///   • playerId is non-null
    ///   • 0 ≤ row &lt; board.Rows
    ///   • 0 ≤ column &lt; board.Cols
    /// Postconditions (effects):
    ///   • Attempts to flip card at (row, column) following game rules
    ///   • If successful → returns updated board state as protocol string
    ///   • If fails (FailNoCard or FailControlled) → throws InvalidOperationException
    /// Throws:
    ///   • InvalidOperationException if flip fails
    ///   • InvalidPositionException if position is out of bounds
    /// </remarks>
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
    /// <param name="board">Board, a Memory Scramble game board</param>
    /// <param name="playerId">string, ID of player applying the map operation</param>
    /// <param name="transformer">Func&lt;string, Task&lt;string&gt;&gt;, pure async function mapping card symbols to new symbols</param>
    /// <returns>Task&lt;string&gt;, async task resolving to the board state in protocol format after transformation</returns>
    /// <remarks>
    /// Preconditions (requires):
    ///   • board is non-null
    ///   • playerId is non-null
    ///   • transformer is a pure function (same input always produces same output)
    /// Postconditions (effects):
    ///   • Every card symbol s is replaced with transformer(s)
    ///   • Card state (face up/down, controlled/uncontrolled) is preserved
    ///   • Returns updated board state as protocol string
    ///   • Other operations can interleave during transformation
    /// </remarks>
    public static async Task<string> MapAsync(Board board, string playerId, Func<string, Task<string>> transformer)
    {
        var boardState = await board.MapAsync(playerId, transformer);
        return boardState.ToProtocolString();
    }

    // PROBLEM 5: WATCH FUNCTIONALITY
    /// <summary>
    /// Watches the board for a change, waiting until any cards turn face up or face down,
    /// are removed from the board, or change from one string to a different string.
    /// Pure glue code (2 lines) - all logic delegated to Board ADT.
    /// </summary>
    /// <param name="board">Board, a Memory Scramble game board</param>
    /// <param name="playerId">string, ID of player watching the board</param>
    /// <returns>Task&lt;string&gt;, async task resolving to the updated board state in protocol format</returns>
    /// <remarks>
    /// Preconditions (requires):
    ///   • board is non-null
    ///   • playerId is non-null
    /// Postconditions (effects):
    ///   • Waits until board changes (card flips, removal, or symbol transformation)
    ///   • Returns immediately if board already changed since player's last watch
    ///   • Returns updated board state as protocol string
    ///   • Does NOT trigger on control changes (take/release without flipping)
    /// </remarks>
    public static async Task<string> WatchAsync(Board board, string playerId)
    {
        await board.WaitForChangeAsync(playerId);
        return board.ViewBy(playerId).ToProtocolString();
    }

    /// <summary>
    /// Resets the board by reloading it from the specified file.
    /// Pure glue code (1 line) - delegates to Board factory method.
    /// </summary>
    /// <param name="boardFilePath">string, path to the board file to load</param>
    /// <returns>Board, a new Board instance loaded from the file</returns>
    /// <remarks>
    /// Preconditions (requires):
    ///   • boardFilePath is a valid path to an existing board file
    ///   • file format is valid (ROWSxCOLS followed by card symbols)
    /// Postconditions (effects):
    ///   • Creates and returns a new Board initialized from the file
    ///   • New board has all cards face down and uncontrolled
    /// Throws:
    ///   • InvalidBoardFileException if file not found or has invalid format
    /// </remarks>
    public static Board Reset(string boardFilePath)
    {
        return Board.ParseFromFile(boardFilePath);
    }
}

