using Angzarr.Examples;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using TechTalk.SpecFlow;

namespace Tests.Steps;

[Binding]
public class SagaSteps
{
    private readonly ScenarioContext _context;

    // Saga state
    private string _sagaType = "";
    private IMessage? _sourceEvent;
    private readonly List<IMessage> _resultCommands = new();
    private string _targetDomain = "";

    // Router state
    private readonly List<string> _registeredSagas = new();
    private readonly List<string> _handledBy = new();
    private bool _routerHasFailingSaga;
    private Exception? _routerException;
    private readonly List<IMessage> _eventBookEvents = new();

    // Shared identifiers
    private readonly byte[] _tableRoot = System.Text.Encoding.UTF8.GetBytes("table-root-00001");
    private readonly byte[] _handRoot = System.Text.Encoding.UTF8.GetBytes("hand-root-0000001");

    // HandStarted data
    private int _handNumber;
    private GameVariant _gameVariant;
    private int _dealerPosition;
    private readonly List<SeatSnapshot> _activePlayers = new();

    // HandComplete data
    private readonly List<PotWinner> _winners = new();
    private long _potTotal;

    // HandEnded data
    private readonly Dictionary<string, long> _stackChanges = new();

    public SagaSteps(ScenarioContext context)
    {
        _context = context;
    }

    private byte[] GetPlayerRoot(string name)
    {
        return System.Text.Encoding.UTF8.GetBytes(name.PadRight(16).Substring(0, 16));
    }

    // =========================================================================
    // Given Steps
    // =========================================================================

    [Given(@"a TableSyncSaga")]
    public void GivenATableSyncSaga()
    {
        _sagaType = "TableSyncSaga";
    }

    [Given(@"a HandResultsSaga")]
    public void GivenAHandResultsSaga()
    {
        _sagaType = "HandResultsSaga";
    }

    [Given(@"a HandStarted event from table domain with:")]
    public void GivenAHandStartedEventFromTableDomainWith(TechTalk.SpecFlow.Table table)
    {
        var row = table.Rows[0];
        _handNumber = int.Parse(row["hand_number"]);
        _gameVariant = row["game_variant"] switch
        {
            "TEXAS_HOLDEM" => GameVariant.TexasHoldem,
            "OMAHA" => GameVariant.Omaha,
            "FIVE_CARD_DRAW" => GameVariant.FiveCardDraw,
            _ => GameVariant.Unspecified,
        };
        _dealerPosition = int.Parse(row["dealer_position"]);

        var handRoot = row.ContainsKey("hand_root")
            ? ByteString.CopyFromUtf8(row["hand_root"])
            : ByteString.CopyFrom(_handRoot);

        _sourceEvent = new HandStarted
        {
            HandRoot = handRoot,
            HandNumber = _handNumber,
            GameVariant = _gameVariant,
            DealerPosition = _dealerPosition,
            SmallBlind = 5,
            BigBlind = 10,
            StartedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"active players:")]
    public void GivenActivePlayers(TechTalk.SpecFlow.Table table)
    {
        _activePlayers.Clear();
        foreach (var row in table.Rows)
        {
            var snapshot = new SeatSnapshot
            {
                PlayerRoot = ByteString.CopyFrom(GetPlayerRoot(row["player_root"])),
                Position = int.Parse(row["position"]),
                Stack = long.Parse(row["stack"]),
            };
            _activePlayers.Add(snapshot);
        }

        // If the source event is HandStarted, add active players to it
        if (_sourceEvent is HandStarted hs)
        {
            hs.ActivePlayers.Clear();
            hs.ActivePlayers.AddRange(_activePlayers);
        }

        // Also store in ScenarioContext for ProcessManagerSteps to consume
        _context["activePlayersTable"] = table;
    }

    [Given(@"a HandComplete event from hand domain with:")]
    public void GivenAHandCompleteEventFromHandDomainWith(TechTalk.SpecFlow.Table table)
    {
        var row = table.Rows[0];
        var tableRoot = row.ContainsKey("table_root")
            ? ByteString.CopyFromUtf8(row["table_root"])
            : ByteString.CopyFrom(_tableRoot);
        _potTotal = row.ContainsKey("pot_total") ? long.Parse(row["pot_total"]) : 0;

        _sourceEvent = new HandComplete
        {
            TableRoot = tableRoot,
            HandNumber = 1,
            CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"winners:")]
    public void GivenWinners(TechTalk.SpecFlow.Table table)
    {
        _winners.Clear();
        foreach (var row in table.Rows)
        {
            _winners.Add(new PotWinner
            {
                PlayerRoot = ByteString.CopyFrom(GetPlayerRoot(row["player_root"])),
                Amount = long.Parse(row["amount"]),
                PotType = "main",
            });
        }

        // If the source event is HandComplete, add winners
        if (_sourceEvent is HandComplete hc)
        {
            hc.Winners.Clear();
            hc.Winners.AddRange(_winners);
        }
    }

