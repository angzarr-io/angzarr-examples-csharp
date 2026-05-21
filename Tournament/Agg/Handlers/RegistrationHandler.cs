using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Tournament.Agg.Handlers;

public static class RegistrationHandler
{
    public static RegistrationOpened HandleOpenRegistration(OpenRegistration cmd, TournamentState state)
    {
        if (!state.Exists)
            throw CommandRejectedError.PreconditionFailed(
                "TOURNAMENT_NOT_FOUND",
                "Tournament does not exist");
        if (state.IsRunning)
            throw CommandRejectedError.PreconditionFailed(
                "CANNOT_OPEN_REGISTRATION_RUNNING",
                "Cannot open registration on a running tournament");
        if (state.IsRegistrationOpen)
            throw CommandRejectedError.PreconditionFailed(
                "REGISTRATION_ALREADY_OPEN",
                "Registration is already open");

        return new RegistrationOpened { OpenedAt = Timestamp.FromDateTime(DateTime.UtcNow) };
    }

    public static RegistrationClosed HandleCloseRegistration(CloseRegistration cmd, TournamentState state)
    {
        // Late registration may still be open while the tournament is running;
        // CloseRegistration is valid in both RegistrationOpen and Running phases
        // as long as the tournament has open registration semantics.
        if (!state.IsRegistrationOpen && !state.IsRunning)
            throw CommandRejectedError.PreconditionFailed(
                "REGISTRATION_NOT_OPEN",
                "Registration is not open");

        return new RegistrationClosed
        {
            TotalRegistrations = state.RegisteredPlayers.Count,
            ClosedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    /// <summary>
    /// Returns true when the running tournament has crossed its
    /// registration_cutoff_level, after which late registration is closed.
    /// </summary>
    public static bool LateRegistrationClosed(TournamentState state)
    {
        if (state.RegistrationCutoffLevel <= 0) return false;
        return state.CurrentLevel > state.RegistrationCutoffLevel;
    }

    public static IMessage HandleEnrollPlayer(EnrollPlayer cmd, TournamentState state)
    {
        // EU-0824: the tournament must exist to enroll players.
        if (!state.Exists)
            throw CommandRejectedError.PreconditionFailed(
                "TOURNAMENT_NOT_FOUND",
                "Tournament does not exist");

        if (cmd.PlayerRoot.IsEmpty)
            return new TournamentEnrollmentRejected
            {
                PlayerRoot = cmd.PlayerRoot,
                ReservationId = cmd.ReservationId,
                Reason = "player_root is required",
                RejectedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            };

        var rootHex = Convert.ToHexString(cmd.PlayerRoot.ToByteArray()).ToLowerInvariant();

        // Registration must be open. Late-reg semantics: running tournaments
        // with registration still open accept enrollments unless cutoff hit.
        var registrationActive = state.IsRegistrationOpen
            || (state.IsRunning && state.RegistrationCutoffLevel > 0 && !LateRegistrationClosed(state));
        if (!registrationActive)
            return new TournamentEnrollmentRejected
            {
                PlayerRoot = cmd.PlayerRoot,
                ReservationId = cmd.ReservationId,
                Reason = "Registration is not open",
                RejectedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            };

        if (state.IsFull)
            return new TournamentEnrollmentRejected
            {
                PlayerRoot = cmd.PlayerRoot,
                ReservationId = cmd.ReservationId,
                Reason = "Tournament is full",
                RejectedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            };

        if (state.IsPlayerRegistered(rootHex))
            return new TournamentEnrollmentRejected
            {
                PlayerRoot = cmd.PlayerRoot,
                ReservationId = cmd.ReservationId,
                Reason = "Player already registered",
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
