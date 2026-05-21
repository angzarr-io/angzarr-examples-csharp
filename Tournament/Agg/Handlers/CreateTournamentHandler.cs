using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf.WellKnownTypes;

namespace Tournament.Agg.Handlers;

public static class CreateTournamentHandler
{
    public static TournamentCreated Handle(CreateTournament cmd, TournamentState state)
    {
        if (state.Exists)
            throw CommandRejectedError.PreconditionFailed(
                "TOURNAMENT_ALREADY_EXISTS",
                "Tournament already exists");
        if (string.IsNullOrEmpty(cmd.Name))
            throw CommandRejectedError.InvalidArgument(
                "NAME_REQUIRED",
                "name is required");
        if (cmd.BuyIn <= 0)
            throw CommandRejectedError.InvalidArgument(
                "BUY_IN_MUST_BE_POSITIVE",
                $"buy_in must be positive, got {cmd.BuyIn}");
        if (cmd.StartingStack <= 0)
            throw CommandRejectedError.InvalidArgument(
                "STARTING_STACK_MUST_BE_POSITIVE",
                $"starting_stack must be positive, got {cmd.StartingStack}");
        if (cmd.MinPlayers < 2)
            throw CommandRejectedError.InvalidArgument(
                "MIN_PLAYERS_TOO_FEW",
                $"min_players must be at least 2, got {cmd.MinPlayers}");
        if (cmd.MaxPlayers < 2)
            throw CommandRejectedError.InvalidArgument(
                "MAX_PLAYERS_TOO_FEW",
                $"max_players must be at least 2, got {cmd.MaxPlayers}");
        if (cmd.MinPlayers > cmd.MaxPlayers)
            throw CommandRejectedError.PreconditionFailed(
                "MIN_PLAYERS_EXCEEDS_MAX",
                $"min_players ({cmd.MinPlayers}) cannot exceed max_players ({cmd.MaxPlayers})",
                new Dictionary<string, string>
                {
                    ["lhs"] = cmd.MinPlayers.ToString(),
                    ["rhs"] = cmd.MaxPlayers.ToString(),
                });

        var evt = new TournamentCreated
        {
            Name = cmd.Name,
            GameVariant = cmd.GameVariant,
            BuyIn = cmd.BuyIn,
            StartingStack = cmd.StartingStack,
            MaxPlayers = cmd.MaxPlayers,
            MinPlayers = cmd.MinPlayers,
            RegistrationCutoffLevel = cmd.RegistrationCutoffLevel,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
        if (cmd.RebuyConfig != null) evt.RebuyConfig = cmd.RebuyConfig;
        evt.BlindStructure.AddRange(cmd.BlindStructure);
        evt.PayoutStructure.AddRange(cmd.PayoutStructure);
        return evt;
    }
}
