using Angzarr.Client;
using Angzarr.Examples;
using FluentAssertions;
using Google.Protobuf;
using Tournament.Agg;
using Tournament.Agg.Handlers;
using Xunit;

namespace Tournament.Agg.Tests;

public class LifecycleHandlerTests
{
    private static TournamentState RunningState()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated
        {
            Name = "Test",
            BuyIn = 1000,
            StartingStack = 10000,
            MaxPlayers = 100,
            MinPlayers = 2,
            BlindStructure =
            {
                new BlindLevel { Level = 1, SmallBlind = 25, BigBlind = 50 },
                new BlindLevel { Level = 2, SmallBlind = 50, BigBlind = 100 },
                new BlindLevel { Level = 3, SmallBlind = 100, BigBlind = 200 },
            },
        });
        state.ApplyRegistrationOpened(new RegistrationOpened());
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
        { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }), FeePaid = 1000, StartingStack = 10000 });
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
        { PlayerRoot = ByteString.CopyFrom(new byte[] { 4, 5, 6 }), FeePaid = 1000, StartingStack = 10000 });
        state.ApplyTournamentStarted(new TournamentStarted());
        return state;
    }

    [Fact]
    public void AdvanceBlind_RejectsNotRunning()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });
        var ex = Assert.Throws<CommandRejectedError>(() =>
            LifecycleHandler.HandleAdvanceBlindLevel(new AdvanceBlindLevel(), state));
        // Pin LifecycleHandler.cs:25 message + code.
        Assert.Equal("Tournament is not running", ex.Message);
        Assert.Equal("TOURNAMENT_NOT_RUNNING", ex.Code);
    }

    [Fact]
    public void AdvanceBlind_IncrementsLevel()
    {
        var state = RunningState();
        state.ApplyBlindAdvanced(new BlindLevelAdvanced { Level = 1 });

        var result = (BlindLevelAdvanced)LifecycleHandler.HandleAdvanceBlindLevel(new AdvanceBlindLevel(), state);

        result.Level.Should().Be(2);
        result.SmallBlind.Should().Be(50);
        result.BigBlind.Should().Be(100);
    }

    [Fact]
    public void AdvanceBlind_RejectsWhenStructureExhausted()
    {
        // Post-source-evolution (EU-0855/EU-0856): advancing past the
        // declared structure throws "BLIND_STRUCTURE_EXHAUSTED" rather
        // than capping. Pinning the new semantics.
        var state = RunningState();
        state.ApplyBlindAdvanced(new BlindLevelAdvanced { Level = 3 }); // At max
        var ex = Assert.Throws<CommandRejectedError>(() =>
            LifecycleHandler.HandleAdvanceBlindLevel(new AdvanceBlindLevel(), state));
        // Pin LifecycleHandler.cs:38-43 message + code + details.
        Assert.Contains("Blind structure exhausted at level 3", ex.Message);
        Assert.Contains("max defined level is 3", ex.Message);
        Assert.Equal("BLIND_STRUCTURE_EXHAUSTED", ex.Code);
        Assert.Equal("3", ex.Details["current"]);
        Assert.Equal("3", ex.Details["max_value"]);
    }

    [Fact]
    public void AdvanceBlind_AtStructureBoundary_StillAdvances()
    {
        // Pin LifecycleHandler.cs:36 newLevel > maxDefined (boundary).
        // Mutant >= maxDefined would reject level 3 → 3 (newLevel==max).
        var state = RunningState();
        state.ApplyBlindAdvanced(new BlindLevelAdvanced { Level = 2 });
        // CurrentLevel=2, newLevel=3, maxDefined=3 → 3>3 false → allowed.
        var evt = (BlindLevelAdvanced)LifecycleHandler.HandleAdvanceBlindLevel(
            new AdvanceBlindLevel(), state);
        Assert.Equal(3, evt.Level);
    }

    [Fact]
    public void AdvanceBlind_NewLevelEqualsMaxDefined_StillEmits()
    {
        // Pin LifecycleHandler.cs:47 newLevel <= maxDefined (boundary).
        // Mutant < maxDefined would skip the BlindLevelAdvanced
        // construction when newLevel == maxDefined.
        var state = RunningState();
        state.ApplyBlindAdvanced(new BlindLevelAdvanced { Level = 2 });
        var evt = (BlindLevelAdvanced)LifecycleHandler.HandleAdvanceBlindLevel(
            new AdvanceBlindLevel(), state);
        Assert.Equal(3, evt.Level);
        Assert.Equal(100, evt.SmallBlind);
        Assert.Equal(200, evt.BigBlind);
    }

    [Fact]
    public void Eliminate_RejectsNotRunning()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });
        var ex = Assert.Throws<CommandRejectedError>(() =>
            LifecycleHandler.HandleEliminatePlayer(
                new EliminatePlayer { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }) }, state));
        // Pin LifecycleHandler.cs:156 message.
        Assert.Equal("Tournament is not running", ex.Message);
    }

    [Fact]
    public void Eliminate_RejectsUnregistered()
    {
        var state = RunningState();
        var ex = Assert.Throws<CommandRejectedError>(() =>
            LifecycleHandler.HandleEliminatePlayer(
                new EliminatePlayer { PlayerRoot = ByteString.CopyFrom(new byte[] { 9, 9, 9 }) }, state));
        // Pin LifecycleHandler.cs:162 message.
        Assert.Contains("is not registered in this tournament", ex.Message);
    }

    [Fact]
    public void Eliminate_SetsFinishPosition()
    {
        var state = RunningState();
        var result = LifecycleHandler.HandleEliminatePlayer(
            new EliminatePlayer { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }) }, state);

        result.FinishPosition.Should().Be(2); // 2 players remaining
    }

    [Fact]
    public void Pause_RejectsNotRunning()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });
        var ex = Assert.Throws<CommandRejectedError>(() =>
            LifecycleHandler.HandlePauseTournament(new PauseTournament { Reason = "break" }, state));
        // Pin LifecycleHandler.cs:182 message.
        Assert.Equal("Tournament is not running", ex.Message);
    }

    [Fact]
    public void Pause_SetsReason()
    {
        var state = RunningState();
        var result = LifecycleHandler.HandlePauseTournament(new PauseTournament { Reason = "Dinner break" }, state);
        result.Reason.Should().Be("Dinner break");
    }

    [Fact]
    public void Resume_RejectsNotPaused()
    {
        var state = RunningState();
        var ex = Assert.Throws<CommandRejectedError>(() =>
            LifecycleHandler.HandleResumeTournament(new ResumeTournament(), state));
        // Pin LifecycleHandler.cs:204 message.
        Assert.Equal("Tournament is not paused", ex.Message);
    }

    [Fact]
    public void Resume_Success()
    {
        var state = RunningState();
        state.ApplyPaused(new TournamentPaused { Reason = "break" });

        var result = LifecycleHandler.HandleResumeTournament(new ResumeTournament(), state);
        result.ResumedAt.Should().NotBeNull();
    }

    // ===== Pause-twice =====

    [Fact]
    public void Pause_AlreadyPaused_Rejects()
    {
        var state = RunningState();
        state.ApplyPaused(new TournamentPaused { Reason = "break" });
        var ex = Assert.Throws<CommandRejectedError>(() =>
            LifecycleHandler.HandlePauseTournament(new PauseTournament(), state));
        // Pin LifecycleHandler.cs:178 message.
        Assert.Equal("Tournament is already paused", ex.Message);
    }

    [Fact]
    public void Resume_FromBagged_SucceedsDirectly()
    {
        // Day-2 resume from bagged.
        var state = RunningState();
        state.Status = TournamentStatus.TournamentBagged;
        var result = LifecycleHandler.HandleResumeTournament(new ResumeTournament(), state);
        result.ResumedAt.Should().NotBeNull();
    }

    // ===== StartTournament =====

    [Fact]
    public void StartTournament_RejectsNonExistent()
    {
        var ex = Assert.Throws<CommandRejectedError>(() =>
            LifecycleHandler.HandleStartTournament(new StartTournament(), new TournamentState()));
        // Pin LifecycleHandler.cs:217 message.
        Assert.Equal("Tournament does not exist", ex.Message);
    }

    [Fact]
    public void StartTournament_RejectsBelowMinPlayers()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated
        { Name = "T", BuyIn = 100, StartingStack = 1000, MaxPlayers = 9, MinPlayers = 3 });
        // Only 1 enrolled
        state.ApplyRegistrationOpened(new RegistrationOpened());
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
        { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }), FeePaid = 100 });

        var ex = Assert.Throws<CommandRejectedError>(() =>
            LifecycleHandler.HandleStartTournament(new StartTournament(), state));
        // Pin LifecycleHandler.cs:221 message.
        Assert.Contains("Not enough players to start: requested 3, available 1", ex.Message);
    }

    [Fact]
    public void StartTournament_Success_EmitsPlayerCountAndPool()
    {
        var state = RunningState();
        state.Status = TournamentStatus.TournamentCreated;  // re-open the start gate
        var evt = LifecycleHandler.HandleStartTournament(new StartTournament(), state);
        Assert.Equal(2, evt.TotalPlayers);
        Assert.Equal(2000, evt.TotalPrizePool);  // 2 players × FeePaid 1000
    }

    // ===== AdvanceBlindLevel non-default branches =====

    [Fact]
    public void AdvanceBlind_NoBlindStructure_AndChipRace_OnlyEmitsColorUp()
    {
        // Chip-race-only invocation; no blind structure.
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated
        {
            Name = "T",
            BuyIn = 100,
            StartingStack = 1000,
            MaxPlayers = 9,
            MinPlayers = 2,
        });
        state.ApplyTournamentStarted(new TournamentStarted());
        // Seed chip inventories — required for ComputeChipRace to produce
        // a meaningful event without throwing. Keys must be hex digit
        // strings so HexToBytes() can decode them.
        state.PlayerChipInventories["aa"] = new Dictionary<int, int>
        {
            { 25, 4 },  // 100 worth in 25-denom chips
        };
        state.PlayerChipInventories["bb"] = new Dictionary<int, int>
        {
            { 25, 7 },  // 175 worth in 25-denom chips
        };

        var result = LifecycleHandler.HandleAdvanceBlindLevel(
            new AdvanceBlindLevel { RetireDenomination = 25, NewDenomination = 100 }, state);
        Assert.IsType<ColorUpCompleted>(result);
        var colorUp = (ColorUpCompleted)result;
        Assert.Equal(25, colorUp.RetiredDenomination);
        Assert.Equal(100, colorUp.NewDenomination);
    }

    [Fact]
    public void AdvanceBlind_NewLevelOutsideStructure_AndChipRace_StillEmits()
    {
        var state = RunningState();
        state.ApplyBlindAdvanced(new BlindLevelAdvanced { Level = 3 });  // at max
        state.PlayerChipInventories["aa"] = new Dictionary<int, int> { { 25, 4 } };

        // newLevel (4) > maxDefined (3); but chipRace = true → no throw.
        var result = LifecycleHandler.HandleAdvanceBlindLevel(
            new AdvanceBlindLevel { RetireDenomination = 25, NewDenomination = 100 }, state);
        Assert.IsType<ColorUpCompleted>(result);
    }

    // ===== ComputeChipRace direct tests =====

    [Fact]
    public void ComputeChipRace_NoInventory_ProducesEmptyEvent()
    {
        var state = new TournamentState();
        var evt = LifecycleHandler.ComputeChipRace(state, retireDenom: 25, newDenom: 100);
        Assert.Equal(25, evt.RetiredDenomination);
        Assert.Equal(100, evt.NewDenomination);
        Assert.Empty(evt.PerPlayerAwards);
    }

    [Fact]
    public void ComputeChipRace_OneFullConversion_ZeroRemainder()
    {
        var state = new TournamentState();
        // 4 × 25 = 100; fullNew = 100/100 = 1; remainder = 0.
        state.PlayerChipInventories["aa"] = new Dictionary<int, int> { { 25, 4 } };

        var evt = LifecycleHandler.ComputeChipRace(state, retireDenom: 25, newDenom: 100);
        Assert.Single(evt.PerPlayerAwards);
        var award = evt.PerPlayerAwards[0];
        Assert.Equal(1, award.ChipsWon);
        Assert.False(award.Rescued);
        Assert.Equal(0, evt.ChipsRemovedByRace);
        Assert.Equal(0, evt.ChipsAddedByRescue);
    }

    [Fact]
    public void ComputeChipRace_RemainderTriggersRaceAward()
    {
        var state = new TournamentState();
        // 3 × 25 = 75; fullNew = 0, remainder = 75.
        state.PlayerChipInventories["aa"] = new Dictionary<int, int> { { 25, 3 } };
        // 5 × 25 = 125; fullNew = 1, remainder = 25.
        state.PlayerChipInventories["bb"] = new Dictionary<int, int> { { 25, 5 } };

        // totalRemainder = 100; raceChipsToAward = 1; chipsRemovedByRace = 0.
        // Top remainder = aa (75) → wins the extra chip.
        var evt = LifecycleHandler.ComputeChipRace(state, retireDenom: 25, newDenom: 100);
        Assert.Equal(0, evt.ChipsRemovedByRace);
        Assert.Equal(2, evt.PerPlayerAwards.Count);
        // Both should end with 1 chip (aa: 0 full + 1 race, bb: 1 full + 0 race).
        Assert.All(evt.PerPlayerAwards, a => Assert.Equal(1, a.ChipsWon));
    }

    [Fact]
    public void ComputeChipRace_RescueAddsChipsToZeroStakePlayer()
    {
        var state = new TournamentState();
        // 1 × 25 = 25; fullNew = 0, remainder = 25.
        state.PlayerChipInventories["aa"] = new Dictionary<int, int> { { 25, 1 } };
        // 1 × 25 = 25; fullNew = 0, remainder = 25.
        state.PlayerChipInventories["bb"] = new Dictionary<int, int> { { 25, 1 } };
        // totalRemainder = 50; raceChipsToAward = 50/100 = 0;
        // chipsRemovedByRace = 50 - 0 = 50.
        // Both contenders lose; both stakeAfter = 0 + 0 = 0; rescue triggers.

        var evt = LifecycleHandler.ComputeChipRace(state, retireDenom: 25, newDenom: 100);
        Assert.Equal(50, evt.ChipsRemovedByRace);
        Assert.Equal(2, evt.PerPlayerAwards.Count);
        Assert.All(evt.PerPlayerAwards, a => Assert.True(a.Rescued));
        Assert.All(evt.PerPlayerAwards, a => Assert.Equal(1, a.ChipsWon));
        Assert.Equal(200, evt.ChipsAddedByRescue);  // 2 × 100
    }
}
