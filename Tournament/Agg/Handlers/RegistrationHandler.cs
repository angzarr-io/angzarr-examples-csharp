using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Tournament.Agg.Handlers;

public static class RegistrationHandler
{
    public static RegistrationOpened HandleOpen(OpenRegistration cmd, TournamentState state)
    {
        if (!state.Exists) throw new CommandRejectedError("Tournament does not exist");
        if (state.IsRegistrationOpen) throw new CommandRejectedError("Registration already open");
        if (state.IsRunning) throw new CommandRejectedError("Tournament is running");

        return new RegistrationOpened { OpenedAt = Timestamp.FromDateTime(DateTime.UtcNow) };
    }

    public static RegistrationClosed HandleClose(CloseRegistration cmd, TournamentState state)
    {
        if (!state.IsRegistrationOpen) throw new CommandRejectedError("Registration not open");

        return new RegistrationClosed
        {
            TotalRegistrations = state.RegisteredPlayers.Count,
            ClosedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    public static IMessage HandleEnroll(EnrollPlayer cmd, TournamentState state)
    {
        var rootHex = Convert.ToHexString(cmd.PlayerRoot.ToByteArray()).ToLowerInvariant();

        if (!state.IsRegistrationOpen)
            return new TournamentEnrollmentRejected
            {
                PlayerRoot = cmd.PlayerRoot,
                ReservationId = cmd.ReservationId,
                Reason = "closed",
                RejectedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            };

        if (state.IsFull)
            return new TournamentEnrollmentRejected
            {
                PlayerRoot = cmd.PlayerRoot,
                ReservationId = cmd.ReservationId,
                Reason = "full",
                RejectedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            };

        if (state.IsPlayerRegistered(rootHex))
            return new TournamentEnrollmentRejected
            {
                PlayerRoot = cmd.PlayerRoot,
                ReservationId = cmd.ReservationId,
                Reason = "already_registered",
                RejectedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            };

        return new TournamentPlayerEnrolled
        {
            PlayerRoot = cmd.PlayerRoot,
            ReservationId = cmd.ReservationId,
            FeePaid = state.BuyIn,
            StartingStack = state.StartingStack,
            RegistrationNumber = state.RegisteredPlayers.Count + 1,
            EnrolledAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }
}
