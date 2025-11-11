using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MemoryScramble.Core;

#region Public value types

/// <summary>
/// Immutable board coordinate (Row, Col).
/// </summary>
/// <remarks>
/// AF(p) = a unique (row, col) pair within the board dimensions.
/// RI(p): Row ∈ [0, Rows-1], Col ∈ [0, Cols-1] (checked by Board).
/// Safe from rep exposure because it's a readonly record struct.
/// </remarks>
public readonly record struct Position(int Row, int Col);

/// <summary>
/// Possible outcomes of a player's flip attempt.
/// </summary>
public enum FlipOutcome
{
    FirstControlled,
    SecondMatch,
    SecondNoMatch,
    FailNoCard,
    FailControlled
}

/// <summary>
/// What the player must resolve before their next first flip.
/// </summary>
public enum PlayerTurnStatus
{
    Unfinished,
    NoMatchShown,
    MatchHeld
}

#endregion

#region Exceptions

/// <summary>
/// Thrown when accessing an invalid position.
/// </summary>
public class InvalidPositionException : Exception
{
    public InvalidPositionException(Position p)
        : base($"Invalid board position {p}.") { }
}

/// <summary>
/// Thrown when a board file fails to parse according to the grammar.
/// </summary>
public class InvalidBoardFileException : Exception
{
    public InvalidBoardFileException(string message) : base(message) { }
}

#endregion

#region BoardState

/// <summary>
/// Immutable snapshot of the visible board from one player's perspective.
/// </summary>
/// <remarks>
/// AF(this) = textual state of all positions for a given player:
///   "none", "down", "up X", or "my X" per the protocol.
/// RI(this):
///   • Spots.Count == Rows * Cols
///   • Each string matches the allowed grammar.
/// </remarks>
public sealed class BoardState
{
    public int Rows { get; }
    public int Cols { get; }
    public IReadOnlyList<string> Spots { get; }

    /// <summary>
    /// Creates a new BoardState with the specified dimensions and spot descriptions.
    /// </summary>
    /// <param name="rows">int, number of rows</param>
    /// <param name="cols">int, number of columns</param>
    /// <param name="spots">IReadOnlyList&lt;string&gt;, list of spot descriptions (length must equal rows * cols)</param>
    /// <remarks>
    /// Preconditions (requires):
    ///   • rows > 0 and cols > 0
    ///   • spots.Count == rows * cols
    ///   • each spot string matches protocol: "none", "down", "up [symbol]", or "my [symbol]"
    /// Postconditions (effects):
    ///   • Creates an immutable BoardState snapshot
    /// </remarks>
    public BoardState(int rows, int cols, IReadOnlyList<string> spots)
    {
        Rows = rows;
        Cols = cols;
        Spots = spots;
    }

    /// <summary>
    /// Converts this BoardState to the protocol string format.
    /// </summary>
    /// <returns>string, protocol-formatted board state</returns>
    /// <remarks>
    /// Preconditions (requires): none
    /// Postconditions (effects):
    ///   • Returns a string in the format:
    ///     ROWSxCOLS\n
    ///     spot1\n
    ///     spot2\n
    ///     ...
    ///     spotN\n
    ///   where N = Rows * Cols
    /// </remarks>
    public string ToProtocolString()
    {
        var sb = new StringBuilder();
        sb.Append(Rows).Append('x').Append(Cols).Append('\n');
        foreach (var s in Spots) sb.Append(s).Append('\n');
        return sb.ToString();
    }

    public override string ToString() => ToProtocolString();
}

#endregion

#region Board ADT documentation skeleton

/// <summary>
/// A mutable Abstract Data Type representing a Memory Scramble game board.
/// The Board supports synchronous gameplay for multiple players according
/// to rules 1–3 of the assignment.
/// </summary>
/// <remarks>
/// Abstraction Function (AF):
///   AF(this) = a grid of ROWS × COLS positions, where each position
///   may contain a card with symbol s (string) or be empty (none).
///   Each card has attributes:
///     - FaceUp: bool (true if visible to all players)
///     - ControlledBy: string? (player ID who currently controls it)
///   Board also tracks per-player state for enforcing the rules:
///     first/second flip logic and rule-3 resolution.
///
/// Representation Invariant (RI):
///   • rows > 0 and cols > 0
///   • if a position is empty ⇒ FaceUp == false and ControlledBy == null
///   • if ControlledBy != null ⇒ position is not empty and FaceUp == true
///   • no two players control the same position
///   • each player controls at most two positions
///   • if a player controls two cards ⇒ their symbols are equal
///
/// Safety from Representation Exposure:
///   • All internal structures (grid, card slots, player states) are private.
///   • BoardState returned by ViewBy() is immutable.
///   • Position is a value type (record struct), not exposing internal pointers.
/// </remarks>
public sealed class Board
{
    // internal representation
    private sealed class CardSlot
    {
        public string? Symbol;
        public bool FaceUp;
        public string? ControlledBy;
        public bool IsEmpty => Symbol is null;
        
