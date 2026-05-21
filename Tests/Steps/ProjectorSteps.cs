using Angzarr.Examples;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using TechTalk.SpecFlow;

namespace Tests.Steps;

[Binding]
public class ProjectorSteps
{
    private readonly ScenarioContext _context;

    // Projector state
    private readonly Dictionary<string, string> _playerNames = new();
    private readonly List<string> _outputLines = new();
    private IMessage? _currentEvent;
    private bool _showTimestamps;
    private DateTime? _eventTimestamp;

    public ProjectorSteps(ScenarioContext context)
    {
        _context = context;
    }

    // =========================================================================
    // Card Formatting Helpers
    // =========================================================================

    private static string FormatRank(Rank rank) => rank switch
    {
        Rank.Ace => "A",
        Rank.King => "K",
        Rank.Queen => "Q",
        Rank.Jack => "J",
        Rank.Ten => "T",
        _ => ((int)rank).ToString(),
    };

    private static string FormatSuit(Suit suit) => suit switch
    {
        Suit.Clubs => "c",
        Suit.Diamonds => "d",
        Suit.Hearts => "h",
        Suit.Spades => "s",
        _ => "?",
    };

    private static string FormatCard(Card card) => $"{FormatRank(card.Rank)}{FormatSuit(card.Suit)}";

    private static string FormatCards(IEnumerable<Card> cards) =>
        string.Join(" ", cards.Select(FormatCard));

    private static string FormatMoney(long amount)
    {
        return $"${amount:N0}";
    }

    private static Card ParseCardNotation(string notation)
    {
        if (notation.Length < 2) throw new ArgumentException($"Invalid card notation: {notation}");

        var rank = notation[0] switch
        {
            'A' => Rank.Ace,
            'K' => Rank.King,
            'Q' => Rank.Queen,
            'J' => Rank.Jack,
            'T' => Rank.Ten,
            '9' => Rank.Nine,
            '8' => Rank.Eight,
            '7' => Rank.Seven,
            '6' => Rank.Six,
            '5' => Rank.Five,
            '4' => Rank.Four,
            '3' => Rank.Three,
            '2' => Rank.Two,
            _ => (Rank)0,
        };
        var suit = notation[1] switch
        {
            'c' => Suit.Clubs,
            'd' => Suit.Diamonds,
            'h' => Suit.Hearts,
            's' => Suit.Spades,
            _ => (Suit)0,
        };
        return new Card { Rank = rank, Suit = suit };
    }

    private string ResolveName(ByteString playerRoot)
    {
        var rootStr = playerRoot.ToStringUtf8();
        if (_playerNames.TryGetValue(rootStr, out var name))
            return name;
        // Fallback: truncated player ID
        if (rootStr.StartsWith("player-"))
            return $"Player_{rootStr.Substring("player-".Length)}";
        return $"Player_{rootStr}";
    }

    private string ResolveName(string playerRoot)
    {
        if (_playerNames.TryGetValue(playerRoot, out var name))
            return name;
        if (playerRoot.StartsWith("player-"))
            return $"Player_{playerRoot.Substring("player-".Length)}";
        return $"Player_{playerRoot}";
    }

    private void AddOutput(string line)
    {
        if (_showTimestamps && _eventTimestamp.HasValue)
        {
            var ts = _eventTimestamp.Value;
            _outputLines.Add($"[{ts:HH:mm:ss}] {line}");
        }
        else
        {
            _outputLines.Add(line);
        }
    }

    private string AllOutput => string.Join("\n", _outputLines);

    // =========================================================================
    // Projector Rendering Logic
    // =========================================================================

