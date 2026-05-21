using Angzarr.Client;
using Angzarr.Examples;
using FluentAssertions;
using Google.Protobuf;
using Tournament.Agg;
using Tournament.Agg.Handlers;
using Xunit;

namespace Tournament.Agg.Tests;

public class RegistrationHandlerTests
{
    private static TournamentState OpenState()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated
        {
            Name = "Test",
            BuyIn = 1000,
            StartingStack = 10000,
            MaxPlayers = 100,
            MinPlayers = 2
        });
        state.ApplyRegistrationOpened(new RegistrationOpened());
        return state;
    }

    [Fact]
    public void Open_RejectsNonExistent()
    {
        var ex = Assert.Throws<CommandRejectedError>(() =>
            RegistrationHandler.HandleOpenRegistration(new OpenRegistration(), new TournamentState()));
        // Pin RegistrationHandler.cs:15 message.
        Assert.Equal("Tournament does not exist", ex.Message);
        Assert.Equal("TOURNAMENT_NOT_FOUND", ex.Code);
    }

    [Fact]
    public void Open_RejectsAlreadyOpen()
    {
        var state = OpenState();
        var ex = Assert.Throws<CommandRejectedError>(() =>
            RegistrationHandler.HandleOpenRegistration(new OpenRegistration(), state));
        // Pin RegistrationHandler.cs:22 message.
        Assert.Equal("Registration is already open", ex.Message);
        Assert.Equal("REGISTRATION_ALREADY_OPEN", ex.Code);
    }

    [Fact]
    public void Open_RejectsRunning()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });
        state.ApplyTournamentStarted(new TournamentStarted());
        var ex = Assert.Throws<CommandRejectedError>(() =>
            RegistrationHandler.HandleOpenRegistration(new OpenRegistration(), state));
        // Pin RegistrationHandler.cs:18 message.
        Assert.Equal("Cannot open registration on a running tournament", ex.Message);
        Assert.Equal("CANNOT_OPEN_REGISTRATION_RUNNING", ex.Code);
    }

    [Fact]
    public void Open_Success()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });
        var result = RegistrationHandler.HandleOpenRegistration(new OpenRegistration(), state);
        result.Should().NotBeNull();
        result.OpenedAt.Should().NotBeNull();
    }

    [Fact]
    public void Close_RejectsNotOpen()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });
        var ex = Assert.Throws<CommandRejectedError>(() =>
            RegistrationHandler.HandleCloseRegistration(new CloseRegistration(), state));
        // Pin RegistrationHandler.cs:35 message.
        Assert.Equal("Registration is not open", ex.Message);
        Assert.Equal("REGISTRATION_NOT_OPEN", ex.Code);
    }

    [Fact]
    public void Close_IncludesTotalRegistrations()
    {
        var state = OpenState();
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
        { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }), FeePaid = 1000 });
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
        { PlayerRoot = ByteString.CopyFrom(new byte[] { 2 }), FeePaid = 1000 });

        var result = RegistrationHandler.HandleCloseRegistration(new CloseRegistration(), state);
        result.TotalRegistrations.Should().Be(2);
    }

    [Fact]
    public void Enroll_RejectsClosed()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });

        var result = RegistrationHandler.HandleEnrollPlayer(
            new EnrollPlayer { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }) }, state);

        result.Should().BeOfType<TournamentEnrollmentRejected>();
        ((TournamentEnrollmentRejected)result).Reason.Should().Contain("not open");
    }

    [Fact]
    public void Enroll_RejectsFull()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 2, MinPlayers = 2 });
        state.ApplyRegistrationOpened(new RegistrationOpened());
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
        { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }), FeePaid = 1000 });
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
        { PlayerRoot = ByteString.CopyFrom(new byte[] { 2 }), FeePaid = 1000 });

        var result = RegistrationHandler.HandleEnrollPlayer(
            new EnrollPlayer { PlayerRoot = ByteString.CopyFrom(new byte[] { 3 }) }, state);

        result.Should().BeOfType<TournamentEnrollmentRejected>();
        ((TournamentEnrollmentRejected)result).Reason.Should().Contain("full");
    }

    [Fact]
    public void Enroll_RejectsDuplicate()
    {
        var state = OpenState();
        var root = ByteString.CopyFrom(new byte[] { 1, 2, 3 });
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled { PlayerRoot = root, FeePaid = 1000 });

        var result = RegistrationHandler.HandleEnrollPlayer(new EnrollPlayer { PlayerRoot = root }, state);

        result.Should().BeOfType<TournamentEnrollmentRejected>();
        ((TournamentEnrollmentRejected)result).Reason.Should().Contain("already registered");
    }

    [Fact]
    public void Enroll_RejectsEmptyPlayerRoot()
    {
        // EU-0824-followup: empty player_root is rejected as a domain event.
        var state = OpenState();
        var result = RegistrationHandler.HandleEnrollPlayer(new EnrollPlayer(), state);
        result.Should().BeOfType<TournamentEnrollmentRejected>();
        ((TournamentEnrollmentRejected)result).Reason.Should().Contain("required");
    }

    [Fact]
    public void Enroll_RejectsNonExistent()
    {
        var ex = Assert.Throws<CommandRejectedError>(() =>
            RegistrationHandler.HandleEnrollPlayer(
                new EnrollPlayer { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }) },
                new TournamentState()));
        // Pin RegistrationHandler.cs:60 message.
        Assert.Equal("Tournament does not exist", ex.Message);
        Assert.Equal("TOURNAMENT_NOT_FOUND", ex.Code);
    }

    [Fact]
    public void Enroll_Success()
    {
        var state = OpenState();
        var result = RegistrationHandler.HandleEnrollPlayer(
            new EnrollPlayer { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }) }, state);

        result.Should().BeOfType<TournamentPlayerEnrolled>();
        var enrolled = (TournamentPlayerEnrolled)result;
        enrolled.FeePaid.Should().Be(1000);
        enrolled.StartingStack.Should().Be(10000);
        enrolled.RegistrationNumber.Should().Be(1);
    }

    // ===== LateRegistrationClosed =====

    [Fact]
    public void LateRegistrationClosed_NoCutoff_AlwaysFalse()
    {
        var state = new TournamentState();
        state.RegistrationCutoffLevel = 0;
        Assert.False(RegistrationHandler.LateRegistrationClosed(state));
    }

    [Fact]
    public void LateRegistrationClosed_NegativeCutoff_AlwaysFalse()
    {
        var state = new TournamentState();
        state.RegistrationCutoffLevel = -1;
        Assert.False(RegistrationHandler.LateRegistrationClosed(state));
    }

    [Fact]
    public void LateRegistrationClosed_BelowCutoff_False()
    {
        var state = new TournamentState();
        state.RegistrationCutoffLevel = 5;
        state.CurrentLevel = 3;
        Assert.False(RegistrationHandler.LateRegistrationClosed(state));
    }

    [Fact]
    public void LateRegistrationClosed_AtCutoff_False()
    {
        // Boundary: CurrentLevel == CutoffLevel must NOT close (source > only).
        var state = new TournamentState();
        state.RegistrationCutoffLevel = 5;
        state.CurrentLevel = 5;
        Assert.False(RegistrationHandler.LateRegistrationClosed(state));
    }

    [Fact]
    public void LateRegistrationClosed_PastCutoff_True()
    {
        var state = new TournamentState();
        state.RegistrationCutoffLevel = 5;
        state.CurrentLevel = 6;
        Assert.True(RegistrationHandler.LateRegistrationClosed(state));
    }

    // ===== Late-reg enrollment during running tournament =====

    [Fact]
    public void Enroll_RunningWithLateRegOpen_Allows()
    {
        // Running + cutoff>0 + currentLevel <= cutoff → enrolls.
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated
        {
            Name = "T",
            BuyIn = 100,
            StartingStack = 1000,
            MaxPlayers = 9,
            MinPlayers = 2,
            RegistrationCutoffLevel = 5,
        });
        state.ApplyRegistrationOpened(new RegistrationOpened());
        state.ApplyTournamentStarted(new TournamentStarted());
        state.ApplyBlindAdvanced(new BlindLevelAdvanced { Level = 3 });

        var result = RegistrationHandler.HandleEnrollPlayer(
            new EnrollPlayer { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }) }, state);
        result.Should().BeOfType<TournamentPlayerEnrolled>();
    }

    [Fact]
    public void Close_RejectsWhenNotOpenAndNotRunning()
    {
        // Source: rejects when NOT (open OR running).
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated
        {
            Name = "T",
            BuyIn = 100,
            StartingStack = 1000,
            MaxPlayers = 9,
            MinPlayers = 2,
        });
        var act = () => RegistrationHandler.HandleCloseRegistration(new CloseRegistration(), state);
        act.Should().Throw<CommandRejectedError>().WithMessage("*not open*");
    }

    [Fact]
    public void Close_DuringRunning_Succeeds()
    {
        // Late-reg semantics: tournament is running + registration open
        // (or running alone) — Close is allowed.
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
        var result = RegistrationHandler.HandleCloseRegistration(new CloseRegistration(), state);
        result.Should().NotBeNull();
    }
}
