using System.Diagnostics;

namespace MemoryScramble.Simulation;

/// <summary>
/// Fuzz testing simulation for Memory Scramble.
/// Simulates multiple players making random moves with random timeouts to stress-test
/// the concurrent game logic and detect race conditions or crashes.
/// </summary>
class Program
{
    private const int NUM_PLAYERS = 4;
    private const int MOVES_PER_PLAYER = 100;
    private const double MIN_DELAY_MS = 0.1;
    private const double MAX_DELAY_MS = 2.0;
    
    private static readonly HttpClient httpClient = new();
    private static int totalSuccesses = 0;
    private static int totalFailures = 0;
    private static int totalErrors = 0;
    private static readonly object statsLock = new();
    private static readonly List<PlayerStats> playerStatsList = new();

    static async Task Main(string[] args)
    {
        // Parse command-line arguments
        string serverUrl = args.Length > 0 ? args[0] : "http://localhost:8080";
        string boardFile = args.Length > 1 ? args[1] : "boards/zoom.txt";
        
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           Memory Scramble - Fuzz Testing Simulation           ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"Server URL:        {serverUrl}");
        Console.WriteLine($"Board:             {boardFile}");
        Console.WriteLine($"Players:           {NUM_PLAYERS}");
        Console.WriteLine($"Moves per player:  {MOVES_PER_PLAYER}");
        Console.WriteLine($"Total moves:       {NUM_PLAYERS * MOVES_PER_PLAYER}");
        Console.WriteLine($"Delay range:       {MIN_DELAY_MS}ms - {MAX_DELAY_MS}ms");
        Console.WriteLine();
        
        // Check server is running
        Console.Write("Checking server... ");
        try
        {
            var response = await httpClient.GetAsync($"{serverUrl}/look/test");
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✓ Server is running");
            }
            else
            {
                Console.WriteLine($"✗ Server returned status {response.StatusCode}");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Cannot connect to server: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Please start the server first:");
            Console.WriteLine($"  dotnet run --project src\\MemoryScramble.Server 8080 {boardFile}");
            return;
        }
        
        // Get board dimensions
        Console.Write("Reading board dimensions... ");
        var boardInfo = await GetBoardInfo(serverUrl);
        Console.WriteLine($"✓ {boardInfo.Rows}x{boardInfo.Cols} board");
        Console.WriteLine();
        
        // Start simulation
        Console.WriteLine("Starting simulation...");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();
        
        var stopwatch = Stopwatch.StartNew();
        
        // Spawn player tasks
        var playerTasks = new List<Task>();
        for (int i = 0; i < NUM_PLAYERS; i++)
        {
            string playerId = $"sim_player_{i}";
            var task = SimulatePlayer(playerId, serverUrl, boardInfo);
            playerTasks.Add(task);
        }
        
        // Wait for all players to finish
        await Task.WhenAll(playerTasks);
        
        stopwatch.Stop();
        
        // Print results
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("Simulation Complete!");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine($"Total duration:    {stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F2}s)");
        Console.WriteLine($"Total moves:       {NUM_PLAYERS * MOVES_PER_PLAYER}");
        Console.WriteLine($"Moves per second:  {(NUM_PLAYERS * MOVES_PER_PLAYER) / stopwatch.Elapsed.TotalSeconds:F0}");
        Console.WriteLine();
        Console.WriteLine($"✓ Successes:       {totalSuccesses}");
        Console.WriteLine($"✗ Failures:        {totalFailures}");
        Console.WriteLine($"⚠ Errors:          {totalErrors}");
        Console.WriteLine();
        
        // Print per-player statistics
        Console.WriteLine("───────────────────────────────────────────────────────────────");
        Console.WriteLine("Per-Player Statistics:");
        Console.WriteLine("───────────────────────────────────────────────────────────────");
        Console.WriteLine();
        
        var sortedStats = playerStatsList.OrderBy(s => s.Duration).ToList();
        foreach (var stats in sortedStats)
        {
            Console.WriteLine($"[{stats.PlayerId}]");
            Console.WriteLine($"  Moves:      {stats.TotalMoves}");
            Console.WriteLine($"  Duration:   {stats.Duration}ms ({stats.Duration / 1000.0:F2}s)");
            Console.WriteLine($"  Speed:      {stats.TotalMoves / (stats.Duration / 1000.0):F0} moves/sec");
            Console.WriteLine($"  Successes:  {stats.Successes}");
            Console.WriteLine($"  Failures:   {stats.Failures}");
            Console.WriteLine($"  Errors:     {stats.Errors}");
            Console.WriteLine();
        }
        
