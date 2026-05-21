using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Tournament.Agg.Handlers;

namespace Tournament.Agg;

/// <summary>
/// Tournament aggregate - OO style with decorator-based command dispatch.
///
/// Delegates to static functional handler classes for business logic.
/// </summary>
public class TournamentAggregate : CommandHandler<TournamentState>
{
    public const string DomainName = "tournament";

    public override string Domain => DomainName;

    protected override TournamentState CreateEmptyState() => new TournamentState();

    protected override void ApplyEvent(TournamentState state, Any eventAny)
    {
        // Canonical proto FQN per `package angzarr_client.proto.examples;`
        // (proto/angzarr_client/proto/examples/tournament.proto). The
        // historical "examples.X" keys never matched wire payloads and
        // silently dropped every Tournament event — Replay produced a
        // permanently-blank state.
        var typeUrl = eventAny.TypeUrl;
        var typeName = typeUrl.Contains('/') ? typeUrl.Split('/').Last() : typeUrl;

        switch (typeName)
        {
            case "angzarr_client.proto.examples.v1.TournamentCreated":
                state.ApplyCreated(eventAny.Unpack<TournamentCreated>());
                break;
            case "angzarr_client.proto.examples.v1.RegistrationOpened":
                state.ApplyRegistrationOpened(eventAny.Unpack<RegistrationOpened>());
                break;
            case "angzarr_client.proto.examples.v1.RegistrationClosed":
                state.ApplyRegistrationClosed(eventAny.Unpack<RegistrationClosed>());
                break;
            case "angzarr_client.proto.examples.v1.TournamentPlayerEnrolled":
                state.ApplyPlayerEnrolled(eventAny.Unpack<TournamentPlayerEnrolled>());
                break;
            case "angzarr_client.proto.examples.v1.TournamentStarted":
                state.ApplyTournamentStarted(eventAny.Unpack<TournamentStarted>());
                break;
            case "angzarr_client.proto.examples.v1.RebuyProcessed":
                state.ApplyRebuyProcessed(eventAny.Unpack<RebuyProcessed>());
                break;
            case "angzarr_client.proto.examples.v1.BlindLevelAdvanced":
                state.ApplyBlindAdvanced(eventAny.Unpack<BlindLevelAdvanced>());
                break;
            case "angzarr_client.proto.examples.v1.PlayerEliminated":
                state.ApplyPlayerEliminated(eventAny.Unpack<PlayerEliminated>());
                break;
            case "angzarr_client.proto.examples.v1.TournamentPaused":
                state.ApplyPaused(eventAny.Unpack<TournamentPaused>());
                break;
            case "angzarr_client.proto.examples.v1.TournamentResumed":
                state.ApplyResumed(eventAny.Unpack<TournamentResumed>());
                break;
            case "angzarr_client.proto.examples.v1.TournamentCompleted":
                state.ApplyCompleted(eventAny.Unpack<TournamentCompleted>());
                break;
            // Advanced handler appliers
            case "angzarr_client.proto.examples.v1.HandForHandStarted":
                state.ApplyHandForHandStarted(eventAny.Unpack<HandForHandStarted>());
                break;
            case "angzarr_client.proto.examples.v1.HandForHandRoundComplete":
                state.ApplyHandForHandRoundComplete(eventAny.Unpack<HandForHandRoundComplete>());
                break;
            case "angzarr_client.proto.examples.v1.HandForHandHandRecorded":
                state.ApplyHandForHandHandRecorded(eventAny.Unpack<HandForHandHandRecorded>());
                break;
            case "angzarr_client.proto.examples.v1.HandForHandEnded":
                state.ApplyHandForHandEnded(eventAny.Unpack<HandForHandEnded>());
                break;
            case "angzarr_client.proto.examples.v1.PlayerMovedTables":
                state.ApplyPlayerMovedTables(eventAny.Unpack<PlayerMovedTables>());
                break;
            case "angzarr_client.proto.examples.v1.PlayerMovedBetweenTables":
                state.ApplyPlayerMovedBetweenTables(eventAny.Unpack<PlayerMovedBetweenTables>());
                break;
            case "angzarr_client.proto.examples.v1.ColorUpCompleted":
                state.ApplyColorUpCompleted(eventAny.Unpack<ColorUpCompleted>());
                break;
            case "angzarr_client.proto.examples.v1.SimultaneousBustsRecorded":
                state.ApplySimultaneousBustsRecorded(eventAny.Unpack<SimultaneousBustsRecorded>());
                break;
            case "angzarr_client.proto.examples.v1.SeatRedrawTriggered":
                state.ApplySeatRedrawTriggered(eventAny.Unpack<SeatRedrawTriggered>());
                break;
            case "angzarr_client.proto.examples.v1.PlayerReEntered":
                state.ApplyPlayerReEntered(eventAny.Unpack<PlayerReEntered>());
                break;
            case "angzarr_client.proto.examples.v1.AbsentBlindAdvanced":
                state.ApplyAbsentBlindAdvanced(eventAny.Unpack<AbsentBlindAdvanced>());
                break;
            case "angzarr_client.proto.examples.v1.BountyAwarded":
                state.ApplyBountyAwarded(eventAny.Unpack<BountyAwarded>());
                break;
            case "angzarr_client.proto.examples.v1.NoShowDetected":
                state.ApplyNoShowDetected(eventAny.Unpack<NoShowDetected>());
                break;
            case "angzarr_client.proto.examples.v1.PenaltyIssued":
                state.ApplyPenaltyIssued(eventAny.Unpack<PenaltyIssued>());
                break;
            case "angzarr_client.proto.examples.v1.PenaltyRoundsDecremented":
                state.ApplyPenaltyRoundsDecremented(eventAny.Unpack<PenaltyRoundsDecremented>());
                break;
            case "angzarr_client.proto.examples.v1.PlayerDisqualified":
                state.ApplyPlayerDisqualified(eventAny.Unpack<PlayerDisqualified>());
                break;
            case "angzarr_client.proto.examples.v1.NewHandsHalted":
                state.ApplyNewHandsHalted(eventAny.Unpack<NewHandsHalted>());
                break;
            case "angzarr_client.proto.examples.v1.BagAndTagComplete":
                state.ApplyBagAndTagComplete(eventAny.Unpack<BagAndTagComplete>());
                break;
            case "angzarr_client.proto.examples.v1.MixedGameVariantRotated":
                state.ApplyMixedGameVariantRotated(eventAny.Unpack<MixedGameVariantRotated>());
                break;
        }
    }

