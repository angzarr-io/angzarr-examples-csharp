using Angzarr.Client;
using Angzarr.Examples;
using Player.Agg.Handlers;
using Xunit;

namespace Player.Agg.Tests;

// docs:start:unit_test_deposit
public class DepositHandlerTests
{
    [Fact]
    public void TestDepositIncreasesBankroll()
    {
        var state = new PlayerState { PlayerId = "player_1", Bankroll = 1000 };
        var cmd = new DepositFunds
        {
            Amount = new Currency { Amount = 500, CurrencyCode = "CHIPS" },
        };

        var evt = DepositFundsHandler.Handle(cmd, state);

        Assert.Equal(1500, evt.NewBalance.Amount);
    }

    [Fact]
    public void TestDepositRejectsNonExistentPlayer()
    {
        var state = new PlayerState(); // PlayerId empty = doesn't exist
        var cmd = new DepositFunds
        {
            Amount = new Currency { Amount = 500, CurrencyCode = "CHIPS" },
        };

        var ex = Assert.Throws<CommandRejectedError>(() => DepositFundsHandler.Handle(cmd, state));

        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void TestDepositRejectsZeroAmount()
    {
        var state = new PlayerState { PlayerId = "p" };
        var cmd = new DepositFunds
        {
            Amount = new Currency { Amount = 0, CurrencyCode = "CHIPS" },
        };

        var ex = Assert.Throws<CommandRejectedError>(() => DepositFundsHandler.Handle(cmd, state));

        Assert.Contains("positive", ex.Message);
    }
}
// docs:end:unit_test_deposit