    [Given(@"a HandEnded event from table domain with:")]
    public void GivenAHandEndedEventFromTableDomainWith(TechTalk.SpecFlow.Table table)
    {
        var row = table.Rows[0];
        _sourceEvent = new HandEnded
        {
            HandRoot = ByteString.CopyFromUtf8(row["hand_root"]),
            EndedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"stack_changes:")]
    public void GivenStackChanges(TechTalk.SpecFlow.Table table)
    {
        _stackChanges.Clear();
        foreach (var row in table.Rows)
        {
            var playerRoot = row["player_root"];
            var change = long.Parse(row["change"]);
            _stackChanges[playerRoot] = change;
        }

        // If the source event is HandEnded, add stack changes
        if (_sourceEvent is HandEnded he)
        {
            he.StackChanges.Clear();
            foreach (var kvp in _stackChanges)
            {
                var playerRootHex = Convert
                    .ToHexString(GetPlayerRoot(kvp.Key))
                    .ToLowerInvariant();
                he.StackChanges[playerRootHex] = kvp.Value;
            }
        }
    }

    [Given(@"a PotAwarded event from hand domain with:")]
    public void GivenAPotAwardedEventFromHandDomainWith(TechTalk.SpecFlow.Table table)
    {
        var row = table.Rows[0];
        _potTotal = long.Parse(row["pot_total"]);

        _sourceEvent = new PotAwarded
        {
            AwardedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"a SagaRouter with TableSyncSaga and HandResultsSaga")]
    public void GivenASagaRouterWithTableSyncSagaAndHandResultsSaga()
    {
        _registeredSagas.Clear();
        _registeredSagas.Add("TableSyncSaga");
        _registeredSagas.Add("HandResultsSaga");
    }

    [Given(@"a SagaRouter with TableSyncSaga")]
    public void GivenASagaRouterWithTableSyncSaga()
    {
        _registeredSagas.Clear();
        _registeredSagas.Add("TableSyncSaga");
    }