    // --- Command handlers ---

    [Handles(typeof(CreateTournament))]
    public TournamentCreated HandleCreateTournament(CreateTournament cmd)
    {
        return CreateTournamentHandler.Handle(cmd, State);
    }

    [Handles(typeof(OpenRegistration))]
    public RegistrationOpened HandleOpenRegistration(OpenRegistration cmd)
    {
        return RegistrationHandler.HandleOpenRegistration(cmd, State);
    }

    [Handles(typeof(CloseRegistration))]
    public RegistrationClosed HandleCloseRegistration(CloseRegistration cmd)
    {
        return RegistrationHandler.HandleCloseRegistration(cmd, State);
    }

    [Handles(typeof(EnrollPlayer))]
    public IMessage HandleEnrollPlayer(EnrollPlayer cmd)
    {
        return RegistrationHandler.HandleEnrollPlayer(cmd, State);
    }

    [Handles(typeof(AdvanceBlindLevel))]
    public IMessage HandleAdvanceBlindLevel(AdvanceBlindLevel cmd)
    {
        return LifecycleHandler.HandleAdvanceBlindLevel(cmd, State);
    }

    [Handles(typeof(EliminatePlayer))]
    public PlayerEliminated HandleEliminatePlayer(EliminatePlayer cmd)
    {
        return LifecycleHandler.HandleEliminatePlayer(cmd, State);
    }

    [Handles(typeof(PauseTournament))]
    public TournamentPaused HandlePauseTournament(PauseTournament cmd)
    {
        return LifecycleHandler.HandlePauseTournament(cmd, State);
    }

    [Handles(typeof(ResumeTournament))]
    public TournamentResumed HandleResumeTournament(ResumeTournament cmd)
    {
        return LifecycleHandler.HandleResumeTournament(cmd, State);
    }

    [Handles(typeof(ProcessRebuy))]
    public IMessage HandleProcessRebuy(ProcessRebuy cmd)
    {
        return ProcessRebuyHandler.Handle(cmd, State);
    }

    [Handles(typeof(StartTournament))]
    public TournamentStarted HandleStartTournament(StartTournament cmd)
    {
        return LifecycleHandler.HandleStartTournament(cmd, State);
    }

    [Handles(typeof(CompleteTournament))]
    public TournamentCompleted HandleCompleteTournament(CompleteTournament cmd)
    {
        return CompleteTournamentHandler.Handle(cmd, State);
    }

