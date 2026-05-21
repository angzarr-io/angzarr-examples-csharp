using Angzarr.Client;
using Angzarr.Examples;
using FluentAssertions;
using Google.Protobuf;
using Tournament.Agg;
using Tournament.Agg.Handlers;
using Xunit;

namespace Tournament.Agg.Tests;

/// <summary>
/// Tests for <see cref="ProcessRebuyHandler"/>. Post-source-evolution
/// reasons and message phrasing have shifted (e.g. "rebuys_disabled" →
/// "Rebuys are not enabled"), and unregistered players now produce a
/// RebuyDenied event rather than throwing (EU-0818).
/// </summary>
public class RebuyHandlerTests
{
    private static TournamentState RebuyState()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated
        {
            Name = "Rebuy",
            BuyIn = 1000,
            StartingStack = 10000,
            MaxPlayers = 100,
            MinPlayers = 2,
            RebuyConfig = new RebuyConfig
            {
                Enabled = true,
                MaxRebuys = 3,
                RebuyLevelCutoff = 4,
                RebuyCost = 1000,
                RebuyChips = 10000,
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
        var ex = Assert.Throws<CommandRejectedError>(() =>
            ProcessRebuyHandler.Handle(
                new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }) },
                new TournamentState()));
        // Pin ProcessRebuyHandler.cs:15 message.
        Assert.Equal("Tournament does not exist", ex.Message);
        Assert.Equal("TOURNAMENT_NOT_FOUND", ex.Code);
    }

    [Fact]
    public void RejectsNotRunning()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated
        { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100, MinPlayers = 2 });
        var ex = Assert.Throws<CommandRejectedError>(() =>
            ProcessRebuyHandler.Handle(
                new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }) }, state));
        // Pin ProcessRebuyHandler.cs:19 message.
        Assert.Equal("Tournament is not running", ex.Message);
        Assert.Equal("TOURNAMENT_NOT_RUNNING", ex.Code);
    }

    [Fact]
    public void DeniesUnregistered()
    {
        // EU-0818: unregistered players now get a RebuyDenied event, not
        // a thrown error.
        var state = RebuyState();
        var result = ProcessRebuyHandler.Handle(
            new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 9, 9, 9 }) }, state);
        result.Should().BeOfType<RebuyDenied>();
        ((RebuyDenied)result).Reason.Should().Contain("not registered");
    }

    [Fact]
    public void DeniesWindowClosed()
    {
        var state = RebuyState();
        state.ApplyBlindAdvanced(new BlindLevelAdvanced { Level = 5 }); // Past cutoff of 4

        var result = ProcessRebuyHandler.Handle(
            new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }) }, state);

        result.Should().BeOfType<RebuyDenied>();
        ((RebuyDenied)result).Reason.Should().Contain("window closed");
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
        ((RebuyDenied)result).Reason.Should().Contain("Maximum rebuys");
    }

    [Fact]
    public void DeniesRebuysDisabled()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated
        {
            Name = "NoRebuy",
            BuyIn = 1000,
            StartingStack = 10000,
            MaxPlayers = 100,
            MinPlayers = 2,
            // No rebuy config
        });
        state.ApplyRegistrationOpened(new RegistrationOpened());
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
        { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }), FeePaid = 1000 });
        state.ApplyTournamentStarted(new TournamentStarted());

        var result = ProcessRebuyHandler.Handle(
            new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }) }, state);

        result.Should().BeOfType<RebuyDenied>();
        ((RebuyDenied)result).Reason.Should().Contain("not enabled");
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

    [Fact]
    public void Processes_WithoutCutoff_AllowsAnyLevel()
    {
        // Pins ProcessRebuyHandler.cs:44 RebuyLevelCutoff > 0 boundary —
        // mutant >= 0 would always check, but cutoff=0 must short-circuit.
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated
        {
            Name = "T",
            BuyIn = 1000,
            StartingStack = 10000,
            MaxPlayers = 100,
            MinPlayers = 2,
            RebuyConfig = new RebuyConfig
            {
                Enabled = true,
                MaxRebuys = 0,
                RebuyLevelCutoff = 0,
                RebuyCost = 1000,
                RebuyChips = 10000,
            },
        });
        state.ApplyRegistrationOpened(new RegistrationOpened());
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
        { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }), FeePaid = 1000 });
        state.ApplyTournamentStarted(new TournamentStarted());
        state.ApplyBlindAdvanced(new BlindLevelAdvanced { Level = 99 });

        var result = ProcessRebuyHandler.Handle(
            new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }) }, state);
        result.Should().BeOfType<RebuyProcessed>();
    }

    [Fact]
    public void Processes_WithoutMaxRebuys_AllowsManyRebuys()
    {
        // Pins ProcessRebuyHandler.cs:55 MaxRebuys > 0 boundary —
        // mutant >= 0 would always check; cutoff=0 disables.
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated
        {
            Name = "T",
            BuyIn = 1000,
            StartingStack = 10000,
            MaxPlayers = 100,
            MinPlayers = 2,
            RebuyConfig = new RebuyConfig
            {
                Enabled = true,
                MaxRebuys = 0,
                RebuyLevelCutoff = 5,
                RebuyCost = 1000,
                RebuyChips = 10000,
            },
        });
        state.ApplyRegistrationOpened(new RegistrationOpened());
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
        { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }), FeePaid = 1000 });
        state.ApplyTournamentStarted(new TournamentStarted());
        // Process 10 rebuys — mutant `>=0` would deny at first rebuy
        // (rebuysUsed=0 >= MaxRebuys=0); source `>0` skips check entirely.
        for (int i = 0; i < 10; i++)
            state.ApplyRebuyProcessed(new RebuyProcessed
            { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }), RebuyCount = i + 1 });

        var result = ProcessRebuyHandler.Handle(
            new ProcessRebuy { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }) }, state);
        result.Should().BeOfType<RebuyProcessed>();
    }
}
