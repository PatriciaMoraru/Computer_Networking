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

// Start the server
Console.WriteLine($"\nMemory Scramble Server");
Console.WriteLine($"Listening on http://localhost:{port}");
Console.WriteLine($"Open in browser: http://localhost:{port}/index.html");
Console.WriteLine($"\nPress Ctrl+C to stop the server.\n");

app.Run($"http://localhost:{port}");