        // PROBLEM 3: Added for concurrent player support
        // Queue of players waiting to control this card
        public Queue<TaskCompletionSource<bool>> WaitingQueue { get; } = new();
    }

    private sealed class PlayerState
    {
        public List<Position> Controlled { get; } = new(2);
        public PlayerTurnStatus Pending { get; set; } = PlayerTurnStatus.Unfinished;
        public List<Position> LastShown { get; } = new(2);
    }

    private readonly int _rows;
    private readonly int _cols;
    private readonly CardSlot[,] _grid;
    private readonly Dictionary<string, PlayerState> _players = new(StringComparer.Ordinal);
    
    // PROBLEM 3: Added lock for thread-safe concurrent access 
    private readonly object _lock = new();
    
    // Serialize flips per-player to prevent overlapping first/second flip races
    private readonly Dictionary<string, SemaphoreSlim> _playerLocks = new(StringComparer.Ordinal);
    
    // PROBLEM 5: Watch functionality (versioned) 
    // Monotonic version that increments on any visible change
    private long _version = 0;
    // List of watchers waiting for the next version; each is completed with the new version
    private readonly List<TaskCompletionSource<long>> _watchers = new();
    // Tracks the last delivered version per player to eliminate long-poll gaps
    private readonly Dictionary<string, long> _playerLastVersion = new(StringComparer.Ordinal);

    [Conditional("DEBUG")]
    private void checkRep()
    {
        Debug.Assert(_grid.GetLength(0) == _rows);
        Debug.Assert(_grid.GetLength(1) == _cols);

        var occupied = new HashSet<(int, int)>();

        foreach (var (pid, st) in _players)
        {
            Debug.Assert(st.Controlled.Count <= 2);
            if (st.Controlled.Count == 2)
            {
                var a = _grid[st.Controlled[0].Row, st.Controlled[0].Col];
                var b = _grid[st.Controlled[1].Row, st.Controlled[1].Col];
                Debug.Assert(a.Symbol == b.Symbol);
            }

            foreach (var p in st.Controlled)
                Debug.Assert(occupied.Add((p.Row, p.Col)));
        }

        for (int r = 0; r < _rows; r++)
        for (int c = 0; c < _cols; c++)
        {
            var s = _grid[r, c];
            if (s.IsEmpty)
                Debug.Assert(!s.FaceUp && s.ControlledBy is null);
            else if (s.ControlledBy != null)
                Debug.Assert(s.FaceUp);
        }
    }

    // ========== Constructor ==========

