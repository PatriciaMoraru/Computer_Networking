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

string projectRoot = Path.Combine(Directory.GetCurrentDirectory(), "..", "..");
string fullBoardPath = Path.GetFullPath(Path.Combine(projectRoot, boardFile));


Console.WriteLine($"Loading board from: {fullBoardPath}");
Board board;
board = Board.ParseFromFile(fullBoardPath);
Console.WriteLine($"Board loaded successfully!");

// Build the web application
var builder = WebApplication.CreateBuilder();

// Add CORS 
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

app.UseCors();

// Serve static files from public/ folder
string publicPath = Path.GetFullPath(Path.Combine(projectRoot, "public"));
Console.WriteLine($"Serving static files from: {publicPath}");
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicPath),
    RequestPath = ""
});

// Memory Scramble HTTP Endpoints

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

// GET /map/{playerId}/{fromCard}/{toCard}
// PROBLEM 4: Replaces all instances of fromCard with toCard on the board
// This is the map() command specified in the MIT assignment
app.MapGet("/map/{playerId}/{fromCard}/{toCard}", async (string playerId, string fromCard, string toCard) =>
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

// GET /reset
// Resets the board to its initial state by reloading from file
app.MapGet("/reset", () =>
{
    try
    {
        board = Commands.Reset(fullBoardPath);
        Console.WriteLine($"Board reset successfully!");
        return Results.Ok("Board reset successfully");
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
Console.WriteLine($"  GET /map/{{playerId}}/{{fromCard}}/{{toCard}} - Replace cards (Problem 4)");
Console.WriteLine($"  GET /watch/{{playerId}} - Watch for board changes (Problem 5)");
Console.WriteLine($"  GET /reset - Reset the board to initial state");
Console.WriteLine($"\nPress Ctrl+C to stop the server.\n");

app.Run($"http://localhost:{port}");