    private void RenderEvent(IMessage evt)
    {
        switch (evt)
        {
            case PlayerRegistered pr:
                _playerNames[pr.Email ?? ""] = pr.DisplayName;
                AddOutput($"{pr.DisplayName} registered");
                break;

            case FundsDeposited fd:
                AddOutput($"Deposited {FormatMoney(fd.Amount.Amount)} - balance: {FormatMoney(fd.NewBalance.Amount)}");
                break;

            case FundsWithdrawn fw:
                AddOutput($"Withdrew {FormatMoney(fw.Amount.Amount)} - balance: {FormatMoney(fw.NewBalance.Amount)}");
                break;

            case FundsReserved fr:
                AddOutput($"Reserved {FormatMoney(fr.Amount.Amount)}");
                break;

            case TableCreated tc:
                var variantName = tc.GameVariant switch
                {
                    GameVariant.TexasHoldem => "TEXAS_HOLDEM",
                    GameVariant.Omaha => "OMAHA",
                    GameVariant.FiveCardDraw => "FIVE_CARD_DRAW",
                    GameVariant.SevenCardStud => "SEVEN_CARD_STUD",
                    _ => tc.GameVariant.ToString(),
                };
                AddOutput($"Table created: {tc.TableName} [{variantName}] {FormatMoney(tc.SmallBlind)}/{FormatMoney(tc.BigBlind)} buy-in {FormatMoney(tc.MinBuyIn)} - {FormatMoney(tc.MaxBuyIn)}");
                break;

            case PlayerJoined pj:
                var joinName = ResolveName(pj.PlayerRoot);
                AddOutput($"{joinName} joined at seat {pj.SeatPosition} with {FormatMoney(pj.BuyInAmount)}");
                break;

            case PlayerLeft pl:
                var leftName = ResolveName(pl.PlayerRoot);
                AddOutput($"{leftName} left the table - cashed out {FormatMoney(pl.ChipsCashedOut)}");
                break;

            case HandStarted hs:
                var playerList = string.Join(", ", hs.ActivePlayers.Select(p => ResolveName(p.PlayerRoot)));
                AddOutput($"=== HAND #{hs.HandNumber} === Dealer: Seat {hs.DealerPosition}");
                AddOutput($"Players: {playerList}");
                break;

            case HandEnded he:
                foreach (var result in he.Results)
                {
                    var winnerName = ResolveName(result.WinnerRoot);
                    AddOutput($"{winnerName} wins {FormatMoney(result.Amount)}");
                }
                break;

            case CardsDealt cd:
                foreach (var playerCards in cd.PlayerCards)
                {
                    var name = ResolveName(playerCards.PlayerRoot);
                    var cards = FormatCards(playerCards.Cards);
                    AddOutput($"{name}: [{cards}]");
                }
                break;

            case BlindPosted bp:
                var blindName = ResolveName(bp.PlayerRoot);
                AddOutput($"{blindName} posts {bp.BlindType.ToUpper()} {FormatMoney(bp.Amount)}");
                break;

            case ActionTaken at:
                var actName = ResolveName(at.PlayerRoot);
                switch (at.Action)
                {
                    case ActionType.Fold:
                        AddOutput($"{actName} folds");
                        break;
                    case ActionType.Check:
                        AddOutput($"{actName} checks");
                        break;
                    case ActionType.Call:
                        AddOutput($"{actName} calls {FormatMoney(at.Amount)} (pot: {FormatMoney(at.PotTotal)})");
                        break;
                    case ActionType.Raise:
                        AddOutput($"{actName} raises to {FormatMoney(at.Amount)} (pot: {FormatMoney(at.PotTotal)})");
                        break;
                    case ActionType.Bet:
                        AddOutput($"{actName} bets {FormatMoney(at.Amount)} (pot: {FormatMoney(at.PotTotal)})");
                        break;
                    case ActionType.AllIn:
                        AddOutput($"{actName} all-in {FormatMoney(at.Amount)} (pot: {FormatMoney(at.PotTotal)})");
                        break;
                }
                break;

            case CommunityCardsDealt ccd:
                var phaseName = ccd.Phase switch
                {
                    Angzarr.Examples.BettingPhase.Flop => "Flop",
                    Angzarr.Examples.BettingPhase.Turn => "Turn",
                    Angzarr.Examples.BettingPhase.River => "River",
                    _ => "Community",
                };
                var boardCards = FormatCards(ccd.Cards);
                AddOutput($"{phaseName}: [{boardCards}]");
                if (ccd.AllCommunityCards.Count > 0)
                {
                    AddOutput($"Board: [{FormatCards(ccd.AllCommunityCards)}]");
                }
                break;

            case ShowdownStarted:
                AddOutput("*** SHOWDOWN ***");
                break;

            case CardsRevealed cr:
                var revealName = ResolveName(cr.PlayerRoot);
                var revealCards = FormatCards(cr.Cards);
                var rankName = cr.Ranking?.RankType switch
                {
                    HandRankType.HighCard => "High Card",
                    HandRankType.Pair => "Pair",
                    HandRankType.TwoPair => "Two Pair",
                    HandRankType.ThreeOfAKind => "Three of a Kind",
                    HandRankType.Straight => "Straight",
                    HandRankType.Flush => "Flush",
                    HandRankType.FullHouse => "Full House",
                    HandRankType.FourOfAKind => "Four of a Kind",
                    HandRankType.StraightFlush => "Straight Flush",
                    HandRankType.RoyalFlush => "Royal Flush",
                    _ => "Unknown",
                };
                AddOutput($"{revealName} shows [{revealCards}] - {rankName}");
                break;

            case CardsMucked cm:
                var muckName = ResolveName(cm.PlayerRoot);
                AddOutput($"{muckName} mucks");
                break;

            case PotAwarded pa:
                foreach (var winner in pa.Winners)
                {
                    var paName = ResolveName(winner.PlayerRoot);
                    AddOutput($"{paName} wins {FormatMoney(winner.Amount)}");
                }
                break;

            case HandComplete hc:
                AddOutput("Final stacks:");
                foreach (var stack in hc.FinalStacks)
                {
                    var stackName = ResolveName(stack.PlayerRoot);
                    var suffix = stack.HasFolded ? " (folded)" : "";
                    AddOutput($"  {stackName}: {FormatMoney(stack.Stack)}{suffix}");
                }
                break;

            case PlayerTimedOut pto:
                var toName = ResolveName(pto.PlayerRoot);
                var defaultAction = pto.DefaultAction switch
                {
                    ActionType.Fold => "auto folds",
                    ActionType.Check => "auto checks",
                    _ => $"auto {pto.DefaultAction}",
                };
                AddOutput($"{toName} timed out - {defaultAction}");
                break;

            default:
                // Unknown event type
                var typeUrl = "unknown";
                AddOutput($"[Unknown event type: {typeUrl}]");
                break;
        }
    }

