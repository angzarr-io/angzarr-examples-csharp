using Angzarr.Client;
using Angzarr.Examples;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using TechTalk.SpecFlow;
using Tournament.Agg.Handlers;
using TournamentState = Tournament.Agg.TournamentState;

namespace Tests.Steps;

[Binding]
public class TournamentSteps
{
    private readonly ScenarioContext _context;

    public TournamentSteps(ScenarioContext context)
    {
        _context = context;
    }

    private TournamentState State
    {
        get
        {
            if (!_context.TryGetValue("tournamentState", out TournamentState? s))
            {
                s = new TournamentState();
                _context["tournamentState"] = s;
            }
            return s!;
        }
        set => _context["tournamentState"] = value;
    }

    private IMessage? ResultEvent
    {
        get => _context.TryGetValue("resultEvent", out IMessage? evt) ? evt : null;
        set => _context["resultEvent"] = value!;
    }

    private static ByteString PlayerRoot(string name)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hash = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes("player:" + name));
        return ByteString.CopyFrom(hash, 0, 16);
    }

    private void HandleCommand(Func<IMessage> handler)
    {
        try
        {
            var result = handler();
            ResultEvent = result;
            _context.Remove("error");
            ApplyEvent(result);
        }
        catch (CommandRejectedError e)
        {
            ResultEvent = null;
            _context["error"] = e;
        }
        catch (InvalidArgumentError e)
        {
            ResultEvent = null;
            _context["error"] = e;
        }
    }

    private void ApplyEvent(IMessage evt)
    {
        switch (evt)
        {
            case TournamentCreated e: State.ApplyCreated(e); break;
            case RegistrationOpened e: State.ApplyRegistrationOpened(e); break;
            case RegistrationClosed e: State.ApplyRegistrationClosed(e); break;
            case TournamentPlayerEnrolled e: State.ApplyPlayerEnrolled(e); break;
            case TournamentStarted e: State.ApplyTournamentStarted(e); break;
            case RebuyProcessed e: State.ApplyRebuyProcessed(e); break;
            case BlindLevelAdvanced e: State.ApplyBlindAdvanced(e); break;
            case PlayerEliminated e: State.ApplyPlayerEliminated(e); break;
            case TournamentPaused e: State.ApplyPaused(e); break;
            case TournamentResumed e: State.ApplyResumed(e); break;
        }
    }

    private void EnrollPlayer(string name)
    {
        var evt = new TournamentPlayerEnrolled
        {
            PlayerRoot = PlayerRoot(name),
            FeePaid = State.BuyIn,
            StartingStack = State.StartingStack,
            RegistrationNumber = State.RegisteredPlayers.Count + 1,
        };
        State.ApplyPlayerEnrolled(evt);
    }

    private void CreateDefault(string name, int maxPlayers)
    {
        State.ApplyCreated(new TournamentCreated
        {
            Name = name, GameVariant = GameVariant.TexasHoldem,
            BuyIn = 1000, StartingStack = 10000,
            MaxPlayers = maxPlayers, MinPlayers = 2,
        });
    }

    // --- Given ---

    [Given("no prior events for the tournament aggregate")]
    public void NoPriorEvents() => State = new TournamentState();

    [Given(@"a TournamentCreated event for ""(.*)""")]
    public void TournamentCreated(string name) => CreateDefault(name, 100);

    [Given(@"a TournamentCreated event for ""(.*)"" with max-players (\d+)")]
    public void TournamentCreatedMax(string name, int max) => CreateDefault(name, max);

    [Given("a RegistrationOpened event")]
    public void RegistrationOpenedEvent() =>
        State.ApplyRegistrationOpened(new RegistrationOpened());

    [Given("a RegistrationClosed event")]
    public void RegistrationClosedEvent() =>
        State.ApplyRegistrationClosed(new RegistrationClosed
        {
            TotalRegistrations = State.RegisteredPlayers.Count,
        });

    [Given("a TournamentStarted event")]
    public void TournamentStartedEvent() =>
        State.ApplyTournamentStarted(new TournamentStarted
        {
            TotalPlayers = State.RegisteredPlayers.Count,
            TotalPrizePool = State.TotalPrizePool,
        });

    [Given("a TournamentPaused event")]
    public void TournamentPausedEvent() =>
        State.ApplyPaused(new TournamentPaused { Reason = "break" });

    [Given(@"(\d+) players enrolled")]
    public void NPlayersEnrolled(int n) { for (int i = 0; i < n; i++) EnrollPlayer($"player-{i + 1}"); }

    [Given(@"player ""(.*)"" enrolled")]
    public void PlayerEnrolled(string name) => EnrollPlayer(name);

    [Given(@"player ""(.*)"" enrolled with (\d+) rebuys used")]
    public void PlayerEnrolledWithRebuys(string name, int rebuys)
    {
        EnrollPlayer(name);
        for (int i = 0; i < rebuys; i++)
            State.ApplyRebuyProcessed(new RebuyProcessed
            {
                PlayerRoot = PlayerRoot(name),
                RebuyCost = State.RebuyConfig!.RebuyCost,
                ChipsAdded = State.RebuyConfig.RebuyChips,
                RebuyCount = i + 1,
            });
    }

    [Given("a running tournament")]
    public void RunningTournament()
    {
        CreateDefault("Test", 100);
        RegistrationOpenedEvent();
        NPlayersEnrolled(3);
        RegistrationClosedEvent();
        TournamentStartedEvent();
    }

    [Given(@"a running tournament with rebuys enabled max (\d+) cutoff level (\d+)")]
    public void RunningTournamentRebuys(int max, int cutoff)
    {
        var blinds = Enumerable.Range(1, 10).Select(i => new BlindLevel
        { Level = i, SmallBlind = i * 25, BigBlind = i * 50 }).ToList();
        State.ApplyCreated(new TournamentCreated
        {
            Name = "Rebuy", GameVariant = GameVariant.TexasHoldem,
            BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100, MinPlayers = 2,
            RebuyConfig = new RebuyConfig
            { Enabled = true, MaxRebuys = max, RebuyLevelCutoff = cutoff, RebuyCost = 1000, RebuyChips = 10000 },
            BlindStructure = { blinds },
        });
        RegistrationOpenedEvent();
        NPlayersEnrolled(3);
        RegistrationClosedEvent();
        TournamentStartedEvent();
    }

    [Given(@"a running tournament with (\d+)-level blind structure")]
    public void RunningTournamentBlinds(int levels)
    {
        var blinds = Enumerable.Range(1, levels).Select(i => new BlindLevel
        { Level = i, SmallBlind = i * 25, BigBlind = i * 50 }).ToList();
        State.ApplyCreated(new TournamentCreated
        {
            Name = "Blind", GameVariant = GameVariant.TexasHoldem,
            BuyIn = 1000, StartingStack = 10000, MaxPlayers = 100, MinPlayers = 2,
            BlindStructure = { blinds },
        });
        RegistrationOpenedEvent();
        NPlayersEnrolled(3);
        RegistrationClosedEvent();
        TournamentStartedEvent();
    }

    [Given(@"a running tournament with (\d+) players remaining")]
    public void RunningTournamentPlayers(int n)
    {
        CreateDefault("Elim", 100);
        RegistrationOpenedEvent();
        NPlayersEnrolled(n);
        RegistrationClosedEvent();
        TournamentStartedEvent();
    }

    [Given(@"the current blind level is (\d+)")]
    public void CurrentBlindLevel(int level)
    {
        for (int i = 0; i < level; i++)
            State.ApplyBlindAdvanced(new BlindLevelAdvanced
            { Level = i + 1, SmallBlind = (i + 1) * 25, BigBlind = (i + 1) * 50 });
    }

    [Given(@"player at position (\d+) eliminated")]
    public void PlayerAtPositionEliminated(int _)
    {
        var first = State.RegisteredPlayers.First();
        State.ApplyPlayerEliminated(new PlayerEliminated
        {
            PlayerRoot = first.Value.PlayerRoot,
            FinishPosition = State.PlayersRemaining,
        });
    }

    // --- When ---

    [When(@"I handle a CreateTournament command with name ""(.*)"" buy-in (\d+) and starting-stack (\d+)")]
    public void HandleCreate(string name, int buyIn, int stack) =>
        HandleCommand(() => CreateHandler.Handle(new CreateTournament
        { Name = name, GameVariant = GameVariant.TexasHoldem, BuyIn = buyIn, StartingStack = stack, MaxPlayers = 100, MinPlayers = 2 }, State));

    [When(@"I handle a CreateTournament command with name ""(.*)"" buy-in (\d+) starting-stack (\d+) and max-players (\d+)")]
    public void HandleCreateMax(string name, int buyIn, int stack, int max) =>
        HandleCommand(() => CreateHandler.Handle(new CreateTournament
        { Name = name, GameVariant = GameVariant.TexasHoldem, BuyIn = buyIn, StartingStack = stack, MaxPlayers = max, MinPlayers = 2 }, State));

    [When("I handle an OpenRegistration command")]
    public void HandleOpen() =>
        HandleCommand(() => RegistrationHandler.HandleOpen(new OpenRegistration(), State));

    [When("I handle a CloseRegistration command")]
    public void HandleClose() =>
        HandleCommand(() => RegistrationHandler.HandleClose(new CloseRegistration(), State));

    [When(@"I handle an EnrollPlayer command for player ""(.*)""")]
    public void HandleEnroll(string name) =>
        HandleCommand(() => RegistrationHandler.HandleEnroll(new EnrollPlayer { PlayerRoot = PlayerRoot(name) }, State));

    [When(@"I handle a ProcessRebuy command for player ""(.*)""")]
    public void HandleRebuy(string name) =>
        HandleCommand(() => RebuyHandler.Handle(new ProcessRebuy { PlayerRoot = PlayerRoot(name) }, State));

    [When("I handle an AdvanceBlindLevel command")]
    public void HandleAdvance() =>
        HandleCommand(() => LifecycleHandler.HandleAdvanceBlind(new AdvanceBlindLevel(), State));

    [When(@"I handle an EliminatePlayer command for player ""(.*)""")]
    public void HandleEliminate(string name) =>
        HandleCommand(() => LifecycleHandler.HandleEliminate(new EliminatePlayer { PlayerRoot = PlayerRoot(name) }, State));

    [When(@"I handle a PauseTournament command with reason ""(.*)""")]
    public void HandlePause(string reason) =>
        HandleCommand(() => LifecycleHandler.HandlePause(new PauseTournament { Reason = reason }, State));

    [When("I handle a ResumeTournament command")]
    public void HandleResume() =>
        HandleCommand(() => LifecycleHandler.HandleResume(new ResumeTournament(), State));

    [When("I rebuild the tournament state")]
    public void RebuildState() { /* State tracked via ApplyEvent */ }

    // --- Then ---

    [Then(@"^the result is a(?:n)? (?:examples\.)?TournamentCreated event$")]
    public void ResultIsCreated() { ResultEvent.Should().BeOfType<Angzarr.Examples.TournamentCreated>(); }

    [Then(@"^the result is a(?:n)? (?:examples\.)?RegistrationOpened event$")]
    public void ResultIsRegOpen() { ResultEvent.Should().BeOfType<Angzarr.Examples.RegistrationOpened>(); }

    [Then(@"^the result is a(?:n)? (?:examples\.)?RegistrationClosed event$")]
    public void ResultIsRegClosed() { ResultEvent.Should().BeOfType<Angzarr.Examples.RegistrationClosed>(); }

    [Then(@"^the result is a(?:n)? (?:examples\.)?TournamentPlayerEnrolled event$")]
    public void ResultIsEnrolled() { ResultEvent.Should().BeOfType<TournamentPlayerEnrolled>(); }

    [Then(@"^the result is a(?:n)? (?:examples\.)?TournamentEnrollmentRejected event$")]
    public void ResultIsRejected() { ResultEvent.Should().BeOfType<TournamentEnrollmentRejected>(); }

    [Then(@"^the result is a(?:n)? (?:examples\.)?RebuyProcessed event$")]
    public void ResultIsRebuy() { ResultEvent.Should().BeOfType<RebuyProcessed>(); }

    [Then(@"^the result is a(?:n)? (?:examples\.)?RebuyDenied event$")]
    public void ResultIsDenied() { ResultEvent.Should().BeOfType<RebuyDenied>(); }

    [Then(@"^the result is a(?:n)? (?:examples\.)?BlindLevelAdvanced event$")]
    public void ResultIsBlind() { ResultEvent.Should().BeOfType<BlindLevelAdvanced>(); }

    [Then(@"^the result is a(?:n)? (?:examples\.)?PlayerEliminated event$")]
    public void ResultIsEliminated() { ResultEvent.Should().BeOfType<PlayerEliminated>(); }

    [Then(@"^the result is a(?:n)? (?:examples\.)?TournamentPaused event$")]
    public void ResultIsPaused() { ResultEvent.Should().BeOfType<Angzarr.Examples.TournamentPaused>(); }

    [Then(@"^the result is a(?:n)? (?:examples\.)?TournamentResumed event$")]
    public void ResultIsResumed() { ResultEvent.Should().BeOfType<Angzarr.Examples.TournamentResumed>(); }

    [Then(@"the tournament event has name ""(.*)""")]
    public void EventHasName(string expected) =>
        ((Angzarr.Examples.TournamentCreated)ResultEvent!).Name.Should().Be(expected);

    [Then(@"the tournament event has buy_in (\d+)")]
    public void EventHasBuyIn(int expected) =>
        ((Angzarr.Examples.TournamentCreated)ResultEvent!).BuyIn.Should().Be(expected);

    [Then(@"the tournament event has starting_stack (\d+)")]
    public void EventHasStartingStack(int expected)
    {
        if (ResultEvent is Angzarr.Examples.TournamentCreated c) c.StartingStack.Should().Be(expected);
        else if (ResultEvent is TournamentPlayerEnrolled e) e.StartingStack.Should().Be(expected);
        else throw new InvalidOperationException("Event does not have starting_stack");
    }

    [Then(@"the tournament event has total_registrations (\d+)")]
    public void EventHasTotalRegs(int expected) =>
        ((Angzarr.Examples.RegistrationClosed)ResultEvent!).TotalRegistrations.Should().Be(expected);

    [Then(@"the tournament event has fee_paid (\d+)")]
    public void EventHasFeePaid(int expected) =>
        ((TournamentPlayerEnrolled)ResultEvent!).FeePaid.Should().Be(expected);

    [Then(@"the tournament event has registration_number (\d+)")]
    public void EventHasRegNumber(int expected) =>
        ((TournamentPlayerEnrolled)ResultEvent!).RegistrationNumber.Should().Be(expected);

    [Then(@"the tournament event has reason ""(.*)""")]
    public void EventHasReason(string expected)
    {
        if (ResultEvent is TournamentEnrollmentRejected r) r.Reason.Should().Be(expected);
        else if (ResultEvent is RebuyDenied d) d.Reason.Should().Be(expected);
        else if (ResultEvent is Angzarr.Examples.TournamentPaused p) p.Reason.Should().Be(expected);
        else throw new InvalidOperationException("Event does not have reason");
    }

    [Then(@"the tournament event has rebuy_count (\d+)")]
    public void EventHasRebuyCount(int expected) =>
        ((RebuyProcessed)ResultEvent!).RebuyCount.Should().Be(expected);

    [Then(@"the tournament event has level (\d+)")]
    public void EventHasLevel(int expected) =>
        ((BlindLevelAdvanced)ResultEvent!).Level.Should().Be(expected);

    [Then(@"the tournament event has finish_position (\d+)")]
    public void EventHasFinish(int expected) =>
        ((PlayerEliminated)ResultEvent!).FinishPosition.Should().Be(expected);

    [Then(@"the tournament state has status ""(.*)""")]
    public void StateHasStatus(string expected)
    {
        var exp = expected switch
        {
            "CREATED" => TournamentStatus.TournamentCreated,
            "REGISTRATION_OPEN" => TournamentStatus.TournamentRegistrationOpen,
            "RUNNING" => TournamentStatus.TournamentRunning,
            "PAUSED" => TournamentStatus.TournamentPaused,
            "COMPLETED" => TournamentStatus.TournamentCompleted,
            _ => throw new ArgumentException($"Unknown status: {expected}"),
        };
        State.Status.Should().Be(exp);
    }

    [Then(@"the tournament state has (\d+) registered players")]
    public void StateHasPlayers(int expected) =>
        State.RegisteredPlayers.Should().HaveCount(expected);

    [Then(@"the tournament state has (\d+) players remaining")]
    public void StateHasRemaining(int expected) =>
        State.PlayersRemaining.Should().Be(expected);

    [Then(@"the tournament state has prize pool (\d+)")]
    public void StateHasPrizePool(int expected) =>
        State.TotalPrizePool.Should().Be(expected);
}