        Console.WriteLine("───────────────────────────────────────────────────────────────");
        
        if (totalErrors == 0)
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    ✓ ALL TESTS PASSED ✓                       ║");
            Console.WriteLine("║                   No crashes detected!                         ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        }
        else
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    ✗ ERRORS DETECTED ✗                        ║");
            Console.WriteLine("║              Server crashed or returned errors!                ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Simulates a single player making random moves with random delays.
    /// This is the core of the fuzz testing - each player operates independently
    /// with varying speeds to create race conditions.
    /// </summary>
    static async Task SimulatePlayer(string playerId, string serverUrl, BoardInfo board)
    {
        var random = new Random(playerId.GetHashCode()); // Seeded for reproducibility
        int successes = 0;
        int failures = 0;
        int errors = 0;
        
        Console.WriteLine($"[{playerId}] Starting simulation...");
        
        var playerStopwatch = Stopwatch.StartNew();
        
        for (int move = 0; move < MOVES_PER_PLAYER; move++)
        {
            try
            {
                // Random delay (0.1ms - 2ms) to create varying race conditions
                double delayMs = MIN_DELAY_MS + (random.NextDouble() * (MAX_DELAY_MS - MIN_DELAY_MS));
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs));
                
                // Pick a random position on the board
                int row = random.Next(0, board.Rows);
                int col = random.Next(0, board.Cols);
                
                // Flip the card
                var response = await httpClient.GetAsync($"{serverUrl}/flip/{playerId}/{row},{col}");
                
                if (response.IsSuccessStatusCode)
                {
                    successes++;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    // Flip failed (no card or controlled) - this is expected behavior
                    failures++;
                }
                else
                {
                    // Unexpected error
                    errors++;
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[{playerId}] ERROR: Status {response.StatusCode} at ({row},{col}): {errorBody}");
                }
            }
            catch (Exception ex)
            {
                errors++;
                Console.WriteLine($"[{playerId}] EXCEPTION: {ex.Message}");
            }
        }
        
        playerStopwatch.Stop();
        
        // Update global stats
        lock (statsLock)
        {
            totalSuccesses += successes;
            totalFailures += failures;
            totalErrors += errors;
            
            // Save per-player statistics
            playerStatsList.Add(new PlayerStats
            {
                PlayerId = playerId,
                TotalMoves = MOVES_PER_PLAYER,
                Duration = playerStopwatch.ElapsedMilliseconds,
                Successes = successes,
                Failures = failures,
                Errors = errors
            });
        }
        
        Console.WriteLine($"[{playerId}] Completed {MOVES_PER_PLAYER} moves in {playerStopwatch.ElapsedMilliseconds}ms: {successes} successes, {failures} failures, {errors} errors");
    }

    /// <summary>
    /// Gets the board dimensions from the server.
    /// </summary>
    static async Task<BoardInfo> GetBoardInfo(string serverUrl)
    {
        var response = await httpClient.GetAsync($"{serverUrl}/look/probe");
        var content = await response.Content.ReadAsStringAsync();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        if (lines.Length == 0)
        {
            throw new Exception("Empty board response");
        }
        
        var dimensions = lines[0].Split('x');
        return new BoardInfo
        {
            Rows = int.Parse(dimensions[0]),
            Cols = int.Parse(dimensions[1])
        };
    }

    /// <summary>
    /// Simple struct to hold board dimensions.
    /// </summary>
    record BoardInfo
    {
        public int Rows { get; init; }
        public int Cols { get; init; }
    }

    /// <summary>
    /// Statistics for a single player's performance.
    /// </summary>
    record PlayerStats
    {
        public required string PlayerId { get; init; }
        public required int TotalMoves { get; init; }
        public required long Duration { get; init; }
        public required int Successes { get; init; }
        public required int Failures { get; init; }
        public required int Errors { get; init; }
    }
}
