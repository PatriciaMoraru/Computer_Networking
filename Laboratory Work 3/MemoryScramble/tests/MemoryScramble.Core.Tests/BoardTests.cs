using System.Diagnostics;
using System.Text;
using Xunit;
using MemoryScramble.Core;

namespace MemoryScramble.Core.Tests;

public class BoardTests
{
    private const string PerfectBoardPath = "../../../../../boards/perfect.txt";

    [Fact]
    public void TestParseFromFile_ValidBoard()
    {
        // Parse the perfect.txt board (3x3 with unicorns and rainbows)
        var board = Board.ParseFromFile(PerfectBoardPath);
        Assert.NotNull(board);

        // View the board - should show all cards face down
        var state = board.ViewBy("player1");
        Assert.Equal(3, state.Rows);
        Assert.Equal(3, state.Cols);
        Assert.Equal(9, state.Spots.Count);
        
        // All cards should be face down initially
        Assert.All(state.Spots, spot => Assert.Equal("down", spot));
    }

    [Fact]
    public void TestFlipFirstCard()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Player flips their first card at position (0, 0)
        var outcome = board.Flip("alice", new Position(0, 0));
        Assert.Equal(FlipOutcome.FirstControlled, outcome);

        // View the board - should see one card controlled by alice
        var state = board.ViewBy("alice");
        Assert.StartsWith("my", state.Spots[0]); // First card is "my X"
        
        // Other player views the board - should see it as "up"
        var bobState = board.ViewBy("bob");
        Assert.StartsWith("up", bobState.Spots[0]); // First card is "up X" for Bob
    }

    [Fact]
    public void TestFlipSecondCard_Match()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Looking at perfect.txt: positions (0,0) and (0,1) both have 🦄
        // Player flips first card
        board.Flip("alice", new Position(0, 0));
        
        // Player flips second card - should match!
        var outcome = board.Flip("alice", new Position(0, 1));
        Assert.Equal(FlipOutcome.SecondMatch, outcome);

        // Both cards should be controlled by alice
        var state = board.ViewBy("alice");
        Assert.StartsWith("my", state.Spots[0]); // (0,0)
        Assert.StartsWith("my", state.Spots[1]); // (0,1)
    }

    [Fact]
    public void TestFlipSecondCard_NoMatch()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Looking at perfect.txt: (0,0) is 🦄 and (0,2) is 🌈 - they don't match
        board.Flip("alice", new Position(0, 0));
        var outcome = board.Flip("alice", new Position(0, 2));
        
        Assert.Equal(FlipOutcome.SecondNoMatch, outcome);

        // Both cards should be face up but not controlled
        var state = board.ViewBy("alice");
        Assert.StartsWith("up", state.Spots[0]); // No longer controlled
        Assert.StartsWith("up", state.Spots[2]);
    }

    [Fact]
    public void TestCleanup_RemoveMatchedCards()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Alice makes a match
        board.Flip("alice", new Position(0, 0)); // 🦄
        board.Flip("alice", new Position(0, 1)); // 🦄 - match!

        // Alice flips a new first card - this should remove the matched cards
        board.Flip("alice", new Position(1, 0));

        var state = board.ViewBy("alice");
        Assert.Equal("none", state.Spots[0]); // Removed
        Assert.Equal("none", state.Spots[1]); // Removed
        Assert.StartsWith("my", state.Spots[3]); // New card at (1,0)
    }

    [Fact]
    public void TestCleanup_TurnDownNonMatched()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Alice flips two non-matching cards
        board.Flip("alice", new Position(0, 0)); // 🦄
        board.Flip("alice", new Position(0, 2)); // 🌈 - no match

        // Cards should be face up
        var state1 = board.ViewBy("alice");
        Assert.StartsWith("up", state1.Spots[0]);
        Assert.StartsWith("up", state1.Spots[2]);

        // Alice flips a new first card - this should turn down the non-matched cards
        board.Flip("alice", new Position(1, 0));

        var state2 = board.ViewBy("alice");
        Assert.Equal("down", state2.Spots[0]); // Turned back down
        Assert.Equal("down", state2.Spots[2]); // Turned back down
    }

    [Fact]
    public void TestFlipEmptySpace_Fails()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Alice makes a match and removes cards
        board.Flip("alice", new Position(0, 0));
        board.Flip("alice", new Position(0, 1));
        board.Flip("alice", new Position(1, 0)); // Triggers cleanup, removes the match

        // Now try to flip the empty space
        var outcome = board.Flip("bob", new Position(0, 0));
        Assert.Equal(FlipOutcome.FailNoCard, outcome);
    }

    [Fact]
    public void TestToString_ShowsBoardState()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        var str = board.ToString();
        
        Assert.Contains("Board 3x3", str);
        Assert.NotEmpty(str);
    }

    [Fact]
    public void TestBoardState_ToProtocolString()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        var state = board.ViewBy("alice");
        var protocol = state.ToProtocolString();
        
        // Should start with "3x3\n"
        Assert.StartsWith("3x3\n", protocol);
        
        // Should have 10 lines total (1 dimension line + 9 cards)
        var lines = protocol.Split('\n');
        Assert.Equal(10, lines.Length); // Including empty last line
    }
}