using MemoryScramble;
using MemoryScramble.Core;
using Microsoft.AspNetCore.Http.HttpResults;

// Parse command-line arguments: port and board file
if (args.Length < 2)
{
    Console.WriteLine("Usage: dotnet run <port> <board-file>");
    Console.WriteLine("Example: dotnet run 8080 boards/perfect.txt");
    return;
}

int port = int.Parse(args[0]);
string boardFile = args[1];

// Resolve the board file path relative to the project root
string projectRoot = Path.Combine(Directory.GetCurrentDirectory(), "..", "..");
string fullBoardPath = Path.GetFullPath(Path.Combine(projectRoot, boardFile));

// Load the board
Console.WriteLine($"Loading board from: {fullBoardPath}");
Board board = Board.ParseFromFile(fullBoardPath);
Console.WriteLine($"Board loaded successfully!");

// Build the web application
var builder = WebApplication.CreateBuilder();

// Add CORS to allow browser clients
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Enable CORS
app.UseCors();

// Serve static files from public/ folder
string publicPath = Path.GetFullPath(Path.Combine(projectRoot, "public"));
Console.WriteLine($"Serving static files from: {publicPath}");
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicPath),
    RequestPath = ""
});

// === Memory Scramble HTTP Endpoints ===

// GET /look/{playerId}
// Returns the board state from the player's perspective
app.MapGet("/look/{playerId}", (string playerId) =>
{
    try
    {
        string boardState = Commands.Look(board, playerId);
        return Results.Text(boardState, "text/plain");
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// GET /flip/{playerId}/{row},{col}
// Flips a card and returns the updated board state
// PROBLEM 3: Now uses async FlipAsync for proper waiting support
app.MapGet("/flip/{playerId}/{position}", async (string playerId, string position) =>
{
    try
    {
        // Parse "row,col" format
        var parts = position.Split(',');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int row) || !int.TryParse(parts[1], out int col))
        {
            return Results.BadRequest("Position must be in format 'row,col'");
        }

        // PROBLEM 3: Use async FlipAsync which supports waiting
        string boardState = await Commands.FlipAsync(board, playerId, row, col);
        return Results.Text(boardState, "text/plain");
    }
    catch (InvalidOperationException ex)
    {
        // Flip failed (no card or controlled by another)
        return Results.Conflict(ex.Message);
    }
    catch (InvalidPositionException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// GET /replace/{playerId}/{fromCard}/{toCard}
// Replaces all instances of fromCard with toCard on the board
// This is what the UI's "replace" button expects!
app.MapGet("/replace/{playerId}/{fromCard}/{toCard}", async (string playerId, string fromCard, string toCard) =>
{
    try
    {
        // Create a transformer function that replaces fromCard with toCard
        Func<string, Task<string>> transformer = async (card) =>
        {
            await Task.Delay(10); // Simulate async work
            return card == fromCard ? toCard : card;
        };

        string boardState = await Commands.MapAsync(board, playerId, transformer);
        return Results.Text(boardState, "text/plain");
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// GET /map/{playerId}?transform={transformType}
// Applies a transformation to all cards on the board
// PROBLEM 4: Implements map() with interleaving support
app.MapGet("/map/{playerId}", async (string playerId, string? transform) =>
{
    try
    {
        // Default transformation if none specified
        if (string.IsNullOrEmpty(transform))
        {
            transform = "uppercase";
        }

        // Create the transformer function based on the transform parameter
        Func<string, Task<string>> transformer = transform.ToLower() switch
        {
            "uppercase" => async (card) =>
            {
                await Task.Delay(100); // Simulate async work
                return card.ToUpper();
            },
            "lowercase" => async (card) =>
            {
                await Task.Delay(100); // Simulate async work
                return card.ToLower();
            },
            "reverse" => async (card) =>
            {
                await Task.Delay(100); // Simulate async work
                return new string(card.Reverse().ToArray());
            },
            "emoji" => async (card) =>
            {
                await Task.Delay(100); // Simulate async work
                // Simple mapping to emojis
                return card switch
                {
                    _ when card.Contains("sun") || card.Contains("SUN") => "☀️",
                    _ when card.Contains("rain") || card.Contains("RAIN") => "🌈",
                    _ when card.Contains("star") || card.Contains("STAR") => "⭐",
                    _ when card.Contains("heart") || card.Contains("HEART") => "❤️",
                    _ => "🎴"
                };
            },
            _ => async (card) =>
            {
                await Task.Delay(100);
                return card; // Identity transformation
            }
        };

        string boardState = await Commands.MapAsync(board, playerId, transformer);
        return Results.Text(boardState, "text/plain");
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// GET /watch/{playerId}
// PROBLEM 5: Waits for board changes and returns updated state
app.MapGet("/watch/{playerId}", async (string playerId) =>
{
    try
    {
        string boardState = await Commands.WatchAsync(board, playerId);
        return Results.Text(boardState, "text/plain");
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// Start the server
Console.WriteLine($"\nMemory Scramble Server");
Console.WriteLine($"Listening on http://localhost:{port}");
Console.WriteLine($"Open in browser: http://localhost:{port}/index.html");
Console.WriteLine($"\nAPI Endpoints:");
Console.WriteLine($"  GET /look/{{playerId}} - View the board");
Console.WriteLine($"  GET /flip/{{playerId}}/{{row}},{{col}} - Flip a card");
Console.WriteLine($"  GET /replace/{{playerId}}/{{fromCard}}/{{toCard}} - Replace cards (for UI)");
Console.WriteLine($"  GET /map/{{playerId}}?transform={{type}} - Transform all cards");
Console.WriteLine($"    Available transforms: uppercase, lowercase, reverse, emoji");
Console.WriteLine($"  GET /watch/{{playerId}} - Watch for board changes (Problem 5)");
Console.WriteLine($"\nPress Ctrl+C to stop the server.\n");

app.Run($"http://localhost:{port}");