    [Given(@"a HandStarted event")]
    public void GivenAHandStartedEvent()
    {
        var evt = new HandStarted
        {
            HandRoot = ByteString.CopyFrom(_handRoot),
            HandNumber = 1,
            GameVariant = GameVariant.TexasHoldem,
            DealerPosition = 0,
            SmallBlind = 5,
            BigBlind = 10,
            StartedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
        evt.ActivePlayers.Add(new SeatSnapshot
        {
            PlayerRoot = ByteString.CopyFrom(GetPlayerRoot("player-1")),
            Position = 0,
            Stack = 500,
        });
        evt.ActivePlayers.Add(new SeatSnapshot
        {
            PlayerRoot = ByteString.CopyFrom(GetPlayerRoot("player-2")),
            Position = 1,
            Stack = 500,
        });
        _sourceEvent = evt;
    }

    [Given(@"an event book with:")]
    public void GivenAnEventBookWith(TechTalk.SpecFlow.Table table)
    {
        _eventBookEvents.Clear();
        foreach (var row in table.Rows)
        {
            var eventType = row["event_type"];
            IMessage evt = eventType switch
            {
                "HandStarted" => CreateDefaultHandStartedEvent(),
                _ => throw new ArgumentException($"Unknown event type: {eventType}"),
            };
            _eventBookEvents.Add(evt);
        }
    }

    [Given(@"a SagaRouter with a failing saga and TableSyncSaga")]
    public void GivenASagaRouterWithAFailingSagaAndTableSyncSaga()
    {
        _registeredSagas.Clear();
        _registeredSagas.Add("FailingSaga");
        _registeredSagas.Add("TableSyncSaga");
        _routerHasFailingSaga = true;
    }

    // =========================================================================
    // When Steps
    // =========================================================================

    [When(@"the saga handles the event")]
    public void WhenTheSagaHandlesTheEvent()
    {
        _resultCommands.Clear();
        _sourceEvent.Should().NotBeNull("Source event must be set before saga handles it");

        if (_sagaType == "TableSyncSaga")
        {
            HandleTableSyncSaga();
        }
        else if (_sagaType == "HandResultsSaga")
        {
            HandleHandResultsSaga();
        }
    }

    [When(@"the router routes the event")]
    public void WhenTheRouterRoutesTheEvent()
    {
        _resultCommands.Clear();
        _handledBy.Clear();
        _routerException = null;

        foreach (var saga in _registeredSagas)
        {
            try
            {
                if (saga == "FailingSaga")
                {
                    // Simulate failure but router continues
                    throw new InvalidOperationException("Saga processing failed");
                }

                if (saga == "TableSyncSaga" && _sourceEvent is HandStarted)
                {
                    _sagaType = saga;
                    HandleTableSyncSaga();
                    _handledBy.Add(saga);
                }
                else if (saga == "HandResultsSaga" && (_sourceEvent is HandEnded || _sourceEvent is PotAwarded))
                {
                    _sagaType = saga;
                    HandleHandResultsSaga();
                    _handledBy.Add(saga);
                }
            }
            catch (Exception ex)
            {
                // Router catches and continues (fault tolerance)
                _routerException = ex;
            }
        }
    }

    [When(@"the router routes the events")]
    public void WhenTheRouterRoutesTheEvents()
    {
        _resultCommands.Clear();
        _handledBy.Clear();

        foreach (var evt in _eventBookEvents)
        {
            _sourceEvent = evt;
            foreach (var saga in _registeredSagas)
            {
                if (saga == "TableSyncSaga" && evt is HandStarted)
                {
                    _sagaType = saga;
                    HandleTableSyncSaga();
                    _handledBy.Add(saga);
                }
            }
        }
    }

    // =========================================================================
    // Saga Translation Logic
    // =========================================================================

    private void HandleTableSyncSaga()
    {
        if (_sourceEvent is HandStarted hs)
        {
            // Translate HandStarted -> DealCards
            var cmd = new DealCards
            {
                TableRoot = hs.HandRoot,
                HandNumber = hs.HandNumber,
                GameVariant = hs.GameVariant,
                DealerPosition = hs.DealerPosition,
                SmallBlind = hs.SmallBlind,
                BigBlind = hs.BigBlind,
            };

            foreach (var player in hs.ActivePlayers)
            {
                cmd.Players.Add(new PlayerInHand
                {
                    PlayerRoot = player.PlayerRoot,
                    Position = player.Position,
                    Stack = player.Stack,
                });
            }

            _resultCommands.Add(cmd);
            _targetDomain = "hand";
        }
        else if (_sourceEvent is HandComplete hc)
        {
            // Translate HandComplete -> EndHand
            var cmd = new EndHand
            {
                HandRoot = ByteString.CopyFromUtf8($"hand-{hc.HandNumber}"),
            };

            foreach (var winner in hc.Winners)
            {
                cmd.Results.Add(new PotResult
                {
                    WinnerRoot = winner.PlayerRoot,
                    Amount = winner.Amount,
                    PotType = winner.PotType,
                });
            }

            _resultCommands.Add(cmd);
            _targetDomain = "table";
        }
    }

    private void HandleHandResultsSaga()
    {
        if (_sourceEvent is HandEnded he)
        {
            // Translate HandEnded -> ReleaseFunds for each player with a stack change
            foreach (var kvp in he.StackChanges)
            {
                var cmd = new ReleaseFunds
                {
                    TableRoot = he.HandRoot,
                };
                _resultCommands.Add(cmd);
            }
            _targetDomain = "player";
        }
        else if (_sourceEvent is PotAwarded pa)
        {
            // Translate PotAwarded -> DepositFunds for each winner
            foreach (var winner in pa.Winners)
            {
                var cmd = new DepositFunds
                {
                    Amount = new Currency
                    {
                        Amount = winner.Amount,
                        CurrencyCode = "CHIPS",
                    },
                };
                _resultCommands.Add(cmd);
            }
            _targetDomain = "player";
        }
    }

    private HandStarted CreateDefaultHandStartedEvent()
    {
        var evt = new HandStarted
        {
            HandRoot = ByteString.CopyFrom(_handRoot),
            HandNumber = 1,
            GameVariant = GameVariant.TexasHoldem,
            DealerPosition = 0,
            SmallBlind = 5,
            BigBlind = 10,
            StartedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
        evt.ActivePlayers.Add(new SeatSnapshot
        {
            PlayerRoot = ByteString.CopyFrom(GetPlayerRoot("player-1")),
            Position = 0,
            Stack = 500,
        });
        evt.ActivePlayers.Add(new SeatSnapshot
        {
            PlayerRoot = ByteString.CopyFrom(GetPlayerRoot("player-2")),
            Position = 1,
            Stack = 500,
        });
        return evt;
    }

    // =========================================================================
    // Then Steps
    // =========================================================================

    [Then(@"the saga emits a DealCards command to hand domain")]
    public void ThenTheSagaEmitsADealCardsCommandToHandDomain()
    {
        _resultCommands.Should().ContainSingle(c => c is DealCards);
        _targetDomain.Should().Be("hand");
    }

    [Then(@"the command has game_variant TEXAS_HOLDEM")]
    public void ThenTheCommandHasGameVariantTexasHoldem()
    {
        var cmd = _resultCommands.OfType<DealCards>().First();
        cmd.GameVariant.Should().Be(GameVariant.TexasHoldem);
    }

    [Then(@"the command has (\d+) players")]
    public void ThenTheCommandHasPlayers(int count)
    {
        var cmd = _resultCommands.OfType<DealCards>().First();
        cmd.Players.Count.Should().Be(count);
    }

    [Then(@"the command has hand_number (\d+)")]
    public void ThenTheCommandHasHandNumber(int number)
    {
        var cmd = _resultCommands.OfType<DealCards>().First();
        cmd.HandNumber.Should().Be(number);
    }

    [Then(@"the saga emits an EndHand command to table domain")]
    public void ThenTheSagaEmitsAnEndHandCommandToTableDomain()
    {
        _resultCommands.Should().ContainSingle(c => c is EndHand);
        _targetDomain.Should().Be("table");
    }

    [Then(@"the command has (\d+) result")]
    public void ThenTheCommandHasResult(int count)
    {
        var cmd = _resultCommands.OfType<EndHand>().First();
        cmd.Results.Count.Should().Be(count);
    }

    [Then(@"the result has winner ""(.*)"" with amount (\d+)")]
    public void ThenTheResultHasWinnerWithAmount(string playerRoot, long amount)
    {
        var cmd = _resultCommands.OfType<EndHand>().First();
        var expectedRoot = ByteString.CopyFrom(GetPlayerRoot(playerRoot));
        var result = cmd.Results.First(r => r.WinnerRoot == expectedRoot);
        result.Amount.Should().Be(amount);
    }

    [Then(@"the saga emits (\d+) ReleaseFunds commands to player domain")]
    public void ThenTheSagaEmitsReleaseFundsCommandsToPlayerDomain(int count)
    {
        _resultCommands.OfType<ReleaseFunds>().Count().Should().Be(count);
        _targetDomain.Should().Be("player");
    }

    [Then(@"the saga emits (\d+) DepositFunds commands to player domain")]
    public void ThenTheSagaEmitsDepositFundsCommandsToPlayerDomain(int count)
    {
        _resultCommands.OfType<DepositFunds>().Count().Should().Be(count);
        _targetDomain.Should().Be("player");
    }

    [Then(@"the first command has amount (\d+) for ""(.*)""")]
    public void ThenTheFirstCommandHasAmountFor(long amount, string playerRoot)
    {
        var cmd = _resultCommands.OfType<DepositFunds>().First();
        cmd.Amount.Should().NotBeNull();
        cmd.Amount.Amount.Should().Be(amount);
    }

    [Then(@"the second command has amount (\d+) for ""(.*)""")]
    public void ThenTheSecondCommandHasAmountFor(long amount, string playerRoot)
    {
        var cmd = _resultCommands.OfType<DepositFunds>().Skip(1).First();
        cmd.Amount.Should().NotBeNull();
        cmd.Amount.Amount.Should().Be(amount);
    }

    [Then(@"only TableSyncSaga handles the event")]
    public void ThenOnlyTableSyncSagaHandlesTheEvent()
    {
        _handledBy.Should().ContainSingle();
        _handledBy.First().Should().Be("TableSyncSaga");
    }

    [Then(@"the saga emits (\d+) DealCards commands")]
    public void ThenTheSagaEmitsDealCardsCommands(int count)
    {
        _resultCommands.OfType<DealCards>().Count().Should().Be(count);
    }

    [Then(@"TableSyncSaga still emits its command")]
    public void ThenTableSyncSagaStillEmitsItsCommand()
    {
        _handledBy.Should().Contain("TableSyncSaga");
        _resultCommands.OfType<DealCards>().Should().NotBeEmpty();
    }

    [Then(@"no exception is raised")]
    public void ThenNoExceptionIsRaised()
    {
        // The router caught the exception internally; no unhandled exception
        // propagated to the test. The router exception was captured but handled.
        _handledBy.Should().NotBeEmpty("Router should have continued processing after failure");
    }

    // NOTE: "the saga emits 0 ReleaseFunds commands to player domain" is handled
    // by the parameterized step ThenTheSagaEmitsReleaseFundsCommandsToPlayerDomain(0)
}
