using Angzarr;
using Angzarr.Examples;
using FluentAssertions;
using Google.Protobuf;
using NUnit.Framework;
using TechTalk.SpecFlow;
using Tests.Client;

namespace Tests.Steps.Acceptance;

/// <summary>
/// Step definitions for acceptance-level player scenarios.
/// Uses ICommandClient to send commands either in-process or via gRPC.
/// </summary>
[Binding]
public class AcceptancePlayerSteps
{
    private readonly ScenarioContext _context;

    public AcceptancePlayerSteps(ScenarioContext context)
    {
        _context = context;
    }

    private ICommandClient Client => (ICommandClient)_context["commandClient"];

    private Dictionary<string, UUID> PlayerIds
    {
        get
        {
            if (!_context.TryGetValue("playerIds", out Dictionary<string, UUID>? ids))
            {
                ids = new Dictionary<string, UUID>();
                _context["playerIds"] = ids;
            }
            return ids!;
        }
    }

    private Dictionary<string, uint> PlayerSequences
    {
        get
        {
            if (!_context.TryGetValue("playerSequences", out Dictionary<string, uint>? seqs))
            {
                seqs = new Dictionary<string, uint>();
                _context["playerSequences"] = seqs;
            }
            return seqs!;
        }
    }

    private UUID GetOrCreatePlayerId(string name)
    {
        if (!PlayerIds.TryGetValue(name, out var id))
        {
            id = new UUID { Value = ByteString.CopyFrom(Guid.NewGuid().ToByteArray()) };
            PlayerIds[name] = id;
        }
        return id;
    }

    private uint NextSequence(string name)
    {
        if (!PlayerSequences.TryGetValue(name, out var seq))
            seq = 0;
        PlayerSequences[name] = seq + 1;
        return seq;
    }

    [When(@"I register player ""(.*)"" with email ""(.*)""")]
    public async Task WhenIRegisterPlayerWithEmail(string name, string email)
    {
        var playerId = GetOrCreatePlayerId(name);
        var cmd = new RegisterPlayer
        {
            DisplayName = name,
            Email = email,
            PlayerType = PlayerType.Human,
        };

        var response = await Client.SendCommandAsync("player", playerId, cmd, NextSequence(name));
        response.Events.Should().NotBeNull();
        response.Events.Pages.Should().NotBeEmpty();
        _context[$"lastPlayerResponse:{name}"] = response;
    }

    [When(@"I deposit (\d+) chips to player ""(.*)""")]
    public async Task WhenIDepositChipsToPlayer(int amount, string name)
    {
        var playerId = GetOrCreatePlayerId(name);
        var cmd = new DepositFunds
        {
            Amount = new Currency { Amount = amount, CurrencyCode = "CHIPS" },
        };

        var response = await Client.SendCommandAsync("player", playerId, cmd, NextSequence(name));
        response.Events.Should().NotBeNull();
        response.Events.Pages.Should().NotBeEmpty();
        _context[$"lastPlayerResponse:{name}"] = response;
    }

    [Then(@"player ""(.*)"" has bankroll (\d+)")]
    public void ThenPlayerHasBankroll(string name, int amount)
    {
        // For in-process, we can check the last deposited event's new_balance.
        // For gRPC, the response events contain the state we need.
        var response = (CommandResponse)_context[$"lastPlayerResponse:{name}"];
        var lastPage = response.Events.Pages[^1];

        if (lastPage.Event.TypeUrl.EndsWith("FundsDeposited"))
        {
            var evt = lastPage.Event.Unpack<FundsDeposited>();
            evt.NewBalance!.Amount.Should().Be(amount);
        }
        else if (lastPage.Event.TypeUrl.EndsWith("PlayerRegistered"))
        {
            // Bankroll is 0 at registration
            amount.Should().Be(0);
        }
        else
        {
            Assert.Fail($"Unexpected last event type: {lastPage.Event.TypeUrl}");
        }
    }

    [Then(@"player ""(.*)"" has available balance (\d+)")]
    public void ThenPlayerHasAvailableBalance(string name, int amount)
    {
        // Available balance = bankroll - reserved.
        // For the first scenario (register + deposit), reserved is 0.
        var response = (CommandResponse)_context[$"lastPlayerResponse:{name}"];
        var lastPage = response.Events.Pages[^1];

        if (lastPage.Event.TypeUrl.EndsWith("FundsDeposited"))
        {
            var evt = lastPage.Event.Unpack<FundsDeposited>();
            evt.NewBalance!.Amount.Should().Be(amount);
        }
    }

    [Then(@"player ""(.*)"" has reserved funds (\d+)")]
    public void ThenPlayerHasReservedFunds(string name, int amount)
    {
        // Check the last FundsReserved or FundsReleased event.
        if (
            _context.TryGetValue($"lastPlayerResponse:{name}", out object? obj)
            && obj is CommandResponse response
        )
        {
            var lastPage = response.Events.Pages[^1];

            if (lastPage.Event.TypeUrl.EndsWith("FundsReserved"))
            {
                var evt = lastPage.Event.Unpack<FundsReserved>();
                evt.NewReservedBalance!.Amount.Should().Be(amount);
            }
            else if (lastPage.Event.TypeUrl.EndsWith("FundsReleased"))
            {
                var evt = lastPage.Event.Unpack<FundsReleased>();
                evt.NewReservedBalance!.Amount.Should().Be(amount);
            }
            else if (amount == 0)
            {
                // No reservation events yet, reserved is 0.
            }
            else
            {
                Assert.Fail(
                    $"Expected reserved funds {amount} but last event was: {lastPage.Event.TypeUrl}"
                );
            }
        }
        else if (amount == 0)
        {
            // No player response yet, reserved is 0.
        }
        else
        {
            Assert.Fail($"No player response found for {name}");
        }
    }

    [Given(@"registered players with bankroll:")]
    public async Task GivenRegisteredPlayersWithBankroll(TechTalk.SpecFlow.Table table)
    {
        foreach (var row in table.Rows)
        {
            var name = row["name"];
            var bankroll = int.Parse(row["bankroll"]);
            var playerId = GetOrCreatePlayerId(name);

            var registerCmd = new RegisterPlayer
            {
                DisplayName = name,
                Email = $"{name.ToLower()}@example.com",
                PlayerType = PlayerType.Human,
            };
            var regResponse = await Client.SendCommandAsync(
                "player",
                playerId,
                registerCmd,
                NextSequence(name)
            );
            regResponse.Events.Should().NotBeNull();

            if (bankroll > 0)
            {
                var depositCmd = new DepositFunds
                {
                    Amount = new Currency { Amount = bankroll, CurrencyCode = "CHIPS" },
                };
                var depResponse = await Client.SendCommandAsync(
                    "player",
                    playerId,
                    depositCmd,
                    NextSequence(name)
                );
                depResponse.Events.Should().NotBeNull();
                _context[$"lastPlayerResponse:{name}"] = depResponse;
            }
            else
            {
                _context[$"lastPlayerResponse:{name}"] = regResponse;
            }
        }
    }
}
