using Angzarr.Client;
using Angzarr.Examples;
using FluentAssertions;
using Tournament.Agg;
using Tournament.Agg.Handlers;
using Xunit;

namespace Tournament.Agg.Tests;

public class CreateHandlerTests
{
    [Fact]
    public void RejectsExistingTournament()
    {
        var state = new TournamentState();
        state.ApplyCreated(new TournamentCreated { Name = "Existing", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 });

        var act = () => CreateTournamentHandler.Handle(new CreateTournament
        { Name = "New", BuyIn = 500, StartingStack = 5000, MaxPlayers = 50 }, state);

        act.Should().Throw<CommandRejectedError>().WithMessage("*already exists*");
    }

    [Fact]
    public void RejectsEmptyName()
    {
        var act = () => CreateTournamentHandler.Handle(new CreateTournament
        { Name = "", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100 }, new TournamentState());

        act.Should().Throw<InvalidArgumentError>().WithMessage("*name*");
    }

    [Fact]
    public void RejectsZeroBuyIn()
    {
        var act = () => CreateTournamentHandler.Handle(new CreateTournament
        { Name = "Test", BuyIn = 0, StartingStack = 10000, MaxPlayers = 100 }, new TournamentState());

        act.Should().Throw<InvalidArgumentError>().WithMessage("*positive*");
    }

    [Fact]
    public void RejectsZeroStartingStack()
    {
        var act = () => CreateTournamentHandler.Handle(new CreateTournament
        { Name = "Test", BuyIn = 1000, StartingStack = 0, MaxPlayers = 100 }, new TournamentState());

        act.Should().Throw<InvalidArgumentError>().WithMessage("*positive*");
    }

    [Fact]
    public void RejectsMaxPlayersLessThan2()
    {
        var act = () => CreateTournamentHandler.Handle(new CreateTournament
        { Name = "Test", BuyIn = 1000, StartingStack = 10000, MaxPlayers = 1 }, new TournamentState());

        act.Should().Throw<InvalidArgumentError>().WithMessage("*max_players*");
    }

    [Fact]
    public void SetsAllFieldsOnSuccess()
    {
        var result = CreateTournamentHandler.Handle(new CreateTournament
        {
            Name = "Sunday Million",
            GameVariant = GameVariant.TexasHoldem,
            BuyIn = 1000,
            StartingStack = 10000,
            MaxPlayers = 100,
            MinPlayers = 10,
        }, new TournamentState());

        result.Name.Should().Be("Sunday Million");
        result.GameVariant.Should().Be(GameVariant.TexasHoldem);
        result.BuyIn.Should().Be(1000);
        result.StartingStack.Should().Be(10000);
        result.MaxPlayers.Should().Be(100);
        result.MinPlayers.Should().Be(10);
        result.CreatedAt.Should().NotBeNull();
    }
}
