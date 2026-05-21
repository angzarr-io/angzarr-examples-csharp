using Angzarr.Client;
using Angzarr.Examples;
using FluentAssertions;
using Tournament.Agg;
using Tournament.Agg.Handlers;
using Xunit;

namespace Tournament.Agg.Tests;

/// <summary>
/// Tests for <see cref="CreateTournamentHandler"/>.
///
/// Each guard branch is asserted on (a) thrown type, (b) StatusCode,
/// (c) structured Code, and (d) full message text — pinning every
/// string mutation Stryker can produce on the canonical error
/// constants.
/// </summary>
public class CreateHandlerTests
{
    private static CreateTournament Valid(string name = "T", long buyIn = 100,
        long startingStack = 1000, int maxPlayers = 9, int minPlayers = 2) =>
        new()
        {
            Name = name,
            BuyIn = buyIn,
            StartingStack = startingStack,
            MaxPlayers = maxPlayers,
            MinPlayers = minPlayers,
        };

    private static TournamentState Empty() => new();

    private static TournamentState Existing()
    {
        var s = new TournamentState();
        s.ApplyCreated(new TournamentCreated
        {
            Name = "Existing",
            BuyIn = 1000,
            StartingStack = 10000,
            MaxPlayers = 100,
            MinPlayers = 2,
        });
        return s;
    }

    [Fact]
    public void RejectsExistingTournament()
    {
        var ex = Assert.Throws<CommandRejectedError>(() =>
            CreateTournamentHandler.Handle(Valid(name: "New", buyIn: 500, startingStack: 5000, maxPlayers: 50),
                Existing()));
        // Exact message, exact StatusCode, exact structured Code.
        Assert.Equal("Tournament already exists", ex.Message);
        Assert.Equal("FAILED_PRECONDITION", ex.StatusCode);
        Assert.Equal("TOURNAMENT_ALREADY_EXISTS", ex.Code);
    }

