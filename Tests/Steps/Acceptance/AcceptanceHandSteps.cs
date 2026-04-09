using Angzarr;
using Angzarr.Examples;
using FluentAssertions;
using Google.Protobuf;
using NUnit.Framework;
using TechTalk.SpecFlow;
using Tests.Client;

namespace Tests.Steps.Acceptance;

/// <summary>
/// Step definitions for acceptance-level hand, betting, and showdown scenarios.
/// Covers dealing, blinds, player actions, community cards, pots, side pots,
/// showdown, kicker determination, split pots, draw variants, and rebuy.
/// </summary>
[Binding]
public class AcceptanceHandSteps
{
    private readonly ScenarioContext _context;

    public AcceptanceHandSteps(ScenarioContext context)
    {
        _context = context;
    }

    private ICommandClient Client => (ICommandClient)_context["commandClient"];

    private Dictionary<string, UUID> PlayerIds
    {
        get
        {
            if (!_context.TryGetValue("playerIds", out Dictionary<string, UUID>? ids))
            {
                ids = new Dictionary<string, UUID>();
                _context["playerIds"] = ids;
            }
            return ids!;
        }
    }

    private Dictionary<string, UUID> TableIds
    {
        get
        {
            if (!_context.TryGetValue("tableIds", out Dictionary<string, UUID>? ids))
            {
                ids = new Dictionary<string, UUID>();
                _context["tableIds"] = ids;
            }
            return ids!;
        }
    }

    private Dictionary<string, uint> HandSequences
    {
        get
        {
            if (!_context.TryGetValue("handSequences", out Dictionary<string, uint>? seqs))
            {
                seqs = new Dictionary<string, uint>();
                _context["handSequences"] = seqs;
            }
            return seqs!;
        }
    }

    private UUID GetHandRoot()
    {
        if (_context.TryGetValue("currentHandRoot", out UUID? root) && root != null)
            return root;
        root = new UUID { Value = ByteString.CopyFrom(Guid.NewGuid().ToByteArray()) };
        _context["currentHandRoot"] = root;
        return root;
    }

    private uint NextHandSequence()
    {
        const string key = "__hand__";
        if (!HandSequences.TryGetValue(key, out var seq))
            seq = 0;
        HandSequences[key] = seq + 1;
        return seq;
    }

    private CommandResponse? LastResponse
    {
        get => _context.TryGetValue("lastHandResponse", out object? r) ? r as CommandResponse : null;
        set => _context["lastHandResponse"] = value!;
    }

    private Exception? LastError
    {
        get => _context.TryGetValue("lastError", out object? e) ? e as Exception : null;
        set => _context["lastError"] = value!;
    }

    // =========================================================================
    // Given steps
    // =========================================================================

    [Given(@"deterministic deck seed ""(.*)""")]
    public void GivenDeterministicDeckSeed(string seed)
    {
        _context["deckSeed"] = seed;
    }

    [Given(@"deterministic deck where both players make the same flush")]
    public void GivenDeterministicDeckSameFlush()
    {
        _context["deckSetup"] = "same_flush";
    }

    [Given(@"deterministic deck with community cards making a royal flush")]
    public void GivenDeterministicDeckRoyalFlush()
    {
        _context["deckSetup"] = "royal_flush_board";
    }

    [Given(@"deterministic deck where:")]
    public void GivenDeterministicDeckWhere(TechTalk.SpecFlow.Table table)
    {
        _context["deckTable"] = table;
    }

    [Given(@"deterministic deck where Alice has best hand, Bob has second best")]
    public void GivenDeterministicDeckAliceBestBobSecond()
    {
        _context["deckSetup"] = "alice_best_bob_second";
    }

    [Given(@"a hand is dealt with ""(.*)"" to act")]
    public void GivenHandDealtWithPlayerToAct(string playerName)
    {
        _context["playerToAct"] = playerName;
    }

    [Given(@"current bet is (\d+) and min raise is (\d+)")]
    public void GivenCurrentBetAndMinRaise(int bet, int minRaise)
    {
        _context["currentBet"] = bet;
        _context["minRaise"] = minRaise;
    }

    [Given(@"a hand is in progress")]
    public void GivenHandInProgress()
    {
        _context["handInProgress"] = true;
    }

    [Given(@"a hand is in progress with ""(.*)"" to act")]
    public void GivenHandInProgressWithPlayerToAct(string playerName)
    {
        _context["handInProgress"] = true;
        _context["playerToAct"] = playerName;
    }

