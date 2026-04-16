using Angzarr.Client;
using Angzarr.Examples;
using FluentAssertions;
using Google.Protobuf;
using Tournament.Agg;
using Tournament.Agg.Handlers;
using Xunit;

namespace Tournament.Agg.Tests;

public class RebuyHandlerTests
{
    private static TournamentState RebuyState()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated
        {
            Name = "Rebuy", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100, MinPlayers = 2,
            RebuyConfig = new RebuyConfig
            {
                Enabled = true, MaxRebuys = 3, RebuyLevelCutoff = 4, RebuyCost = 1000, RebuyChips = 10000,
            },
        });
        state.ApplyRegistrationOpened(new RegistrationOpened());
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
            { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }), FeePaid = 1000, StartingStack = 10000 });
        state.ApplyTournamentStarted(new TournamentStarted());
        state.ApplyBlindAdvanced(new BlindLevelAdvanced { Level = 2 });
        return state;
    }

    [Fact]
    public void RejectsNonExistent()
    {
        var act = () => ProcessRebuyHandler.Handle(
            new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }) },
            new TournamentState());
        act.Should().Throw<CommandRejectedError>().WithMessage("*does not exist*");
    }

    [Fact]
    public void RejectsNotRunning()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });
        var act = () => ProcessRebuyHandler.Handle(
            new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }) }, state);
        act.Should().Throw<CommandRejectedError>().WithMessage("*not running*");
    }

    [Fact]
    public void RejectsUnregistered()
    {
        var state = RebuyState();
        var act = () => ProcessRebuyHandler.Handle(
            new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 9, 9, 9 }) }, state);
        act.Should().Throw<CommandRejectedError>().WithMessage("*not registered*");
    }

    [Fact]
    public void DeniesWindowClosed()
    {
        var state = RebuyState();
        state.ApplyBlindAdvanced(new BlindLevelAdvanced { Level = 5 }); // Past cutoff of 4

        var result = ProcessRebuyHandler.Handle(
            new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }) }, state);

        result.Should().BeOfType<RebuyDenied>();
        ((RebuyDenied)result).Reason.Should().Be("window_closed");
    }

    [Fact]
    public void DeniesMaxReached()
    {
        var state = RebuyState();
        for (int i = 0; i < 3; i++)
            state.ApplyRebuyProcessed(new RebuyProcessed
                { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }), RebuyCount = i + 1 });

        var result = ProcessRebuyHandler.Handle(
            new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }) }, state);

        result.Should().BeOfType<RebuyDenied>();
        ((RebuyDenied)result).Reason.Should().Be("max_reached");
    }

    [Fact]
    public void DeniesRebuysDisabled()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated
        {
            Name = "NoRebuy", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100,
            // No rebuy config
        });
        state.ApplyRegistrationOpened(new RegistrationOpened());
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
            { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }), FeePaid = 1000 });
        state.ApplyTournamentStarted(new TournamentStarted());

        var result = ProcessRebuyHandler.Handle(
            new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }) }, state);

        result.Should().BeOfType<RebuyDenied>();
        ((RebuyDenied)result).Reason.Should().Be("rebuys_disabled");
    }

    [Fact]
    public void ProcessesSuccessfully()
    {
        var state = RebuyState();
        var result = ProcessRebuyHandler.Handle(
            new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }) }, state);

        result.Should().BeOfType<RebuyProcessed>();
        var processed = (RebuyProcessed)result;
        processed.RebuyCost.Should().Be(1000);
        processed.ChipsAdded.Should().Be(10000);
        processed.RebuyCount.Should().Be(1);
    }

    [Fact]
    public void IncrementsRebuyCount()
    {
        var state = RebuyState();
        state.ApplyRebuyProcessed(new RebuyProcessed
            { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }), RebuyCount = 2 });

        var result = ProcessRebuyHandler.Handle(
            new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }) }, state);

        result.Should().BeOfType<RebuyProcessed>();
        ((RebuyProcessed)result).RebuyCount.Should().Be(3);
    }
}
