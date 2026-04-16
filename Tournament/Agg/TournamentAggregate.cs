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
        var typeUrl = eventAny.TypeUrl;
        var typeName = typeUrl.Contains('/') ? typeUrl.Split('/').Last() : typeUrl;

        switch (typeName)
        {
            case "examples.TournamentCreated":
                state.ApplyCreated(eventAny.Unpack<TournamentCreated>());
                break;
            case "examples.RegistrationOpened":
                state.ApplyRegistrationOpened(eventAny.Unpack<RegistrationOpened>());
                break;
            case "examples.RegistrationClosed":
                state.ApplyRegistrationClosed(eventAny.Unpack<RegistrationClosed>());
                break;
            case "examples.TournamentPlayerEnrolled":
                state.ApplyPlayerEnrolled(eventAny.Unpack<TournamentPlayerEnrolled>());
                break;
            case "examples.TournamentStarted":
                state.ApplyTournamentStarted(eventAny.Unpack<TournamentStarted>());
                break;
            case "examples.RebuyProcessed":
                state.ApplyRebuyProcessed(eventAny.Unpack<RebuyProcessed>());
                break;
            case "examples.BlindLevelAdvanced":
                state.ApplyBlindAdvanced(eventAny.Unpack<BlindLevelAdvanced>());
                break;
            case "examples.PlayerEliminated":
                state.ApplyPlayerEliminated(eventAny.Unpack<PlayerEliminated>());
                break;
            case "examples.TournamentPaused":
                state.ApplyPaused(eventAny.Unpack<TournamentPaused>());
                break;
            case "examples.TournamentResumed":
                state.ApplyResumed(eventAny.Unpack<TournamentResumed>());
                break;
            case "examples.TournamentCompleted":
                state.ApplyCompleted(eventAny.Unpack<TournamentCompleted>());
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
    public BlindLevelAdvanced HandleAdvanceBlindLevel(AdvanceBlindLevel cmd)
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
}