    // =========================================================================
    // Given Steps
    // =========================================================================

    [Given(@"an OutputProjector")]
    public void GivenAnOutputProjector()
    {
        _outputLines.Clear();
        _playerNames.Clear();
        _showTimestamps = false;
        _currentEvent = null;
    }

    [Given(@"an OutputProjector with player name ""(.*)""")]
    public void GivenAnOutputProjectorWithPlayerName(string name)
    {
        GivenAnOutputProjector();
        var playerRoot = $"player-{name.ToLower()}";
        _playerNames[playerRoot] = name;
    }

    [Given(@"an OutputProjector with player names ""(.*)"" and ""(.*)""")]
    public void GivenAnOutputProjectorWithPlayerNames(string name1, string name2)
    {
        GivenAnOutputProjector();
        _playerNames[$"player-{name1.ToLower()}"] = name1;
        _playerNames[$"player-{name2.ToLower()}"] = name2;
    }

    [Given(@"an OutputProjector with show_timestamps enabled")]
    public void GivenAnOutputProjectorWithShowTimestampsEnabled()
    {
        GivenAnOutputProjector();
        _showTimestamps = true;
    }

    [Given(@"an OutputProjector with show_timestamps disabled")]
    public void GivenAnOutputProjectorWithShowTimestampsDisabled()
    {
        GivenAnOutputProjector();
        _showTimestamps = false;
    }

