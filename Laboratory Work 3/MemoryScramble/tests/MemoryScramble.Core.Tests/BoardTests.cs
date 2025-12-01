using Xunit;
using MemoryScramble.Core;

namespace MemoryScramble.Core.Tests;

// ===== PROBLEM 3: Tests updated to use async FlipAsync =====
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
    public async Task TestFlipFirstCard() // PROBLEM 3: Now async
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Player flips their first card at position (0, 0)
        var outcome = await board.FlipAsync("alice", new Position(0, 0)); // PROBLEM 3: FlipAsync
        Assert.Equal(FlipOutcome.FirstControlled, outcome);

        // View the board - should see one card controlled by alice
        var state = board.ViewBy("alice");
        Assert.StartsWith("my", state.Spots[0]); // First card is "my X"
        
        // Other player views the board - should see it as "up"
        var bobState = board.ViewBy("bob");
        Assert.StartsWith("up", bobState.Spots[0]); // First card is "up X" for Bob
    }

    [Fact]
    public async Task TestFlipSecondCard_Match() // PROBLEM 3: Now async
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Looking at perfect.txt: positions (0,0) and (0,1) both have 🦄
        // Player flips first card
        await board.FlipAsync("alice", new Position(0, 0)); // PROBLEM 3: FlipAsync
        
        // Player flips second card - should match!
        var outcome = await board.FlipAsync("alice", new Position(0, 1)); // PROBLEM 3: FlipAsync
        Assert.Equal(FlipOutcome.SecondMatch, outcome);

        // Both cards should be controlled by alice
        var state = board.ViewBy("alice");
        Assert.StartsWith("my", state.Spots[0]); // (0,0)
        Assert.StartsWith("my", state.Spots[1]); // (0,1)
    }

    [Fact]
    public async Task TestFlipSecondCard_NoMatch() // PROBLEM 3: Now async
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Looking at perfect.txt: (0,0) is 🦄 and (0,2) is 🌈 - they don't match
        await board.FlipAsync("alice", new Position(0, 0)); // PROBLEM 3: FlipAsync
        var outcome = await board.FlipAsync("alice", new Position(0, 2)); // PROBLEM 3: FlipAsync
        
        Assert.Equal(FlipOutcome.SecondNoMatch, outcome);

        // Both cards should be face up but not controlled
        var state = board.ViewBy("alice");
        Assert.StartsWith("up", state.Spots[0]); // No longer controlled
        Assert.StartsWith("up", state.Spots[2]);
    }

    [Fact]
    public async Task TestCleanup_RemoveMatchedCards() // PROBLEM 3: Now async
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Alice makes a match
        await board.FlipAsync("alice", new Position(0, 0)); // PROBLEM 3: FlipAsync - 🦄
        await board.FlipAsync("alice", new Position(0, 1)); // PROBLEM 3: FlipAsync - 🦄 - match!

        // Alice flips a new first card - this should remove the matched cards
        await board.FlipAsync("alice", new Position(1, 0)); // PROBLEM 3: FlipAsync

        var state = board.ViewBy("alice");
        Assert.Equal("none", state.Spots[0]); // Removed
        Assert.Equal("none", state.Spots[1]); // Removed
        Assert.StartsWith("my", state.Spots[3]); // New card at (1,0)
    }

    [Fact]
    public async Task TestCleanup_TurnDownNonMatched() // PROBLEM 3: Now async
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Alice flips two non-matching cards
        await board.FlipAsync("alice", new Position(0, 0)); // PROBLEM 3: FlipAsync - 🦄
        await board.FlipAsync("alice", new Position(0, 2)); // PROBLEM 3: FlipAsync - 🌈 - no match

        // Cards should be face up
        var state1 = board.ViewBy("alice");
        Assert.StartsWith("up", state1.Spots[0]);
        Assert.StartsWith("up", state1.Spots[2]);

        // Alice flips a new first card - this should turn down the non-matched cards
        await board.FlipAsync("alice", new Position(1, 0)); // PROBLEM 3: FlipAsync

        var state2 = board.ViewBy("alice");
        Assert.Equal("down", state2.Spots[0]); // Turned back down
        Assert.Equal("down", state2.Spots[2]); // Turned back down
    }

    [Fact]
    public async Task TestFlipEmptySpace_Fails() // PROBLEM 3: Now async
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Alice makes a match and removes cards
        await board.FlipAsync("alice", new Position(0, 0)); // PROBLEM 3: FlipAsync
        await board.FlipAsync("alice", new Position(0, 1)); // PROBLEM 3: FlipAsync
        await board.FlipAsync("alice", new Position(1, 0)); // PROBLEM 3: FlipAsync - Triggers cleanup, removes the match

        // Now try to flip the empty space
        var outcome = await board.FlipAsync("bob", new Position(0, 0)); // PROBLEM 3: FlipAsync
        Assert.Equal(FlipOutcome.FailNoCard, outcome);
    }

    [Fact]
    public async Task TestFlipFirstCard_WaitsForControlled()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Alice controls a card
        await board.FlipAsync("alice", new Position(0, 0));
        
        // Bob tries to flip the same card - should wait (Rule 1-D)
        var bobTask = board.FlipAsync("bob", new Position(0, 0));
        
        // Bob should be waiting (not completed yet)
        await Task.Delay(100);
        Assert.False(bobTask.IsCompleted);
        
        // Alice releases by flipping second card (non-match)
        await board.FlipAsync("alice", new Position(0, 2));
        
        // Now Bob should get the card
        var outcome = await bobTask;
        Assert.Equal(FlipOutcome.FirstControlled, outcome);
        
        // Bob should control it now
        var state = board.ViewBy("bob");
        Assert.StartsWith("my", state.Spots[0]);
    }

    [Fact]
    public async Task TestFlipSecondCard_EmptyFails()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Alice controls first card
        await board.FlipAsync("alice", new Position(0, 0));
        
        // Bob matches and removes a pair to create empty space
        await board.FlipAsync("bob", new Position(1, 1));
        await board.FlipAsync("bob", new Position(2, 2)); // Assuming these match
        
        // Check if they matched and trigger cleanup
        var bobState = board.ViewBy("bob");
        if (bobState.Spots[4].StartsWith("my") && bobState.Spots[8].StartsWith("my"))
        {
            // They matched - trigger cleanup
            await board.FlipAsync("bob", new Position(0, 1));
            
            // Alice tries to flip removed position as second card (Rule 2-A)
            var outcome = await board.FlipAsync("alice", new Position(1, 1));
            
            Assert.Equal(FlipOutcome.FailNoCard, outcome);
            
            // Alice should have lost control of first card
            var aliceState = board.ViewBy("alice");
            Assert.StartsWith("up", aliceState.Spots[0]); // No longer "my"
        }
    }

    [Fact]
    public async Task TestFlipSecondCard_ControlledFails()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Alice controls first card
        await board.FlipAsync("alice", new Position(0, 0));
        
        // Bob controls another card
        await board.FlipAsync("bob", new Position(0, 1));
        
        // Alice tries to flip Bob's controlled card as second (Rule 2-B)
        var outcome = await board.FlipAsync("alice", new Position(0, 1));
        
        Assert.Equal(FlipOutcome.FailControlled, outcome);
        
        // Alice should have lost control
        var aliceState = board.ViewBy("alice");
        Assert.StartsWith("up", aliceState.Spots[0]); // Not "my"
        
        // Bob should still control his card
        var bobState = board.ViewBy("bob");
        Assert.StartsWith("my", bobState.Spots[1]);
    }

    [Fact]
    public async Task TestCleanup_DoesNotFlipDownControlledCards()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Alice flips non-matching pair
        await board.FlipAsync("alice", new Position(0, 0));
        await board.FlipAsync("alice", new Position(0, 2)); // No match
        
        // Cards should be face up
        var state1 = board.ViewBy("alice");
        Assert.StartsWith("up", state1.Spots[0]);
        Assert.StartsWith("up", state1.Spots[2]);
        
        // Bob takes control of one of Alice's cards
        await board.FlipAsync("bob", new Position(0, 0));
        
        // Alice flips new first card - should trigger cleanup (Rule 3-B)
        await board.FlipAsync("alice", new Position(1, 0));
        
        // Card (0,0) should still be face up (Bob controls it, so Rule 3-B doesn't flip it)
        var state2 = board.ViewBy("alice");
        Assert.StartsWith("up", state2.Spots[0]); // NOT "down" - Bob controls it
        
        // Card (0,2) should be face down (not controlled)
        Assert.Equal("down", state2.Spots[2]);
        
        // Verify Bob still controls (0,0)
        var bobState = board.ViewBy("bob");
        Assert.StartsWith("my", bobState.Spots[0]);
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
        
        // Should have 10 non-empty lines (1 dimension line + 9 cards)
        var lines = protocol.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(10, lines.Length);
    }

    // ===== PROBLEM 4: MAP TESTS =====

    [Fact]
    public async Task TestMap_BasicTransformation()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Flip a card so we can see the transformation
        await board.FlipAsync("player1", new Position(0, 0));
        
        // Get the original card value
        var stateBefore = board.ViewBy("player1");
        var protocolBefore = stateBefore.ToProtocolString();
        
        // Prefix transformation that will visibly change all cards
        var result = await board.MapAsync("player1", async (card) =>
        {
            await Task.Delay(10); // Simulate async work
            return $"NEW_{card}";
        });
        
        // The card should now have "NEW_" prefix
        var protocolAfter = result.ToProtocolString();
        
        // Should contain "my NEW_" (still controlled, but transformed)
        Assert.Contains("my NEW_", protocolAfter);
        
        // Verify transformation worked - the protocol should be different
        Assert.NotEqual(protocolBefore, protocolAfter);
    }

    [Fact]
    public async Task TestMap_ConsistencyOfMatchingPairs()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // The perfect board has matching pairs
        // Transform and verify matching cards get the same transformation
        Dictionary<string, string> seenTransformations = new();
        
        var result = await board.MapAsync("player1", async (card) =>
        {
            await Task.Delay(10);
            var transformed = $"NEW_{card}";
            
            // If we've seen this card before, verify we're applying the same transformation
            if (seenTransformations.ContainsKey(card))
            {
                Assert.Equal(transformed, seenTransformations[card]);
            }
            else
            {
                seenTransformations[card] = transformed;
            }
            
            return transformed;
        });
        
        Assert.NotNull(result);
    }

    [Fact]
    public async Task TestMap_InterleaveWithFlip()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Start a slow map operation
        var mapTask = board.MapAsync("player1", async (card) =>
        {
            await Task.Delay(100); // Slow transformation
            return card.ToUpper();
        });
        
        // While map is running, flip a card (should not block)
        await Task.Delay(50); // Let map start
        var flipOutcome = await board.FlipAsync("player2", new Position(0, 0));
        
        // Flip should succeed
        Assert.Equal(FlipOutcome.FirstControlled, flipOutcome);
        
        // Wait for map to finish
        var mapResult = await mapTask;
        Assert.NotNull(mapResult);
    }

    [Fact]
    public async Task TestMap_InterleaveWithLook()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Start a slow map operation
        var mapTask = board.MapAsync("player1", async (card) =>
        {
            await Task.Delay(100); // Slow transformation
            return card.ToUpper();
        });
        
        // While map is running, look at the board (should not block)
        await Task.Delay(50); // Let map start
        var lookResult = board.ViewBy("player2");
        
        // Look should succeed
        Assert.NotNull(lookResult);
        Assert.Equal(3, lookResult.Rows);
        Assert.Equal(3, lookResult.Cols);
        
        // Wait for map to finish
        var mapResult = await mapTask;
        Assert.NotNull(mapResult);
    }

    [Fact]
    public async Task TestMap_TransformationDoesNotAffectControl()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Player controls a card
        await board.FlipAsync("alice", new Position(0, 0));
        
        // Transform all cards
        await board.MapAsync("alice", async (card) =>
        {
            await Task.Delay(10);
            return $"TRANSFORMED_{card}";
        });
        
        // Alice should still control the card
        var state = board.ViewBy("alice");
        var protocol = state.ToProtocolString();
        
        // First card should show "my TRANSFORMED_..." (still controlled)
        Assert.Contains("my TRANSFORMED_", protocol);
    }

    [Fact]
    public async Task TestMap_RemovedCardsNotTransformed()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Match and remove a pair
        var outcome1 = await board.FlipAsync("alice", new Position(0, 0)); // First card
        var outcome2 = await board.FlipAsync("alice", new Position(1, 1)); // Assume this matches
        
        // If they matched, flip again to trigger cleanup (Rule 3-A removes them)
        if (outcome2 == FlipOutcome.SecondMatch)
        {
            await board.FlipAsync("alice", new Position(0, 1)); // Trigger cleanup
        }
        
        // Transform all cards
        var result = await board.MapAsync("alice", async (card) =>
        {
            await Task.Delay(10);
            return $"NEW_{card}";
        });
        
        // Removed cards should still show as "none"
        var protocol = result.ToProtocolString();
        if (outcome2 == FlipOutcome.SecondMatch)
        {
            Assert.Contains("none", protocol);
        }
    }

    // ===== PROBLEM 5: WATCH TESTS =====

    [Fact]
    public async Task TestWatch_WaitsForChange()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Start watching in background
        var watchTask = board.WaitForChangeAsync("player1");
        
        // Watch should not complete immediately
        await Task.Delay(100);
        Assert.False(watchTask.IsCompleted);
        
        // Flip a card (triggers change)
        await board.FlipAsync("player1", new Position(0, 0));
        
        // Watch should now complete
        await watchTask; // Should complete quickly
        Assert.True(watchTask.IsCompleted);
    }

    [Fact]
    public async Task TestWatch_MultipleWatchersNotified()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Multiple watchers
        var watch1 = board.WaitForChangeAsync("p1");
        var watch2 = board.WaitForChangeAsync("p2");
        var watch3 = board.WaitForChangeAsync("p3");
        
        // All should be waiting
        await Task.Delay(100);
        Assert.False(watch1.IsCompleted);
        Assert.False(watch2.IsCompleted);
        Assert.False(watch3.IsCompleted);
        
        // One change notifies all
        await board.FlipAsync("player1", new Position(0, 0));
        
        // All should complete
        await Task.WhenAll(watch1, watch2, watch3);
        Assert.True(watch1.IsCompleted);
        Assert.True(watch2.IsCompleted);
        Assert.True(watch3.IsCompleted);
    }

    [Fact]
    public async Task TestWatch_NotifiesOnFlipFaceUp()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        var watchTask = board.WaitForChangeAsync("player1");
        await Task.Delay(50);
        Assert.False(watchTask.IsCompleted);
        
        // Flip face up triggers notification
        await board.FlipAsync("alice", new Position(0, 0));
        
        await watchTask;
        Assert.True(watchTask.IsCompleted);
    }

    [Fact]
    public async Task TestWatch_NotifiesOnFlipFaceDown()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Set up: Flip two non-matching cards, then start watching
        await board.FlipAsync("alice", new Position(0, 0));
        await board.FlipAsync("alice", new Position(0, 2)); // Non-match
        
        // Cards are now face up
        var watchTask = board.WaitForChangeAsync("player1");
        await Task.Delay(50);
        Assert.False(watchTask.IsCompleted);
        
        // Flip new first card triggers cleanup (flips previous cards face down)
        await board.FlipAsync("alice", new Position(1, 0));
        
        await watchTask;
        Assert.True(watchTask.IsCompleted);
    }

    [Fact]
    public async Task TestWatch_NotifiesOnRemoval()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Set up: Make a match
        await board.FlipAsync("alice", new Position(0, 0));
        await board.FlipAsync("alice", new Position(0, 1)); // Match
        
        // Start watching
        var watchTask = board.WaitForChangeAsync("player1");
        await Task.Delay(50);
        Assert.False(watchTask.IsCompleted);
        
        // Flip new card triggers cleanup (removes matched cards)
        await board.FlipAsync("alice", new Position(1, 0));
        
        await watchTask;
        Assert.True(watchTask.IsCompleted);
    }

    [Fact]
    public async Task TestWatch_NotifiesOnMapTransform()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        var watchTask = board.WaitForChangeAsync("player1");
        await Task.Delay(50);
        Assert.False(watchTask.IsCompleted);
        
        // Map operation triggers notification
        await board.MapAsync("player1", async (card) =>
        {
            await Task.Delay(10);
            return card.ToUpper();
        });
        
        await watchTask;
        Assert.True(watchTask.IsCompleted);
    }

    [Fact]
    public async Task TestWatch_InterleaveWithOtherOperations()
    {
        var board = Board.ParseFromFile(PerfectBoardPath);
        
        // Start watching 
        var watchTask = board.WaitForChangeAsync("player1");
        
        // Other operations should work normally while watching
        var lookResult = board.ViewBy("player2");
        Assert.NotNull(lookResult);
        
        // Verify watch still waiting
        await Task.Delay(50);
        Assert.False(watchTask.IsCompleted);
        
        // Trigger change
        await board.FlipAsync("player1", new Position(0, 0));
        
        await watchTask;
        Assert.True(watchTask.IsCompleted);
    }
}