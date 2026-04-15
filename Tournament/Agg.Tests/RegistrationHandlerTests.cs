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
            Name = "Test", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100, MinPlayers = 2
        });
        state.ApplyRegistrationOpened(new RegistrationOpened());
        return state;
    }

    [Fact]
    public void Open_RejectsNonExistent()
    {
        var act = () => RegistrationHandler.HandleOpen(new OpenRegistration(), new TournamentState());
        act.Should().Throw<CommandRejectedError>().WithMessage("*does not exist*");
    }

    [Fact]
    public void Open_RejectsAlreadyOpen()
    {
        var state = OpenState();
        var act = () => RegistrationHandler.HandleOpen(new OpenRegistration(), state);
        act.Should().Throw<CommandRejectedError>().WithMessage("*already open*");
    }

    [Fact]
    public void Open_RejectsRunning()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });
        state.ApplyTournamentStarted(new TournamentStarted());
        var act = () => RegistrationHandler.HandleOpen(new OpenRegistration(), state);
        act.Should().Throw<CommandRejectedError>().WithMessage("*running*");
    }

    [Fact]
    public void Open_Success()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });
        var result = RegistrationHandler.HandleOpen(new OpenRegistration(), state);
        result.Should().NotBeNull();
        result.OpenedAt.Should().NotBeNull();
    }

    [Fact]
    public void Close_RejectsNotOpen()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });
        var act = () => RegistrationHandler.HandleClose(new CloseRegistration(), state);
        act.Should().Throw<CommandRejectedError>().WithMessage("*not open*");
    }

    [Fact]
    public void Close_IncludesTotalRegistrations()
    {
        var state = OpenState();
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
            { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }), FeePaid = 1000 });
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
            { PlayerRoot = ByteString.CopyFrom(new byte[] { 2 }), FeePaid = 1000 });

        var result = RegistrationHandler.HandleClose(new CloseRegistration(), state);
        result.TotalRegistrations.Should().Be(2);
    }

    [Fact]
    public void Enroll_RejectsClosed()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });

        var result = RegistrationHandler.HandleEnroll(
            new EnrollPlayer { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }) }, state);

        result.Should().BeOfType<TournamentEnrollmentRejected>();
        ((TournamentEnrollmentRejected)result).Reason.Should().Be("closed");
    }

    [Fact]
    public void Enroll_RejectsFull()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "T", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 2 });
        state.ApplyRegistrationOpened(new RegistrationOpened());
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
            { PlayerRoot = ByteString.CopyFrom(new byte[] { 1 }), FeePaid = 1000 });
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled
            { PlayerRoot = ByteString.CopyFrom(new byte[] { 2 }), FeePaid = 1000 });

        var result = RegistrationHandler.HandleEnroll(
            new EnrollPlayer { PlayerRoot = ByteString.CopyFrom(new byte[] { 3 }) }, state);

        result.Should().BeOfType<TournamentEnrollmentRejected>();
        ((TournamentEnrollmentRejected)result).Reason.Should().Be("full");
    }

    [Fact]
    public void Enroll_RejectsDuplicate()
    {
        var state = OpenState();
        var root = ByteString.CopyFrom(new byte[] { 1, 2, 3 });
        state.ApplyPlayerEnrolled(new TournamentPlayerEnrolled { PlayerRoot = root, FeePaid = 1000 });

        var result = RegistrationHandler.HandleEnroll(new EnrollPlayer { PlayerRoot = root }, state);

        result.Should().BeOfType<TournamentEnrollmentRejected>();
        ((TournamentEnrollmentRejected)result).Reason.Should().Be("already_registered");
    }

    [Fact]
    public void Enroll_Success()
    {
        var state = OpenState();
        var result = RegistrationHandler.HandleEnroll(
            new EnrollPlayer { PlayerRoot = ByteString.CopyFrom(new byte[] { 1, 2, 3 }) }, state);

        result.Should().BeOfType<TournamentPlayerEnrolled>();
        var enrolled = (TournamentPlayerEnrolled)result;
        enrolled.FeePaid.Should().Be(1000);
        enrolled.StartingStack.Should().Be(10000);
        enrolled.RegistrationNumber.Should().Be(1);
    }
}
