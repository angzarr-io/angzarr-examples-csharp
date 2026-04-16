using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf.WellKnownTypes;

namespace Tournament.Agg.Handlers;

public static class CreateTournamentHandler
{
    public static TournamentCreated Handle(CreateTournament cmd, TournamentState state)
    {
        if (state.Exists)
            throw new CommandRejectedError("Tournament already exists");
        if (string.IsNullOrEmpty(cmd.Name))
            throw new InvalidArgumentError("name is required");
        if (cmd.BuyIn <= 0)
            throw new InvalidArgumentError("buy_in must be positive");
        if (cmd.StartingStack <= 0)
            throw new InvalidArgumentError("starting_stack must be positive");
        if (cmd.MaxPlayers < 2)
            throw new InvalidArgumentError("max_players must be at least 2");

        var builder = new TournamentCreated
        {
            Name = cmd.Name,
            GameVariant = cmd.GameVariant,
            BuyIn = cmd.BuyIn,
            StartingStack = cmd.StartingStack,
            MaxPlayers = cmd.MaxPlayers,
            MinPlayers = cmd.MinPlayers,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
        if (cmd.RebuyConfig != null) builder.RebuyConfig = cmd.RebuyConfig;
        builder.BlindStructure.AddRange(cmd.BlindStructure);
        return builder;
    }
}