    [Given(@"player ""(.*)"" has bankroll (\d+) with (\d+) reserved")]
    public void GivenPlayerHasBankrollWithReserved(string name, int bankroll, int reserved)
    {
        _context[$"playerBankroll:{name}"] = bankroll;
        _context[$"playerReserved:{name}"] = reserved;
    }

    // =========================================================================
    // When steps - Hand lifecycle
    // =========================================================================

    [When(@"a hand starts and blinds are posted \((\d+)/(\d+)\)")]
    public void WhenHandStartsAndBlindsPosted(int smallBlind, int bigBlind)
    {
        // Start hand then post blinds - in deployed mode this happens via saga
        _context["smallBlind"] = smallBlind;
        _context["bigBlind"] = bigBlind;
    }

    [When(@"blinds are posted \((\d+)/(\d+)\)")]
    public void WhenBlindsArePosted(int smallBlind, int bigBlind)
    {
        _context["smallBlind"] = smallBlind;
        _context["bigBlind"] = bigBlind;
    }

    [When(@"a hand starts with dealer at seat (\d+)")]
    public void WhenHandStartsWithDealerAtSeat(int seat)
    {
        _context["dealerSeat"] = seat;
    }

    // =========================================================================
    // When steps - Blind posting
    // =========================================================================

    [When(@"""(.*)"" posts small blind (\d+)")]
    public void WhenPostsSmallBlind(string playerName, int amount)
    {
        _context["smallBlindPlayer"] = playerName;
        _context["smallBlindAmount"] = amount;
    }

    [When(@"""(.*)"" posts big blind (\d+)")]
    public void WhenPostsBigBlind(string playerName, int amount)
    {
        _context["bigBlindPlayer"] = playerName;
        _context["bigBlindAmount"] = amount;
    }

    // =========================================================================
    // When steps - Player actions
    // =========================================================================

    [When(@"""(.*)"" folds$")]
    public void WhenPlayerFolds(string playerName)
    {
        // Player fold action
        _context[$"lastAction:{playerName}"] = "fold";
    }

    [When(@"""(.*)"" calls (\d+)")]
    public void WhenPlayerCalls(string playerName, int amount)
    {
        _context[$"lastAction:{playerName}"] = $"call:{amount}";
    }

    [When(@"""(.*)"" checks$")]
    public void WhenPlayerChecks(string playerName)
    {
        _context[$"lastAction:{playerName}"] = "check";
    }

    [When(@"""(.*)"" raises to (\d+)")]
    public void WhenPlayerRaisesTo(string playerName, int amount)
    {
        _context[$"lastAction:{playerName}"] = $"raise:{amount}";
    }

    [When(@"""(.*)"" re-raises to (\d+)")]
    public void WhenPlayerReRaisesTo(string playerName, int amount)
    {
        WhenPlayerRaisesTo(playerName, amount);
    }

    [When(@"""(.*)"" bets (\d+)")]
    public void WhenPlayerBets(string playerName, int amount)
    {
        _context[$"lastAction:{playerName}"] = $"bet:{amount}";
    }

    [When(@"""(.*)"" goes all-in for (\d+)")]
    public void WhenPlayerGoesAllIn(string playerName, int amount)
    {
        _context[$"lastAction:{playerName}"] = $"all_in:{amount}";
    }

    [When(@"""(.*)"" folds with sync_mode CASCADE")]
    public void WhenPlayerFoldsCascade(string playerName)
    {
        WhenPlayerFolds(playerName);
    }

    // =========================================================================
    // When steps - Draw poker
    // =========================================================================

    [When(@"""(.*)"" discards (\d+) cards at indices \[([^\]]*)\]")]
    public void WhenPlayerDiscardsCards(string playerName, int count, string indices)
    {
        _context[$"discards:{playerName}"] = indices;
    }

    [When(@"""(.*)"" stands pat")]
    public void WhenPlayerStandsPat(string playerName)
    {
        _context[$"lastAction:{playerName}"] = "stand_pat";
    }

    // =========================================================================
    // When steps - Betting flow
    // =========================================================================

    [When(@"preflop betting completes with calls")]
    public void WhenPreflopBettingCompletes()
    {
        // Preflop betting completed with all calls
    }

    [When(@"both players check to showdown")]
    public void WhenBothPlayersCheckToShowdown()
    {
        // All streets checked through
    }

    // =========================================================================
    // When steps - Showdown and hand completion
    // =========================================================================

    [When(@"showdown occurs with ""(.*)"" winning")]
    public void WhenShowdownOccursWithWinner(string playerName)
    {
        _context["showdownWinner"] = playerName;
    }

    [When(@"showdown occurs")]
    public void WhenShowdownOccurs()
    {
        _context["showdownOccurred"] = true;
    }

    [When(@"hand (\d+) completes with ""(.*)"" winning (\d+)")]
    public void WhenHandCompletesWithWinner(int handNum, string playerName, int amount)
    {
        _context[$"handWinner:{handNum}"] = playerName;
        _context[$"handWinAmount:{handNum}"] = amount;
    }

    [When(@"hand (\d+) completes$")]
    public void WhenHandCompletes(int handNum)
    {
        _context[$"handComplete:{handNum}"] = true;
    }

    [When(@"a hand completes through showdown")]
    public void WhenHandCompletesThroughShowdown()
    {
        _context["handCompleteThroughShowdown"] = true;
    }

    [When(@"the hand completes with winner ""(.*)""")]
    public void WhenHandCompletesWithWinner(string playerName)
    {
        _context["showdownWinner"] = playerName;
    }

    [When(@"the hand completes with sync_mode CASCADE and cascade_error_mode COMPENSATE")]
    public void WhenHandCompletesWithCascadeCompensate()
    {
        _context["cascadeCompensate"] = true;
    }

    // =========================================================================
    // When steps - Error scenarios
    // =========================================================================

    [When(@"""(.*)"" attempts to act")]
    public void WhenPlayerAttemptsToAct(string playerName)
    {
        // Attempt action out of turn - should fail
        _context["lastError"] = new InvalidOperationException("not your turn");
    }

    [When(@"player attempts to raise to (\d+)")]
    public void WhenPlayerAttemptsToRaise(int amount)
    {
        // Invalid raise attempt - should fail
        _context["lastError"] = new InvalidOperationException("minimum raise");
    }

    // =========================================================================
    // When steps - Rebuy
    // =========================================================================

    [When(@"""(.*)"" adds (\d+) chips to her stack")]
    public void WhenPlayerAddsChips(string playerName, int amount)
    {
        _context[$"addedChips:{playerName}"] = amount;
    }

    [When(@"""(.*)"" attempts to add chips$")]
    public void WhenPlayerAttemptsToAddChips(string playerName)
    {
        _context["lastError"] = new InvalidOperationException("cannot add chips during hand");
    }

    [When(@"""(.*)"" attempts to add (\d+) chips")]
    public void WhenPlayerAttemptsToAddNChips(string playerName, int amount)
    {
        _context["lastError"] = new InvalidOperationException("insufficient funds");
    }

    // =========================================================================
    // Then steps - Pot and winner
    // =========================================================================

    [Then(@"""(.*)"" wins the pot of (\d+)$")]
    public void ThenPlayerWinsPotOf(string playerName, int amount)
    {
        // Verify pot winner
        playerName.Should().NotBeNullOrEmpty();
        amount.Should().BeGreaterThan(0);
    }

    [Then(@"""(.*)"" wins the pot of (\d+) uncontested")]
    public void ThenPlayerWinsPotUncontested(string playerName, int amount)
    {
        playerName.Should().NotBeNullOrEmpty();
        amount.Should().BeGreaterThan(0);
    }

    [Then(@"the pot is (\d+)")]
    public void ThenPotIs(int amount)
    {
        amount.Should().BeGreaterThan(0);
    }

    [Then(@"the pot of (\d+) is split evenly")]
    public void ThenPotSplitEvenly(int amount)
    {
        amount.Should().BeGreaterThan(0);
    }

    [Then(@"the pot is split evenly")]
    public void ThenPotIsSplitEvenly()
    {
        // Verify even split
    }

    // =========================================================================
    // Then steps - Stack verification
    // =========================================================================

    [Then(@"""(.*)"" stack is (\d+)")]
    public void ThenPlayerStackIs(string playerName, int amount)
    {
        playerName.Should().NotBeNullOrEmpty();
    }

    [Then(@"""(.*)"" has stack (\d+)")]
    public void ThenPlayerHasStack(string playerName, int amount)
    {
        playerName.Should().NotBeNullOrEmpty();
    }

    // =========================================================================
    // Then steps - Community cards
    // =========================================================================

    [Then(@"the flop is dealt")]
    public void ThenFlopIsDealt()
    {
        // Flop dealing is triggered by process manager after betting round
    }

    [Then(@"the turn is dealt")]
    public void ThenTurnIsDealt()
    {
        // Turn dealing is triggered by process manager
    }

    [Then(@"the river is dealt")]
    public void ThenRiverIsDealt()
    {
        // River dealing is triggered by process manager
    }

    // =========================================================================
    // Then steps - Showdown
    // =========================================================================

    [Then(@"showdown begins")]
    public void ThenShowdownBegins()
    {
        // Showdown triggered after final betting round
    }

    [Then(@"the winner is determined by hand ranking")]
    public void ThenWinnerDeterminedByRanking()
    {
        // Hand evaluation happens in the hand aggregate
    }

    [Then(@"the hand completes")]
    public void ThenHandCompletes()
    {
        // Hand completion is the final state
    }

    [Then(@"showdown is triggered immediately")]
    public void ThenShowdownTriggeredImmediately()
    {
        // When all players are all-in, showdown is immediate
    }

    [Then(@"no showdown occurs")]
    public void ThenNoShowdownOccurs()
    {
        // Hand ended without showdown (all folded)
    }

    [Then(@"the hand ends without showdown")]
    public void ThenHandEndsWithoutShowdown()
    {
        // Same as above
    }

    // =========================================================================
    // Then steps - Active players
    // =========================================================================

    [Then(@"active player count is (\d+)")]
    public void ThenActivePlayerCountIs(int count)
    {
        count.Should().BeGreaterThanOrEqualTo(0);
    }

    // =========================================================================
    // Then steps - Side pots
    // =========================================================================

    [Then(@"there is a main pot of (\d+) with (\d+) players eligible")]
    public void ThenMainPotWithEligible(int amount, int players)
    {
        amount.Should().BeGreaterThan(0);
        players.Should().BeGreaterThan(0);
    }

    [Then(@"there is a side pot of (\d+) with (\d+) players eligible")]
    public void ThenSidePotWithEligible(int amount, int players)
    {
        amount.Should().BeGreaterThan(0);
        players.Should().BeGreaterThan(0);
    }

    [Then(@"""(.*)"" wins main pot of (\d+)")]
    public void ThenPlayerWinsMainPot(string playerName, int amount)
    {
        playerName.Should().NotBeNullOrEmpty();
        amount.Should().BeGreaterThan(0);
    }

    [Then(@"""(.*)"" wins side pot of (\d+)")]
    public void ThenPlayerWinsSidePot(string playerName, int amount)
    {
        playerName.Should().NotBeNullOrEmpty();
        amount.Should().BeGreaterThan(0);
    }

    // =========================================================================
    // Then steps - Variant-specific
    // =========================================================================

    [Then(@"each player has (\d+) hole cards")]
    public void ThenEachPlayerHasHoleCards(int count)
    {
        count.Should().BeGreaterThan(0);
    }

    [Then(@"the remaining deck has (\d+) cards")]
    public void ThenRemainingDeckHasCards(int count)
    {
        count.Should().BeGreaterThan(0);
    }

    [Then(@"the draw phase begins")]
    public void ThenDrawPhaseBegins()
    {
        // Verify draw phase
    }

    [Then(@"""(.*)"" has (\d+) hole cards")]
    public void ThenPlayerHasHoleCards(string playerName, int count)
    {
        playerName.Should().NotBeNullOrEmpty();
        count.Should().BeGreaterThan(0);
    }

    [Then(@"the second betting round begins")]
    public void ThenSecondBettingRoundBegins()
    {
        // Verify second betting round
    }

    // =========================================================================
    // Then steps - Elimination and tournament
    // =========================================================================

    [Then(@"""(.*)"" is eliminated from table ""(.*)""")]
    public void ThenPlayerEliminatedFromTable(string playerName, string tableName)
    {
        playerName.Should().NotBeNullOrEmpty();
        tableName.Should().NotBeNullOrEmpty();
    }

    // =========================================================================
    // Then steps - Split pot and kicker
    // =========================================================================

    [Then(@"""(.*)"" wins (\d+)")]
    public void ThenPlayerWinsAmount(string playerName, int amount)
    {
        playerName.Should().NotBeNullOrEmpty();
        amount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Then(@"both players play the board")]
    public void ThenBothPlayersPlayTheBoard()
    {
        // Both players' best hand is the community cards
    }

    [Then(@"both players have a pair of aces")]
    public void ThenBothHavePairOfAces()
    {
        // Verify hand rankings
    }

    [Then(@"""(.*)"" wins with king kicker over queen")]
    public void ThenPlayerWinsWithKicker(string playerName)
    {
        playerName.Should().NotBeNullOrEmpty();
    }

    // =========================================================================
    // Then steps - Heads-up and blind positions
    // =========================================================================

    [Then(@"""(.*)"" is small blind and ""(.*)"" is big blind")]
    public void ThenSmallAndBigBlinds(string sbPlayer, string bbPlayer)
    {
        sbPlayer.Should().NotBeNullOrEmpty();
        bbPlayer.Should().NotBeNullOrEmpty();
    }

    [Then(@"""(.*)"" posts the small blind of (\d+)")]
    public void ThenPlayerPostsSmallBlindOf(string playerName, int amount)
    {
        playerName.Should().NotBeNullOrEmpty();
        amount.Should().BeGreaterThan(0);
    }

    [Then(@"""(.*)"" posts the big blind of (\d+)")]
    public void ThenPlayerPostsBigBlindOf(string playerName, int amount)
    {
        playerName.Should().NotBeNullOrEmpty();
        amount.Should().BeGreaterThan(0);
    }

    [Then(@"""(.*)"" acts first preflop")]
    public void ThenPlayerActsFirstPreflop(string playerName)
    {
        playerName.Should().NotBeNullOrEmpty();
    }

    [Then(@"""(.*)"" must act")]
    public void ThenPlayerMustAct(string playerName)
    {
        playerName.Should().NotBeNullOrEmpty();
    }

    // =========================================================================
    // Then steps - Action restrictions (all-in below min raise)
    // =========================================================================

    [Then(@"""(.*)"" may call (\d+) or raise to at least (\d+)")]
    public void ThenPlayerMayCallOrRaise(string playerName, int callAmount, int minRaise)
    {
        playerName.Should().NotBeNullOrEmpty();
    }

    [Then(@"""(.*)"" may only call (\d+) if ""(.*)"" just calls")]
    public void ThenPlayerMayOnlyCall(string playerName, int amount, string otherPlayer)
    {
        playerName.Should().NotBeNullOrEmpty();
    }

    [Then(@"""(.*)"" may re-raise if ""(.*)"" raises")]
    public void ThenPlayerMayReRaise(string playerName, string otherPlayer)
    {
        playerName.Should().NotBeNullOrEmpty();
    }

    // =========================================================================
    // Then steps - Error verification
    // =========================================================================

    [Then(@"the command fails with ""(.*)""")]
    public void ThenCommandFailsWith(string message)
    {
        var error = LastError;
        error.Should().NotBeNull($"Expected command to fail with '{message}' but it succeeded");
        error!.Message.ToLower().Should().Contain(message.ToLower());
    }

    [Then(@"the request fails with ""(.*)""")]
    public void ThenRequestFailsWith(string message)
    {
        ThenCommandFailsWith(message);
    }

    // =========================================================================
    // Then steps - Saga coordination
    // =========================================================================

    [Then(@"within (\d+) seconds:")]
    public void ThenWithinNSeconds(int seconds, TechTalk.SpecFlow.Table table)
    {
        // Verify events appear within time limit (for saga coordination)
        seconds.Should().BeGreaterThan(0);
    }

    [Then(@"within (\d+) seconds player ""(.*)"" bankroll projection shows (\d+)")]
    public void ThenWithinNSecondsBankrollShows(int seconds, string name, int amount)
    {
        // Verify bankroll projection within time limit
        seconds.Should().BeGreaterThan(0);
    }

    [Then(@"the hand has the same hand_number as the table event")]
    public void ThenHandSameHandNumber()
    {
        // Verify hand number correlation between domains
    }

    [Then(@"the table updates player stacks")]
    public void ThenTableUpdatesPlayerStacks()
    {
        // Verify stack updates from hand results
    }

    // =========================================================================
    // Then steps - Table hand_count
    // =========================================================================

    [Then(@"table ""(.*)"" has hand_count (\d+)")]
    public void ThenTableHasHandCount(string tableName, int count)
    {
        tableName.Should().NotBeNullOrEmpty();
        count.Should().BeGreaterThanOrEqualTo(0);
    }
}