    /// <summary>
    /// Creates a Board with the specified dimensions and card symbols.
    /// </summary>
    /// <param name="rows">int, number of rows in the board</param>
    /// <param name="cols">int, number of columns in the board</param>
    /// <param name="symbols">IReadOnlyList&lt;string&gt;, list of card symbols, reading left-to-right, top-to-bottom</param>
    /// <remarks>
    /// Preconditions (requires):
    ///   • rows > 0 and cols > 0
    ///   • symbols.Count == rows * cols
    ///   • each symbol in symbols is a non-null string
    /// Postconditions (effects):
    ///   • Creates a new Board with dimensions rows × cols
    ///   • All cards are face down and uncontrolled
    ///   • Card at position (r, c) has symbol symbols[r * cols + c]
    /// Throws:
    ///   • ArgumentException if preconditions are violated
    /// </remarks>
    private Board(int rows, int cols, IReadOnlyList<string> symbols)
    {
        if (rows <= 0 || cols <= 0)
            throw new ArgumentException("Board dimensions must be positive.");
        if (symbols.Count != rows * cols)
            throw new ArgumentException($"Expected {rows * cols} cards but got {symbols.Count}.");

        _rows = rows;
        _cols = cols;
        _grid = new CardSlot[rows, cols];

        int idx = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                _grid[r, c] = new CardSlot
                {
                    Symbol = symbols[idx++],
                    FaceUp = false,
                    ControlledBy = null
                };
            }
        }

        checkRep();
    }

    // ========== Factory Method ==========

    /// <summary>
    /// Parses a board file and creates a new Board.
    /// </summary>
    /// <param name="filename">string, path to the board file</param>
    /// <returns>Board, a new Board with cards from the file</returns>
    /// <remarks>
    /// Preconditions (requires):
    ///   • filename is a valid file path
    ///   • file exists and is readable
    ///   • file format: first line "ROWSxCOLS", followed by ROWS*COLS lines of card symbols
    ///   • each card symbol is non-empty, contains no whitespace
    /// Postconditions (effects):
    ///   • Returns a new Board initialized from the file
    ///   • All cards are face down and uncontrolled
    /// Throws:
    ///   • InvalidBoardFileException if file is not found, cannot be read, or has invalid format
    /// </remarks>
    public static Board ParseFromFile(string filename)
    {
        try
        {
            string content = File.ReadAllText(filename);
            string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            // Remove any trailing empty lines
            while (lines.Length > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines = lines[..^1];
            }

            if (lines.Length < 2)
                throw new InvalidBoardFileException("Board file must have at least 2 lines (dimensions + cards).");

            // Parse first line: "ROWSxCOLS"
            var dimensionMatch = Regex.Match(lines[0], @"^(\d+)x(\d+)$");
            if (!dimensionMatch.Success)
                throw new InvalidBoardFileException($"Invalid dimension line: '{lines[0]}'. Expected format: ROWSxCOLS");

            int rows = int.Parse(dimensionMatch.Groups[1].Value);
            int cols = int.Parse(dimensionMatch.Groups[2].Value);

            if (rows <= 0 || cols <= 0)
                throw new InvalidBoardFileException($"Board dimensions must be positive: {rows}x{cols}");

            int expectedCards = rows * cols;
            int actualCards = lines.Length - 1; // Exclude first line

            if (actualCards != expectedCards)
                throw new InvalidBoardFileException(
                    $"Expected {expectedCards} cards for {rows}x{cols} board, but found {actualCards} cards.");

            // Parse card symbols (lines 1 through end)
            var symbols = new List<string>();
            for (int i = 1; i < lines.Length; i++)
            {
                string card = lines[i];

                // Validate card: non-empty, no whitespace, no newlines
                if (string.IsNullOrWhiteSpace(card))
                    throw new InvalidBoardFileException($"Card at line {i + 1} is empty or whitespace.");
                if (card.Any(char.IsWhiteSpace))
                    throw new InvalidBoardFileException($"Card at line {i + 1} contains whitespace: '{card}'");

                symbols.Add(card);
            }

            return new Board(rows, cols, symbols);
        }
        catch (FileNotFoundException)
        {
            throw new InvalidBoardFileException($"Board file not found: {filename}");
        }
        catch (IOException ex)
        {
            throw new InvalidBoardFileException($"Error reading board file: {ex.Message}");
        }
    }

    // ========== Public Methods ==========

    /* PROBLEM 2: SYNCHRONOUS VERSION (commented out for Problem 3) 
    /// <summary>
    /// Attempt to flip a card at the specified position for the given player.
    /// Follows the Memory Scramble game rules for first/second card flips and cleanup.
    /// THIS WAS THE SYNCHRONOUS VERSION - replaced with async version in Problem 3
    /// </summary>
    public FlipOutcome Flip(string playerId, Position position)
    {
        // Validate position
        if (position.Row < 0 || position.Row >= _rows || position.Col < 0 || position.Col >= _cols)
            throw new InvalidPositionException(position);

        // Get or create player state
        if (!_players.ContainsKey(playerId))
            _players[playerId] = new PlayerState();

        var player = _players[playerId];
        var slot = _grid[position.Row, position.Col];

        // Determine if this is a first or second card flip
        bool isFirstCard = player.Controlled.Count == 0;

        if (isFirstCard)
        {
            // Before flipping a new first card, handle cleanup from previous turn (Rule 3)
            HandleCleanup(player);

            // Now flip the first card (Rule 1)
            return FlipFirstCard(playerId, player, position, slot);
        }
        else
        {
            // Flip second card (Rule 2)
            return FlipSecondCard(playerId, player, position, slot);
        }
    }
    END SYNCHRONOUS VERSION -------- */

    //  PROBLEM 3: ASYNCHRONOUS VERSION WITH WAITING SUPPORT 
    /// <summary>
    /// Attempt to flip a card at the specified position for the given player.
    /// Follows the Memory Scramble game rules for first/second card flips and cleanup.
    /// Supports concurrent players with proper waiting when cards are controlled.
    /// </summary>
    /// <param name="playerId">string, the ID of the player making the flip</param>
    /// <param name="position">Position, the board position (row, col) of the card to flip</param>
    /// <returns>Task&lt;FlipOutcome&gt;, async task resolving to the outcome of the flip attempt</returns>
    /// <remarks>
    /// Preconditions (requires):
    ///   • playerId is non-null
    ///   • position is within board bounds (0 ≤ position.Row &lt; rows, 0 ≤ position.Col &lt; cols)
    /// Postconditions (effects):
    ///   First card (player controls 0 or 2 cards):
    ///     • Rule 3: Handles cleanup from previous turn (removes matched pairs, flips down non-matched)
    ///     • Rule 1-A: If card is face down and uncontrolled → flips it face up, player controls it
    ///     • Rule 1-B: If card is face up but uncontrolled → player controls it (no visual change)
    ///     • Rule 1-C: If card is empty → returns FailNoCard
    ///     • Rule 1-D: If card is controlled by another player → waits until released, then takes control
    ///   Second card (player controls 1 card):
    ///     • Rule 2-A: If position is empty → returns FailNoCard, releases first card
    ///     • Rule 2-B: If card controlled by any player → returns FailControlled, releases first card
    ///     • Rule 2-C: If cards match → keeps control of both, returns SecondMatch
    ///     • Rule 2-D: If cards don't match → releases both (leaves face up), returns SecondNoMatch
    ///   Concurrency:
    ///     • Multiple players can flip concurrently (thread-safe with locks)
    ///     • Same player's flips are serialized (per-player semaphore prevents overlapping flips)
    ///     • Waiting players are queued and notified when cards become available
    /// Throws:
    ///   • InvalidPositionException if position is outside board bounds
    /// </remarks>
    public async Task<FlipOutcome> FlipAsync(string playerId, Position position)
    {
        // Per-player serialization: prevent overlapping flips from the same player
        SemaphoreSlim playerSemaphore;
        lock (_lock)
        {
            if (!_playerLocks.TryGetValue(playerId, out playerSemaphore!))
            {
                playerSemaphore = new SemaphoreSlim(1, 1);
                _playerLocks[playerId] = playerSemaphore;
            }
        }

        await playerSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            // Validate position
            if (position.Row < 0 || position.Row >= _rows || position.Col < 0 || position.Col >= _cols)
                throw new InvalidPositionException(position);

            PlayerState player;
            bool isFirstCard;
            
            // ALL player state access must be inside lock to prevent race conditions
            lock (_lock)
            {
                // Get or create player state
                if (!_players.ContainsKey(playerId))
                    _players[playerId] = new PlayerState();
                
                player = _players[playerId];
                
                // Determine if this is a first or second card flip
                // First card if: no cards controlled (0) OR matched pair controlled (2)
                isFirstCard = player.Controlled.Count != 1;
                
                // If first card, handle cleanup from previous turn (Rule 3)
                if (isFirstCard)
                {
                    HandleCleanup(player);
                }
            }

            if (isFirstCard)
            {
                // Now flip the first card (Rule 1) - may need to wait
                return await FlipFirstCardAsync(playerId, player, position);
            }
            else
            {
                // Flip second card (Rule 2)
                lock (_lock)
                {
                    return FlipSecondCard(playerId, player, position, _grid[position.Row, position.Col]);
                }
            }
        }
        finally
        {
            playerSemaphore.Release();
        }
    }

    /// <summary>
    /// Handles cleanup before a player flips a new first card (Rule 3-A and 3-B).
    /// PROBLEM 3: Now notifies waiting players when cards are released.
    /// </summary>
    /// <param name="player">PlayerState, the player's current state</param>
    /// <remarks>
    /// Preconditions (requires):
    ///   • player is non-null
    ///   • must be called inside _lock
    /// Postconditions (effects):
    ///   Rule 3-A (MatchHeld):
    ///     • Removes both matched cards from the board (sets Symbol to null)
    ///     • Wakes all waiting players for those positions (they will get FailNoCard)
    ///     • Notifies watchers of board change
    ///   Rule 3-B (NoMatchShown):
    ///     • Flips face-down any unmatched cards that are face-up and uncontrolled
    ///     • Wakes one waiting player per position (FIFO fairness)
    ///     • Notifies watchers if any cards were flipped
    ///   • Resets player.Pending to Unfinished
    ///   • Clears player.Controlled and player.LastShown as appropriate
    /// </remarks>
    private void HandleCleanup(PlayerState player)
    {
        if (player.Pending == PlayerTurnStatus.MatchHeld)
        {
            // Rule 3-A: Remove matched cards from the board
            foreach (var pos in player.Controlled)
            {
                var slot = _grid[pos.Row, pos.Col];
                slot.Symbol = null; // Remove card
                slot.FaceUp = false;
                slot.ControlledBy = null;
                
                // PROBLEM 3: Card removed: wake all waiters so they don't hang
                NotifyWaitingPlayers(slot, notifyAll: true);
            }
            player.Controlled.Clear();
            
            // PROBLEM 5: Notify watchers that cards were removed
            NotifyWatchers();
        }
        else if (player.Pending == PlayerTurnStatus.NoMatchShown)
        {
            // Rule 3-B: Turn face-down any unmatched cards that are still face-up and not controlled
            bool anyFlipped = false;
            foreach (var pos in player.LastShown)
            {
                var slot = _grid[pos.Row, pos.Col];
                // Only turn face-down if still on board, face-up, and not controlled by another player
                if (!slot.IsEmpty && slot.FaceUp && slot.ControlledBy == null)
                {
                    slot.FaceUp = false;
                    anyFlipped = true;
                    // PROBLEM 3: Normal availability: wake one waiter to preserve FIFO fairness
                    NotifyWaitingPlayers(slot);
                }
            }
            player.LastShown.Clear();
            
            // PROBLEM 5: Notify watchers if any cards were flipped face down
            if (anyFlipped)
            {
                NotifyWatchers();
            }
        }

        player.Pending = PlayerTurnStatus.Unfinished;
        checkRep();
    }

    /* PROBLEM 2: SYNCHRONOUS FlipFirstCard (commented out for Problem 3)
    /// <summary>
    /// Handles flipping the first card in a pair (Rule 1).
    /// THIS WAS THE SYNCHRONOUS VERSION - replaced with async version in Problem 3
    /// </summary>
    private FlipOutcome FlipFirstCard(string playerId, PlayerState player, Position position, CardSlot slot)
    {
        // Rule 1-A: No card at this position
        if (slot.IsEmpty)
        {
            checkRep();
            return FlipOutcome.FailNoCard;
        }

        // Rule 1-D: Card is controlled by another player
        // For synchronous version, we don't wait - we just fail
        if (slot.ControlledBy != null && slot.ControlledBy != playerId)
        {
            checkRep();
            return FlipOutcome.FailControlled;
        }

        // Rule 1-B: Card is face down - flip it up
        if (!slot.FaceUp)
        {
            slot.FaceUp = true;
        }

        // Rule 1-C: Card is already face up but not controlled - we can still control it
        slot.ControlledBy = playerId;
        player.Controlled.Add(position);

        checkRep();
        return FlipOutcome.FirstControlled;
    }
    END SYNCHRONOUS VERSION ------- */

    // PROBLEM 3: ASYNC FlipFirstCard WITH WAITING SUPPORT
    /// <summary>
    /// Handles flipping the first card in a pair (Rule 1).
    /// ASYNC VERSION: Waits if card is controlled by another player.
    /// </summary>
    /// <param name="playerId">string, ID of the player making the flip</param>
    /// <param name="player">PlayerState, the player's current state</param>
    /// <param name="position">Position, the board position to flip</param>
    /// <returns>Task&lt;FlipOutcome&gt;, async task resolving to the flip outcome</returns>
    /// <remarks>
    /// Preconditions (requires):
    ///   • playerId is non-null
    ///   • player is non-null and corresponds to playerId
    ///   • position is within board bounds
    ///   • player.Controlled.Count == 0 or 2 (first card scenario)
    /// Postconditions (effects):
    ///   • Rule 1-C: If position is empty → returns FailNoCard
    ///   • Rule 1-D: If card controlled by another player → waits until released, then retries
    ///   • Rule 1-A: If card face down and available → flips it face up, takes control
    ///   • Rule 1-B: If card face up and available → takes control (no visual change)
    ///   • On success: player.Controlled contains position, returns FirstControlled
    ///   • Notifies watchers if card was flipped face up
    /// Thread-safety:
    ///   • Waits outside lock (allows other operations)
    ///   • Uses while(true) loop to re-check after waking from wait
    /// </remarks>
    private async Task<FlipOutcome> FlipFirstCardAsync(string playerId, PlayerState player, Position position)
    {
        while (true)
        {
            CardSlot slot;
            TaskCompletionSource<bool>? waitTask = null;

            // Inspect state and decide whether to wait or take control
            lock (_lock)
            {
                slot = _grid[position.Row, position.Col];

                // Rule 1-A: No card at this position
                if (slot.IsEmpty)
                {
                    checkRep();
                    return FlipOutcome.FailNoCard;
                }

                // Rule 1-D: Card is controlled by another player → keep waiting
                if (slot.ControlledBy != null && slot.ControlledBy != playerId)
                {
                    waitTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    slot.WaitingQueue.Enqueue(waitTask);
                    // fall through to await outside lock
                }
                else
                {
                    // Card is available: flip up if needed and take control
                    bool wasFlipped = false;
                    if (!slot.FaceUp)
                    {
                        slot.FaceUp = true;
                        wasFlipped = true;
                    }

                    slot.ControlledBy = playerId;
                    player.Controlled.Add(position);

                    if (wasFlipped)
                    {
                        NotifyWatchers();
                    }

                    checkRep();
                    return FlipOutcome.FirstControlled;
                }
            }

            // If we need to wait, do it outside the lock, then loop and re-check
            if (waitTask != null)
            {
                await waitTask.Task.ConfigureAwait(false);
                // Loop to re-check the state and either take control or wait again
            }
        }
    }

    /// <summary>
    /// PROBLEM 3: Notifies waiting players that a card is now available.
    /// </summary>
    /// <param name="slot">CardSlot, the card slot to notify waiters for</param>
    /// <param name="notifyAll">bool, if true wakes all waiters; if false wakes only one (FIFO)</param>
    /// <remarks>
    /// Preconditions (requires):
    ///   • slot is non-null
    ///   • must be called inside _lock
    /// Postconditions (effects):
    ///   If notifyAll == false:
    ///     • Wakes one waiting player (FIFO fairness)
    ///     • Used when card becomes available normally (e.g., after non-match)
    ///   If notifyAll == true:
    ///     • Wakes ALL waiting players
    ///     • Used when card is removed (Rule 3-A) - all waiters should get FailNoCard
    ///   • Dequeued tasks have TrySetResult(true) called
    /// </remarks>
    private void NotifyWaitingPlayers(CardSlot slot, bool notifyAll = false)
    {
        // Notify waiting player(s)
        if (!notifyAll)
        {
            if (slot.WaitingQueue.Count > 0)
            {
                var nextWaiter = slot.WaitingQueue.Dequeue();
                nextWaiter.TrySetResult(true); // Wake up the next waiting player
            }
            return;
        }

        // notifyAll: wake everyone currently waiting on this slot
        while (slot.WaitingQueue.Count > 0)
        {
            var waiter = slot.WaitingQueue.Dequeue();
            waiter.TrySetResult(true);
        }
    }

    /// <summary>
    /// Handles flipping the second card in a pair (Rule 2).
    /// </summary>
    /// <param name="playerId">string, ID of the player making the flip</param>
    /// <param name="player">PlayerState, the player's current state</param>
    /// <param name="position">Position, the board position to flip</param>
    /// <param name="slot">CardSlot, the card slot at the position</param>
    /// <returns>FlipOutcome, the result of the second card flip</returns>
    /// <remarks>
    /// Preconditions (requires):
    ///   • playerId, player, slot are non-null
    ///   • position is within board bounds
    ///   • player.Controlled.Count == 1 (second card scenario)
    ///   • must be called inside _lock
    /// Postconditions (effects):
    ///   • Rule 2-A: If position is empty → relinquishes first card, returns FailNoCard
    ///   • Rule 2-B: If card controlled → relinquishes first card, returns FailControlled
    ///   • Rule 2-C: Flips card face up if needed
    ///   • Rule 2-D (match): Both cards remain controlled, returns SecondMatch
    ///   • Rule 2-E (no match): Releases both cards (leaves face up), returns SecondNoMatch
    ///   • Notifies watchers if card was flipped face up
    ///   • Notifies waiting players when cards are released (Rule 2-E)
    /// </remarks>
    private FlipOutcome FlipSecondCard(string playerId, PlayerState player, Position position, CardSlot slot)
    {
        // Rule 2-A: No card at this position
        if (slot.IsEmpty)
        {
            // Fail and relinquish control of first card
            RelinquishControl(player);
            checkRep();
            return FlipOutcome.FailNoCard;
        }

        // Rule 2-B: Card is controlled by a player (including ourselves)
        if (slot.ControlledBy != null)
        {
            // Fail and relinquish control of first card
            RelinquishControl(player);
            checkRep();
            return FlipOutcome.FailControlled;
        }

        // Rule 2-C: Flip card face up if needed
        bool wasFlipped = false;
        if (!slot.FaceUp)
        {
            slot.FaceUp = true;
            wasFlipped = true;
        }

        // PROBLEM 5: Notify watchers if card was flipped face up
        if (wasFlipped)
        {
            NotifyWatchers();
        }

        // Now check if the cards match
        var firstPos = player.Controlled[0];
        var firstSlot = _grid[firstPos.Row, firstPos.Col];
        bool isMatch = firstSlot.Symbol == slot.Symbol;

        if (isMatch)
        {
            // Rule 2-D: Cards match! Keep control of both
            slot.ControlledBy = playerId;
            player.Controlled.Add(position);
            player.Pending = PlayerTurnStatus.MatchHeld;

            checkRep();
            return FlipOutcome.SecondMatch;
        }
        else
        {
            // Rule 2-E: Cards don't match - relinquish control but leave face up
            player.LastShown.Add(firstPos);
            player.LastShown.Add(position);
            firstSlot.ControlledBy = null;
            
            // CRITICAL: Clear player's Controlled list BEFORE notifying waiting players
            // Otherwise, the waiting player might take control while this player still
            // has the card in their Controlled list, causing two players to control the same card!
            player.Controlled.Clear();
            player.Pending = PlayerTurnStatus.NoMatchShown;
            
            // PROBLEM 3: Normal availability after releasing: wake one waiter
            NotifyWaitingPlayers(firstSlot);

            checkRep();
            return FlipOutcome.SecondNoMatch;
        }
    }

    /// <summary>
    /// Helper to relinquish control of all cards (but leave them face up).
    /// PROBLEM 3: Now notifies waiting players when cards are released.
    /// </summary>
    /// <param name="player">PlayerState, the player whose control is being relinquished</param>
    /// <remarks>
    /// Preconditions (requires):
    ///   • player is non-null
    ///   • must be called inside _lock
    /// Postconditions (effects):
    ///   • Clears ControlledBy on all cards the player controls
    ///   • Moves controlled positions to player.LastShown
    ///   • Sets player.Pending to NoMatchShown
    ///   • Clears player.Controlled
    ///   • Notifies one waiting player per released card (FIFO fairness)
    ///   • Cards remain face up (not flipped down)
    /// Usage:
    ///   • Called when second flip fails (Rule 2-A, 2-B)
    /// </remarks>
    private void RelinquishControl(PlayerState player)
    {
        // First, collect positions and clear ControlledBy on slots
        var positions = new List<Position>(player.Controlled);
        foreach (var pos in positions)
        {
            var slot = _grid[pos.Row, pos.Col];
            slot.ControlledBy = null;
        }
        
        // CRITICAL: Update player state BEFORE notifying waiting players
        player.LastShown.AddRange(player.Controlled);
        player.Controlled.Clear();
        player.Pending = PlayerTurnStatus.NoMatchShown;
        
        // NOW notify waiting players (after player.Controlled is cleared)
        foreach (var pos in positions)
        {
            var slot = _grid[pos.Row, pos.Col];
            // Card just became uncontrolled; wake one waiter to contend now.
            NotifyWaitingPlayers(slot);
        }
    }

    /// <summary>
    /// Modifies the board by replacing every card with f(card), without affecting other state.
    /// This operation can interleave with other operations. The board remains observably 
    /// pairwise consistent: if two cards match before map(), they will always be observed 
    /// as matching during and after map().
    /// </summary>
    /// <param name="playerId">string, ID of player applying the map operation</param>
    /// <param name="transformer">Func&lt;string, Task&lt;string&gt;&gt;, pure async function mapping card symbols to new symbols</param>
    /// <returns>Task&lt;BoardState&gt;, async task resolving to the board state after transformation from playerId's perspective</returns>
    /// <remarks>
    /// Preconditions (requires):
    ///   • playerId is non-null
    ///   • transformer is a pure mathematical function (same input → same output)
    ///   • transformer does not throw exceptions
    /// Postconditions (effects):
    ///   • Every card symbol s on the board is replaced with transformer(s)
    ///   • Card state (face up/down, controlled/uncontrolled, empty) is unchanged
    ///   • If two cards had the same symbol before, they have the same symbol after
    ///   • Other operations (flip, look, watch, even other maps) can interleave during transformation
    ///   • Returns the updated board state from playerId's perspective
    ///   • Notifies watchers if any symbols changed
    /// Thread-safety:
    ///   • Transformer calls execute outside lock (allows interleaving)
    ///   • Symbol replacements applied atomically (inside lock)
    ///   • Pairwise consistency maintained: matching cards always observed as matching
    /// </remarks>
    public async Task<BoardState> MapAsync(string playerId, Func<string, Task<string>> transformer)
    {
        // So first 1: Collect all unique symbols on the board (quick operation with lock)
        HashSet<string> uniqueSymbols;
        lock (_lock)
        {
            uniqueSymbols = new HashSet<string>();
            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    var slot = _grid[r, c];
                    if (!slot.IsEmpty)
                    {
                        uniqueSymbols.Add(slot.Symbol!);
                    }
                }
            }
        }
        // Lock released! Other operations can now interleave.

        // Then 2: Transform each unique symbol (slow operation WITHOUT lock)
        var transformations = new Dictionary<string, string>();
        foreach (var symbol in uniqueSymbols)
        {
            var newSymbol = await transformer(symbol);
            transformations[symbol] = newSymbol;
        }
        // Other operations (flip, look, even other map) could have interleaved during awaits!

        // Then 3: Apply all transformations atomically (quick operation with lock)
        lock (_lock)
        {
            bool anyChanged = false;
            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    var slot = _grid[r, c];
                    // Only transform if card still exists and we have a transformation for it
                    if (!slot.IsEmpty && transformations.ContainsKey(slot.Symbol!))
                    {
                        slot.Symbol = transformations[slot.Symbol!];
                        anyChanged = true;
                    }
                }
            }
            
            // PROBLEM 5: Notify watchers if any symbols changed
            if (anyChanged)
            {
                NotifyWatchers();
            }
            
            checkRep();
        }

        // Finally 4: Return the updated board state from this player's perspective
        return ViewBy(playerId);
    }

    // PROBLEM 5: WATCH FUNCTIONALITY 
    
    /// <summary>
    /// Waits asynchronously until the board changes.
    /// A change is defined as: cards flip up/down, removed, or symbols change.
    /// Control changes (take/release without flipping) do NOT trigger this.
    /// </summary>
    /// <param name="playerId">string, ID of the player watching for changes</param>
    /// <returns>Task, async task that completes when the board changes (or immediately if already changed since last watch)</returns>
    /// <remarks>
    /// Preconditions (requires):
    ///   • playerId is non-null
    /// Postconditions (effects):
    ///   • If board version has changed since player's last watch → returns immediately
    ///   • Otherwise → waits until next visible board change (flip up/down, removal, map)
    ///   • Updates player's last-seen version to current version
    ///   • Does NOT consider control changes (take/release) as visible changes
    /// Versioned Watch Mechanism:
    ///   • Board maintains monotonic version counter (_version)
    ///   • Each player tracks their last-delivered version (_playerLastVersion)
    ///   • Prevents "gap" bug: changes between watch requests are never missed
    ///   • Multiple watchers all notified simultaneously on any change
    /// Thread-safety:
    ///   • Waits outside lock to allow other operations to proceed
    ///   • Version checks and updates are atomic (inside lock)
    /// </remarks>
    public async Task WaitForChangeAsync(string playerId)
    {
        TaskCompletionSource<long> watcher;
        lock (_lock)
        {
            // Initialize last version for this player if first time
            if (!_playerLastVersion.TryGetValue(playerId, out var last))
            {
                _playerLastVersion[playerId] = _version;
                last = _version;
            }

            // If a newer version already exists, return immediately (no gap)
            if (_version > last)
            {
                _playerLastVersion[playerId] = _version;
                return;
            }

            // Otherwise, wait for the next version
            watcher = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            _watchers.Add(watcher);
        }
        // Wait outside the lock - this allows other operations to proceed
        var newVersion = await watcher.Task.ConfigureAwait(false);
        lock (_lock)
        {
            _playerLastVersion[playerId] = newVersion;
        }
    }

    /// <summary>
    /// Notifies all waiting watchers that the board has changed.
    /// Must be called inside a lock(_lock) block.
    /// </summary>
    /// <remarks>
    /// Preconditions (requires):
    ///   • must be called inside _lock
    /// Postconditions (effects):
    ///   • Increments board version (_version++)
    ///   • Notifies all waiting watchers by calling TrySetResult(newVersion)
    ///   • Clears the _watchers list
    /// Usage:
    ///   • Called on any visible board change (card flip, removal, transformation)
    ///   • NOT called on control changes without flipping
    /// </remarks>
    private void NotifyWatchers()
    {
        // Bump board version on any visible change
        _version++;
        var newVersion = _version;

        foreach (var watcher in _watchers)
        {
            watcher.TrySetResult(newVersion);
        }
        _watchers.Clear();
    }

    /// <summary>
    /// Flips a card and returns the board state, with error handling.
    /// This is a convenience method specifically for the Commands module to keep it as simple glue code.
    /// </summary>
    /// <param name="playerId">string, the ID of the player making the flip</param>
    /// <param name="position">Position, the board position (row, col) of the card to flip</param>
    /// <returns>Task&lt;BoardState&gt;, async task resolving to the board state after the flip from the player's perspective</returns>
    /// <remarks>
    /// Preconditions (requires):
    ///   • playerId is non-null
    ///   • position is within board bounds
    /// Postconditions (effects):
    ///   • Attempts to flip card at position using FlipAsync
    ///   • If flip succeeds (FirstControlled, SecondMatch, SecondNoMatch) → returns board state
    ///   • If flip fails (FailNoCard, FailControlled) → throws InvalidOperationException
    /// Throws:
    ///   • InvalidOperationException if flip fails (FailNoCard or FailControlled)
    ///   • InvalidPositionException if position is outside board bounds
    /// </remarks>
    public async Task<BoardState> FlipAndViewAsync(string playerId, Position position)
    {
        var outcome = await FlipAsync(playerId, position);
        if (outcome == FlipOutcome.FailNoCard || outcome == FlipOutcome.FailControlled)
            throw new InvalidOperationException($"Flip failed: {outcome}");
        return ViewBy(playerId);
    }

    /// <summary>
    /// Returns the current state of the board from a specific player's perspective.
    /// </summary>
    /// <param name="playerId">string, the ID of the player viewing the board</param>
    /// <returns>BoardState, immutable snapshot showing what this player can see</returns>
    /// <remarks>
    /// Preconditions (requires):
    ///   • playerId is non-null
    /// Postconditions (effects):
    ///   • Returns an immutable BoardState with:
    ///     - Dimensions (rows × cols)
    ///     - Array of spot descriptions, one per position (left-to-right, top-to-bottom):
    ///       • "none" if card removed
    ///       • "down" if card face down
    ///       • "my [symbol]" if card face up and controlled by this player
    ///       • "up [symbol]" if card face up but not controlled by this player
    ///   • Does not modify board state
    /// Thread-safety:
    ///   • Thread-safe (acquires lock during read)
    ///   • Returns immutable snapshot (safe from rep exposure)
    /// </remarks>
    public BoardState ViewBy(string playerId)
    {
        // Must lock when reading shared state to prevent race conditions
        lock (_lock)
        {
            var spots = new List<string>();

            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    var slot = _grid[r, c];

                    if (slot.IsEmpty)
                    {
                        spots.Add("none");
                    }
                    else if (!slot.FaceUp)
                    {
                        spots.Add("down");
                    }
                    else if (slot.ControlledBy == playerId)
                    {
                        // Face-up card controlled by this player
                        spots.Add($"my {slot.Symbol}");
                    }
                    else
                    {
                        // Face-up card not controlled by this player (or controlled by no one)
                        spots.Add($"up {slot.Symbol}");
                    }
                }
            }

            checkRep();
            return new BoardState(_rows, _cols, spots);
        }
    }

    /// <summary>
    /// Returns a string representation of the board for debugging.
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Board {_rows}x{_cols}:");
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                var slot = _grid[r, c];
                if (slot.IsEmpty)
                    sb.Append("[ empty ] ");
                else if (slot.FaceUp)
                    sb.Append($"[{slot.Symbol,5}↑] ");
                else
                    sb.Append($"[{slot.Symbol,5}↓] ");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

#endregion