    [Given(@"a PlayerRegistered event with display_name ""(.*)""")]
    public void GivenAPlayerRegisteredEventWithDisplayName(string displayName)
    {
        _currentEvent = new PlayerRegistered
        {
            DisplayName = displayName,
            Email = $"{displayName.ToLower()}@test.com",
            RegisteredAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"a FundsDeposited event with amount (\d+) and new_balance (\d+)")]
    public void GivenAFundsDepositedEventWithAmountAndNewBalance(long amount, long balance)
    {
        _currentEvent = new FundsDeposited
        {
            Amount = new Currency { Amount = amount, CurrencyCode = "CHIPS" },
            NewBalance = new Currency { Amount = balance, CurrencyCode = "CHIPS" },
            DepositedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"a FundsWithdrawn event with amount (\d+) and new_balance (\d+)")]
    public void GivenAFundsWithdrawnEventWithAmountAndNewBalance(long amount, long balance)
    {
        _currentEvent = new FundsWithdrawn
        {
            Amount = new Currency { Amount = amount, CurrencyCode = "CHIPS" },
            NewBalance = new Currency { Amount = balance, CurrencyCode = "CHIPS" },
            WithdrawnAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"a FundsReserved event with amount (\d+)")]
    public void GivenAFundsReservedEventWithAmount(long amount)
    {
        _currentEvent = new FundsReserved
        {
            Amount = new Currency { Amount = amount, CurrencyCode = "CHIPS" },
            ReservedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"a TableCreated event with:")]
    public void GivenATableCreatedEventWith(TechTalk.SpecFlow.Table table)
    {
        var row = table.Rows[0];
        var variant = row["game_variant"] switch
        {
            "TEXAS_HOLDEM" => GameVariant.TexasHoldem,
            "OMAHA" => GameVariant.Omaha,
            "FIVE_CARD_DRAW" => GameVariant.FiveCardDraw,
            _ => GameVariant.Unspecified,
        };

        _currentEvent = new TableCreated
        {
            TableName = row["table_name"],
            GameVariant = variant,
            SmallBlind = long.Parse(row["small_blind"]),
            BigBlind = long.Parse(row["big_blind"]),
            MinBuyIn = long.Parse(row["min_buy_in"]),
            MaxBuyIn = long.Parse(row["max_buy_in"]),
            MaxPlayers = 9,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"a PlayerJoined event at seat (\d+) with buy_in (\d+)")]
    public void GivenAPlayerJoinedEventAtSeatWithBuyIn(int seat, long buyIn)
    {
        // Find a player root from names if available
        var playerRoot = _playerNames.Keys.FirstOrDefault() ?? "player-unknown";
        _currentEvent = new PlayerJoined
        {
            PlayerRoot = ByteString.CopyFromUtf8(playerRoot),
            SeatPosition = seat,
            BuyInAmount = buyIn,
            Stack = buyIn,
            JoinedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"a PlayerLeft event with chips_cashed_out (\d+)")]
    public void GivenAPlayerLeftEventWithChipsCashedOut(long chipsCashedOut)
    {
        var playerRoot = _playerNames.Keys.FirstOrDefault() ?? "player-unknown";
        _currentEvent = new PlayerLeft
        {
            PlayerRoot = ByteString.CopyFromUtf8(playerRoot),
            ChipsCashedOut = chipsCashedOut,
            LeftAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    // NOTE: "a HandStarted event with:" step is defined in ProcessManagerSteps
    // and handles both PM and projector scenarios by checking column presence.
    // The projector scenario stores the event in ScenarioContext for retrieval here.

    [Given(@"active players ""(.*)"", ""(.*)"", ""(.*)"" at seats (\d+), (\d+), (\d+)")]
    public void GivenActivePlayersAtSeats(string p1, string p2, string p3, int s1, int s2, int s3)
    {
        // The shared "a HandStarted event with:" step in ProcessManagerSteps stores
        // the proto event under "projectorCurrentEvent" in ScenarioContext, but the
        // local _currentEvent slot stays null. Pull it forward so active players can
        // be attached to the same proto the projector will render.
        if (_currentEvent == null
            && _context.TryGetValue("projectorCurrentEvent", out IMessage? sharedEvt)
            && sharedEvt != null)
        {
            _currentEvent = sharedEvt;
        }

        var players = new[] { (p1, s1), (p2, s2), (p3, s3) };
        if (_currentEvent is HandStarted hs)
        {
            foreach (var (name, seat) in players)
            {
                var root = $"player-{name.ToLower()}";
                _playerNames[root] = name;
                hs.ActivePlayers.Add(new SeatSnapshot
                {
                    PlayerRoot = ByteString.CopyFromUtf8(root),
                    Position = seat,
                    Stack = 500,
                });
            }
        }
    }

    [Given(@"a HandEnded event with winner ""(.*)"" amount (\d+)")]
    public void GivenAHandEndedEventWithWinnerAmount(string winner, long amount)
    {
        var winnerRoot = $"player-{winner.ToLower()}";
        _currentEvent = new HandEnded
        {
            HandRoot = ByteString.CopyFromUtf8("hand-1"),
            EndedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
        ((HandEnded)_currentEvent).Results.Add(new PotResult
        {
            WinnerRoot = ByteString.CopyFromUtf8(winnerRoot),
            Amount = amount,
            PotType = "main",
        });
    }

    [Given(@"a CardsDealt event with player ""(.*)"" holding (.+)")]
    public void GivenACardsDealtEventWithPlayerHolding(string playerName, string cardsStr)
    {
        var playerRoot = $"player-{playerName.ToLower()}";
        var cards = cardsStr.Split(' ').Select(ParseCardNotation).ToList();

        var holeCards = new PlayerHoleCards
        {
            PlayerRoot = ByteString.CopyFromUtf8(playerRoot),
        };
        holeCards.Cards.AddRange(cards);

        var evt = new CardsDealt
        {
            HandNumber = 1,
            GameVariant = GameVariant.TexasHoldem,
            DealtAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
        evt.PlayerCards.Add(holeCards);
        _currentEvent = evt;
    }

    [Given(@"a BlindPosted event for ""(.*)"" type ""(.*)"" amount (\d+)")]
    public void GivenABlindPostedEventForTypeAmount(string playerName, string blindType, long amount)
    {
        var playerRoot = $"player-{playerName.ToLower()}";
        _currentEvent = new BlindPosted
        {
            PlayerRoot = ByteString.CopyFromUtf8(playerRoot),
            BlindType = blindType,
            Amount = amount,
            PostedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    // Action-only form (FOLD/CHECK). The character class restricts the action
    // capture to uppercase letters/underscores so amount/pot_total scenarios are
    // routed to the 4-parameter overload instead of matching here ambiguously.
    [Given(@"an ActionTaken event for ""(.*)"" action ([A-Z_]+)$")]
    public void GivenAnActionTakenEventForAction(string playerName, string action)
    {
        var playerRoot = $"player-{playerName.ToLower()}";
        var actionType = action switch
        {
            "FOLD" => ActionType.Fold,
            "CHECK" => ActionType.Check,
            "CALL" => ActionType.Call,
            "RAISE" => ActionType.Raise,
            "BET" => ActionType.Bet,
            "ALL_IN" => ActionType.AllIn,
            _ => ActionType.ActionUnspecified,
        };

        _currentEvent = new ActionTaken
        {
            PlayerRoot = ByteString.CopyFromUtf8(playerRoot),
            Action = actionType,
            Amount = 0,
            PotTotal = 0,
            ActionAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"an ActionTaken event for ""(.*)"" action (.+) amount (\d+) pot_total (\d+)")]
    public void GivenAnActionTakenEventForActionAmountPotTotal(string playerName, string action, long amount, long potTotal)
    {
        var playerRoot = $"player-{playerName.ToLower()}";
        var actionType = action switch
        {
            "FOLD" => ActionType.Fold,
            "CHECK" => ActionType.Check,
            "CALL" => ActionType.Call,
            "RAISE" => ActionType.Raise,
            "BET" => ActionType.Bet,
            "ALL_IN" => ActionType.AllIn,
            _ => ActionType.ActionUnspecified,
        };

        _currentEvent = new ActionTaken
        {
            PlayerRoot = ByteString.CopyFromUtf8(playerRoot),
            Action = actionType,
            Amount = amount,
            PotTotal = potTotal,
            ActionAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"a CommunityCardsDealt event for FLOP with cards (.+)")]
    public void GivenACommunityCardsDealtEventForFlopWithCards(string cardsStr)
    {
        var cards = cardsStr.Split(' ').Select(ParseCardNotation).ToList();
        var evt = new CommunityCardsDealt
        {
            Phase = Angzarr.Examples.BettingPhase.Flop,
            DealtAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
        evt.Cards.AddRange(cards);
        evt.AllCommunityCards.AddRange(cards);
        _currentEvent = evt;
    }

    [Given(@"a CommunityCardsDealt event for TURN with card (.+)")]
    public void GivenACommunityCardsDealtEventForTurnWithCard(string cardStr)
    {
        var card = ParseCardNotation(cardStr);
        var evt = new CommunityCardsDealt
        {
            Phase = Angzarr.Examples.BettingPhase.Turn,
            DealtAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
        evt.Cards.Add(card);
        // Board so far would include flop + turn but for this test we just add the turn card
        evt.AllCommunityCards.Add(card);
        _currentEvent = evt;
    }

    [Given(@"a ShowdownStarted event")]
    public void GivenAShowdownStartedEvent()
    {
        _currentEvent = new ShowdownStarted
        {
            StartedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"a CardsRevealed event for ""(.*)"" with cards (.+) and ranking (.+)")]
    public void GivenACardsRevealedEventForWithCardsAndRanking(string playerName, string cardsStr, string ranking)
    {
        var playerRoot = $"player-{playerName.ToLower()}";
        var cards = cardsStr.Split(' ').Select(ParseCardNotation).ToList();
        var rankType = ranking switch
        {
            "PAIR" => HandRankType.Pair,
            "TWO_PAIR" => HandRankType.TwoPair,
            "THREE_OF_A_KIND" => HandRankType.ThreeOfAKind,
            "STRAIGHT" => HandRankType.Straight,
            "FLUSH" => HandRankType.Flush,
            "FULL_HOUSE" => HandRankType.FullHouse,
            "FOUR_OF_A_KIND" => HandRankType.FourOfAKind,
            "STRAIGHT_FLUSH" => HandRankType.StraightFlush,
            "ROYAL_FLUSH" => HandRankType.RoyalFlush,
            "HIGH_CARD" => HandRankType.HighCard,
            _ => (HandRankType)0,
        };

        var evt = new CardsRevealed
        {
            PlayerRoot = ByteString.CopyFromUtf8(playerRoot),
            Ranking = new HandRanking { RankType = rankType },
            RevealedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
        evt.Cards.AddRange(cards);
        _currentEvent = evt;
    }

    [Given(@"a CardsMucked event for ""(.*)""")]
    public void GivenACardsMuckedEventFor(string playerName)
    {
        var playerRoot = $"player-{playerName.ToLower()}";
        _currentEvent = new CardsMucked
        {
            PlayerRoot = ByteString.CopyFromUtf8(playerRoot),
            MuckedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"a PotAwarded event with winner ""(.*)"" amount (\d+)")]
    public void GivenAPotAwardedEventWithWinnerAmount(string winner, long amount)
    {
        var winnerRoot = $"player-{winner.ToLower()}";
        var evt = new PotAwarded
        {
            AwardedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
        evt.Winners.Add(new PotWinner
        {
            PlayerRoot = ByteString.CopyFromUtf8(winnerRoot),
            Amount = amount,
            PotType = "main",
        });
        _currentEvent = evt;
    }

    [Given(@"a HandComplete event with final stacks:")]
    public void GivenAHandCompleteEventWithFinalStacks(TechTalk.SpecFlow.Table table)
    {
        var evt = new HandComplete
        {
            TableRoot = ByteString.CopyFromUtf8("table-1"),
            HandNumber = 1,
            CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };

        foreach (var row in table.Rows)
        {
            var playerName = row["player"];
            var stack = long.Parse(row["stack"]);
            var hasFolded = bool.Parse(row["has_folded"]);
            var playerRoot = $"player-{playerName.ToLower()}";

            evt.FinalStacks.Add(new PlayerStackSnapshot
            {
                PlayerRoot = ByteString.CopyFromUtf8(playerRoot),
                Stack = stack,
                HasFolded = hasFolded,
            });
        }

        _currentEvent = evt;
    }

    [Given(@"a PlayerTimedOut event for ""(.*)"" with default_action (.+)")]
    public void GivenAPlayerTimedOutEventForWithDefaultAction(string playerName, string action)
    {
        var playerRoot = $"player-{playerName.ToLower()}";
        var actionType = action switch
        {
            "FOLD" => ActionType.Fold,
            "CHECK" => ActionType.Check,
            _ => ActionType.ActionUnspecified,
        };

        _currentEvent = new PlayerTimedOut
        {
            PlayerRoot = ByteString.CopyFromUtf8(playerRoot),
            DefaultAction = actionType,
            TimedOutAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"player ""(.*)"" is registered as ""(.*)""")]
    public void GivenPlayerIsRegisteredAs(string playerRoot, string displayName)
    {
        _playerNames[playerRoot] = displayName;
    }

    [Given(@"an event with created_at (\d+:\d+:\d+)")]
    public void GivenAnEventWithCreatedAt(string time)
    {
        var parts = time.Split(':');
        var hour = int.Parse(parts[0]);
        var minute = int.Parse(parts[1]);
        var second = int.Parse(parts[2]);
        _eventTimestamp = new DateTime(2024, 1, 1, hour, minute, second, DateTimeKind.Utc);

        // Create a simple event to render
        _currentEvent = new PlayerRegistered
        {
            DisplayName = "TestPlayer",
            RegisteredAt = Timestamp.FromDateTime(_eventTimestamp.Value),
        };
    }

    [Given(@"an event with created_at")]
    public void GivenAnEventWithCreatedAt()
    {
        _eventTimestamp = DateTime.UtcNow;
        _currentEvent = new PlayerRegistered
        {
            DisplayName = "TestPlayer",
            RegisteredAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Given(@"an event book with PlayerJoined and BlindPosted events")]
    public void GivenAnEventBookWithPlayerJoinedAndBlindPostedEvents()
    {
        var playerRoot = "player-test";
        _playerNames[playerRoot] = "TestPlayer";

        // Store multiple events for batch processing
        var events = new List<IMessage>
        {
            new PlayerJoined
            {
                PlayerRoot = ByteString.CopyFromUtf8(playerRoot),
                SeatPosition = 1,
                BuyInAmount = 500,
                Stack = 500,
                JoinedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            },
            new BlindPosted
            {
                PlayerRoot = ByteString.CopyFromUtf8(playerRoot),
                BlindType = "small",
                Amount = 5,
                PostedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            },
        };
        _context["eventBookEvents"] = events;
    }

    [Given(@"an event with unknown type_url ""(.*)""")]
    public void GivenAnEventWithUnknownTypeUrl(string typeUrl)
    {
        // Store the type_url for the unknown event rendering
        _context["unknownTypeUrl"] = typeUrl;
    }

    // =========================================================================
    // When Steps
    // =========================================================================

    [When(@"the projector handles the event")]
    public void WhenTheProjectorHandlesTheEvent()
    {
        _outputLines.Clear();

        // Check for event stored via shared step (e.g., "a HandStarted event with:")
        if (_currentEvent == null
            && _context.TryGetValue("projectorCurrentEvent", out IMessage? sharedEvt)
            && sharedEvt != null)
        {
            _currentEvent = sharedEvt;
        }

        // Check for unknown event type
        if (_currentEvent == null
            && _context.TryGetValue("unknownTypeUrl", out string? typeUrl)
            && typeUrl != null)
        {
            AddOutput($"[Unknown event type: {typeUrl}]");
            return;
        }

        _currentEvent.Should().NotBeNull("An event must be set before the projector handles it");
        RenderEvent(_currentEvent!);
    }

    [When(@"formatting cards:")]
    public void WhenFormattingCards(TechTalk.SpecFlow.Table table)
    {
        _outputLines.Clear();
        var cards = new List<Card>();
        foreach (var row in table.Rows)
        {
            var suit = row["suit"] switch
            {
                "CLUBS" => Suit.Clubs,
                "DIAMONDS" => Suit.Diamonds,
                "HEARTS" => Suit.Hearts,
                "SPADES" => Suit.Spades,
                _ => (Suit)0,
            };
            var rank = (Rank)int.Parse(row["rank"]);
            cards.Add(new Card { Suit = suit, Rank = rank });
        }
        AddOutput(FormatCards(cards));
    }

    [When(@"formatting cards with rank 2 through 14")]
    public void WhenFormattingCardsWithRank2Through14()
    {
        _outputLines.Clear();
        for (int r = 2; r <= 14; r++)
        {
            var card = new Card { Suit = Suit.Spades, Rank = (Rank)r };
            AddOutput($"{r}={FormatCard(card)}");
        }
    }

    [When(@"an event references ""(.*)""")]
    public void WhenAnEventReferences(string playerRoot)
    {
        _outputLines.Clear();
        var name = ResolveName(playerRoot);
        AddOutput(name);
    }

    [When(@"an event references unknown ""(.*)""")]
    public void WhenAnEventReferencesUnknown(string playerRoot)
    {
        _outputLines.Clear();
        var name = ResolveName(playerRoot);
        AddOutput(name);
    }

    [When(@"the projector handles the event book")]
    public void WhenTheProjectorHandlesTheEventBook()
    {
        _outputLines.Clear();
        if (_context.TryGetValue("eventBookEvents", out List<IMessage>? events) && events != null)
        {
            foreach (var evt in events)
            {
                RenderEvent(evt);
            }
        }
    }

    // =========================================================================
    // Then Steps
    // =========================================================================

    [Then(@"the output contains ""(.*)""")]
    public void ThenTheOutputContains(string expected)
    {
        AllOutput.Should().Contain(expected,
            $"Expected output to contain \"{expected}\" but got:\n{AllOutput}");
    }

    [Then(@"the output starts with ""\[(.+)\]""")]
    public void ThenTheOutputStartsWith(string prefix)
    {
        _outputLines.Should().NotBeEmpty();
        _outputLines[0].Should().StartWith($"[{prefix}]");
    }

    [Then(@"the output does not start with ""\[(.+)""")]
    public void ThenTheOutputDoesNotStartWith(string prefix)
    {
        _outputLines.Should().NotBeEmpty();
        _outputLines[0].Should().NotStartWith($"[{prefix}");
    }

    [Then(@"the output uses ""(.*)"" prefix")]
    public void ThenTheOutputUsesPrefix(string expected)
    {
        AllOutput.Should().Contain(expected,
            $"Expected output to contain prefix \"{expected}\" but got:\n{AllOutput}");
    }

    [Then(@"the output uses ""(.*)""")]
    public void ThenTheOutputUses(string expected)
    {
        AllOutput.Should().Contain(expected);
    }

    [Then(@"ranks 2-9 display as digits")]
    public void ThenRanks2Through9DisplayAsDigits()
    {
        for (int r = 2; r <= 9; r++)
        {
            AllOutput.Should().Contain($"{r}={r}s",
                $"Rank {r} should display as digit {r}");
        }
    }

    [Then(@"rank (\d+) displays as ""(.*)""")]
    public void ThenRankDisplaysAs(int rank, string display)
    {
        AllOutput.Should().Contain($"{rank}={display}s",
            $"Rank {rank} should display as {display}");
    }

    [Then(@"both events are rendered in order")]
    public void ThenBothEventsAreRenderedInOrder()
    {
        _outputLines.Count.Should().BeGreaterThanOrEqualTo(2,
            "Both events should produce output");

        // First event should be PlayerJoined, second BlindPosted
        var joinIndex = _outputLines.FindIndex(l => l.Contains("joined"));
        var blindIndex = _outputLines.FindIndex(l => l.Contains("posts"));
        joinIndex.Should().BeGreaterThanOrEqualTo(0, "PlayerJoined output expected");
        blindIndex.Should().BeGreaterThanOrEqualTo(0, "BlindPosted output expected");
        joinIndex.Should().BeLessThan(blindIndex, "Events should be rendered in order");
    }
}
