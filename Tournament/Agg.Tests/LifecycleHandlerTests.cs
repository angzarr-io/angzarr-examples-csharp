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
            Name = "Test", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100, MinPlayers = 2,
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
        var act = () => LifecycleHandler.HandleAdvanceBlind(new AdvanceBlindLevel(), state);
        act.Should().Throw<CommandRejectedError>().WithMessage("*not running*");
    }

    [Fact]
    public void AdvanceBlind_IncrementsLevel()
    {
        var state = RunningState();
        state.ApplyBlindAdvanced(new BlindLevelAdvanced { Level = 1 });

        var result = LifecycleHandler.HandleAdvanceBlind(new AdvanceBlindLevel(), state);

        result.Level.Should().Be(2);
        result.SmallBlind.Should().Be(50);
        result.BigBlind.Should().Be(100);
    }

    [Fact]
    public void AdvanceBlind_CapsAtLastLevel()
    {
        var state = RunningState();
        state.ApplyBlindAdvanced(new BlindLevelAdvanced { Level = 3 }); // At max

        var result = LifecycleHandler.HandleAdvanceBlind(new AdvanceBlindLevel(), state);

        result.Level.Should().Be(4);
        result.SmallBlind.Should().Be(100); // Uses last level values
        result.BigBlind.Should().Be(200);
    }

    [Fact]
    public void Eliminate_RejectsNotRunning()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });
        var act = () => LifecycleHandler.HandleEliminate(
            new EliminatePlayer { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }) }, state);
        act.Should().Throw<CommandRejectedError>().WithMessage("*not running*");
    }

    [Fact]
    public void Eliminate_RejectsUnregistered()
    {
        var state = RunningState();
        var act = () => LifecycleHandler.HandleEliminate(
            new EliminatePlayer { PlayerRoot = ByteString.CopyFrom(new byte[] { 9, 9, 9 }) }, state);
        act.Should().Throw<CommandRejectedError>().WithMessage("*not registered*");
    }

    [Fact]
    public void Eliminate_SetsFinishPosition()
    {
        var state = RunningState();
        var result = LifecycleHandler.HandleEliminate(
            new EliminatePlayer { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }) }, state);

        result.FinishPosition.Should().Be(2); // 2 players remaining
    }

    [Fact]
    public void Pause_RejectsNotRunning()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });
        var act = () => LifecycleHandler.HandlePause(new PauseTournament { Reason = "break" }, state);
        act.Should().Throw<CommandRejectedError>().WithMessage("*not running*");
    }

    [Fact]
    public void Pause_SetsReason()
    {
        var state = RunningState();
        var result = LifecycleHandler.HandlePause(new PauseTournament { Reason = "Dinner break" }, state);
        result.Reason.Should().Be("Dinner break");
    }

    [Fact]
    public void Resume_RejectsNotPaused()
    {
        var state = RunningState();
        var act = () => LifecycleHandler.HandleResume(new ResumeTournament(), state);
        act.Should().Throw<CommandRejectedError>().WithMessage("*not paused*");
    }

    [Fact]
    public void Resume_Success()
    {
        var state = RunningState();
        state.ApplyPaused(new TournamentPaused { Reason = "break" });

        var result = LifecycleHandler.HandleResume(new ResumeTournament(), state);
        result.ResumedAt.Should().NotBeNull();
    }
}