    [Fact]
    public void RejectsEmptyName()
    {
        var ex = Assert.Throws<CommandRejectedError>(() =>
            CreateTournamentHandler.Handle(Valid(name: ""), Empty()));
        Assert.Equal("name is required", ex.Message);
        Assert.Equal("INVALID_ARGUMENT", ex.StatusCode);
        Assert.Equal("NAME_REQUIRED", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void RejectsNonPositiveBuyIn(long buyIn)
    {
        var ex = Assert.Throws<CommandRejectedError>(() =>
            CreateTournamentHandler.Handle(Valid(buyIn: buyIn), Empty()));
        Assert.Contains("buy_in must be positive", ex.Message);
        Assert.Contains(buyIn.ToString(), ex.Message);
        Assert.Equal("INVALID_ARGUMENT", ex.StatusCode);
        Assert.Equal("BUY_IN_MUST_BE_POSITIVE", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveStartingStack(long startingStack)
    {
        var ex = Assert.Throws<CommandRejectedError>(() =>
            CreateTournamentHandler.Handle(Valid(startingStack: startingStack), Empty()));
        Assert.Contains("starting_stack must be positive", ex.Message);
        Assert.Equal("INVALID_ARGUMENT", ex.StatusCode);
        Assert.Equal("STARTING_STACK_MUST_BE_POSITIVE", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void RejectsMinPlayersLessThan2(int minPlayers)
    {
        var ex = Assert.Throws<CommandRejectedError>(() =>
            CreateTournamentHandler.Handle(Valid(minPlayers: minPlayers, maxPlayers: 10), Empty()));
        Assert.Contains("min_players must be at least 2", ex.Message);
        Assert.Equal("INVALID_ARGUMENT", ex.StatusCode);
        Assert.Equal("MIN_PLAYERS_TOO_FEW", ex.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-5)]
    public void RejectsMaxPlayersLessThan2(int maxPlayers)
    {
        var ex = Assert.Throws<CommandRejectedError>(() =>
            CreateTournamentHandler.Handle(Valid(minPlayers: 2, maxPlayers: maxPlayers), Empty()));
        Assert.Contains("max_players must be at least 2", ex.Message);
        Assert.Equal("INVALID_ARGUMENT", ex.StatusCode);
        Assert.Equal("MAX_PLAYERS_TOO_FEW", ex.Code);
    }

    [Fact]
    public void AcceptsMaxPlayersExactly2Boundary()
    {
        // Pins CreateTournamentHandler.cs:31 cmd.MaxPlayers < 2 (boundary).
        // Mutant <= 2 would reject; the source accepts.
        var result = CreateTournamentHandler.Handle(Valid(minPlayers: 2, maxPlayers: 2), Empty());
        Assert.Equal(2, result.MaxPlayers);
    }

    [Fact]
    public void AcceptsMinPlayersExactly2Boundary()
    {
        var result = CreateTournamentHandler.Handle(Valid(minPlayers: 2, maxPlayers: 9), Empty());
        Assert.Equal(2, result.MinPlayers);
    }

    [Fact]
    public void RejectsMinPlayersGreaterThanMaxPlayers()
    {
        var ex = Assert.Throws<CommandRejectedError>(() =>
            CreateTournamentHandler.Handle(Valid(minPlayers: 10, maxPlayers: 5), Empty()));
        Assert.Contains("min_players (10) cannot exceed max_players (5)", ex.Message);
        Assert.Equal("FAILED_PRECONDITION", ex.StatusCode);
        Assert.Equal("MIN_PLAYERS_EXCEEDS_MAX", ex.Code);
        // Pin the structured details map keys (lhs, rhs).
        Assert.Equal("10", ex.Details["lhs"]);
        Assert.Equal("5", ex.Details["rhs"]);
    }

    [Fact]
    public void AcceptsMinPlayersEqualToMaxPlayers()
    {
        // Pins CreateTournamentHandler.cs:35 cmd.MinPlayers > cmd.MaxPlayers
        // (boundary). Mutant >= would reject equal; source accepts.
        var result = CreateTournamentHandler.Handle(Valid(minPlayers: 5, maxPlayers: 5), Empty());
        Assert.Equal(5, result.MinPlayers);
        Assert.Equal(5, result.MaxPlayers);
    }

    [Fact]
    public void RebuyConfig_NullOmittedFromEvent()
    {
        // Pins CreateTournamentHandler.cs:56 if (cmd.RebuyConfig != null).
        // Mutant `== null` would attempt to set null (which fails); the
        // source `!= null` correctly skips when null. Construct a command
        // WITHOUT a RebuyConfig and assert the event also lacks it.
        var result = CreateTournamentHandler.Handle(Valid(), Empty());
        Assert.Null(result.RebuyConfig);
    }

    [Fact]
    public void RebuyConfig_SetWhenProvided()
    {
        var cmd = Valid();
        cmd.RebuyConfig = new RebuyConfig
        {
            Enabled = true,
            MaxRebuys = 3,
            RebuyLevelCutoff = 5,
            RebuyCost = 100,
            RebuyChips = 1000,
        };
        var result = CreateTournamentHandler.Handle(cmd, Empty());
        Assert.NotNull(result.RebuyConfig);
        Assert.True(result.RebuyConfig.Enabled);
        Assert.Equal(3, result.RebuyConfig.MaxRebuys);
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
        }, Empty());

        result.Name.Should().Be("Sunday Million");
        result.GameVariant.Should().Be(GameVariant.TexasHoldem);
        result.BuyIn.Should().Be(1000);
        result.StartingStack.Should().Be(10000);
        result.MaxPlayers.Should().Be(100);
        result.MinPlayers.Should().Be(10);
        result.CreatedAt.Should().NotBeNull();
    }

    [Fact]
    public void BlindStructure_AppendedFromCommand()
    {
        var cmd = Valid();
        cmd.BlindStructure.Add(new BlindLevel { Level = 1, SmallBlind = 10, BigBlind = 20 });
        cmd.BlindStructure.Add(new BlindLevel { Level = 2, SmallBlind = 20, BigBlind = 40 });
        var result = CreateTournamentHandler.Handle(cmd, Empty());
        Assert.Equal(2, result.BlindStructure.Count);
        Assert.Equal(20, result.BlindStructure[1].SmallBlind);
    }

    [Fact]
    public void PayoutStructure_AppendedFromCommand()
    {
        var cmd = Valid();
        cmd.PayoutStructure.Add(new PayoutPosition { Position = 1, Percentage = 60 });
        cmd.PayoutStructure.Add(new PayoutPosition { Position = 2, Percentage = 40 });
        var result = CreateTournamentHandler.Handle(cmd, Empty());
        Assert.Equal(2, result.PayoutStructure.Count);
        Assert.Equal(60, result.PayoutStructure[0].Percentage);
    }
}