    // ========================================================================
    // Advanced handlers (19) — HIGH-EX-2.4.2 / HIGH-EX-PY-RS-3 closure
    // ========================================================================

    [Handles(typeof(EnterHandForHand))]
    public HandForHandStarted HandleEnterHandForHand(EnterHandForHand cmd)
        => AdvancedHandlers.HandleEnterHandForHand(cmd, State);

    [Handles(typeof(RecordTableHandComplete))]
    public PlayerMovedTables HandleRecordTableHandComplete(RecordTableHandComplete cmd)
        => AdvancedHandlers.HandleRecordTableHandComplete(cmd, State);

    [Handles(typeof(RecordHandForHandRoundComplete))]
    public HandForHandRoundComplete HandleRecordHandForHandRoundComplete(RecordHandForHandRoundComplete cmd)
        => AdvancedHandlers.HandleRecordHandForHandRoundComplete(cmd, State);

    [Handles(typeof(RecordHandForHandHand))]
    public HandForHandHandRecorded HandleRecordHandForHandHand(RecordHandForHandHand cmd)
        => AdvancedHandlers.HandleRecordHandForHandHand(cmd, State);

    [Handles(typeof(ColorUp))]
    public ColorUpCompleted HandleColorUp(ColorUp cmd)
        => AdvancedHandlers.HandleColorUp(cmd, State);

    [Handles(typeof(RebalanceTables))]
    public PlayerMovedBetweenTables HandleRebalanceTables(RebalanceTables cmd)
        => AdvancedHandlers.HandleRebalanceTables(cmd, State);

    [Handles(typeof(RecordSimultaneousBusts))]
    public SimultaneousBustsRecorded HandleRecordSimultaneousBusts(RecordSimultaneousBusts cmd)
        => AdvancedHandlers.HandleRecordSimultaneousBusts(cmd, State);

    [Handles(typeof(TriggerSeatRedraw))]
    public SeatRedrawTriggered HandleTriggerSeatRedraw(TriggerSeatRedraw cmd)
        => AdvancedHandlers.HandleTriggerSeatRedraw(cmd, State);

    [Handles(typeof(ReseatAbsentPlayer))]
    public PlayerMovedBetweenTables HandleReseatAbsentPlayer(ReseatAbsentPlayer cmd)
        => AdvancedHandlers.HandleReseatAbsentPlayer(cmd, State);

    [Handles(typeof(ReEntryPlayer))]
    public PlayerReEntered HandleReEntryPlayer(ReEntryPlayer cmd)
        => AdvancedHandlers.HandleReEntryPlayer(cmd, State);

    [Handles(typeof(AdvanceAbsentBlind))]
    public AbsentBlindAdvanced HandleAdvanceAbsentBlind(AdvanceAbsentBlind cmd)
        => AdvancedHandlers.HandleAdvanceAbsentBlind(cmd, State);

    [Handles(typeof(AwardBounty))]
    public BountyAwarded HandleAwardBounty(AwardBounty cmd)
        => AdvancedHandlers.HandleAwardBounty(cmd, State);

    [Handles(typeof(DetectNoShow))]
    public NoShowDetected HandleDetectNoShow(DetectNoShow cmd)
        => AdvancedHandlers.HandleDetectNoShow(cmd, State);

    [Handles(typeof(IssuePenalty))]
    public PenaltyIssued HandleIssuePenalty(IssuePenalty cmd)
        => AdvancedHandlers.HandleIssuePenalty(cmd, State);

    [Handles(typeof(DecrementPenalty))]
    public PenaltyRoundsDecremented HandleDecrementPenalty(DecrementPenalty cmd)
        => AdvancedHandlers.HandleDecrementPenalty(cmd, State);

    [Handles(typeof(DisqualifyPlayer))]
    public PlayerDisqualified HandleDisqualifyPlayer(DisqualifyPlayer cmd)
        => AdvancedHandlers.HandleDisqualifyPlayer(cmd, State);

    [Handles(typeof(StopNewHands))]
    public NewHandsHalted HandleStopNewHands(StopNewHands cmd)
        => AdvancedHandlers.HandleStopNewHands(cmd, State);

    [Handles(typeof(BagAndTag))]
    public BagAndTagComplete HandleBagAndTag(BagAndTag cmd)
        => AdvancedHandlers.HandleBagAndTag(cmd, State);

    [Handles(typeof(RotateMixedGameVariant))]
    public MixedGameVariantRotated HandleRotateMixedGameVariant(RotateMixedGameVariant cmd)
        => AdvancedHandlers.HandleRotateMixedGameVariant(cmd, State);
}
