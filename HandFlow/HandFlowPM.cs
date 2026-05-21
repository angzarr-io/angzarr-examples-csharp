using Angzarr;
using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf.WellKnownTypes;

namespace HandFlow;

// docs:start:pm_state_oo
/// <summary>
/// Hand-flow phase enum mirroring the Python/Rust canonical 9-phase set.
/// Closes HIGH-EX-4.1 (enum lift) for C#.
/// Full transition-table integration deferred to v2 per Java precedent.
/// </summary>
public enum HandPhase
{
    WAITING_FOR_START,
    DEALING,
    POSTING_BLINDS,
    BETTING,
    DEALING_COMMUNITY,
    DRAW,
    SHOWDOWN,
    AWARDING_POT,
    COMPLETE,
}

/// <summary>
/// PM's aggregate state (rebuilt from its own events).
/// Expanded to track phase per Python/Rust canonical (HIGH-EX-4.1).
/// </summary>
public class PMState
{
    public byte[]? HandRoot { get; set; }
    public bool HandInProgress { get; set; }
    public HandPhase Phase { get; set; } = HandPhase.WAITING_FOR_START;
    public GameVariant GameVariant { get; set; } = GameVariant.Unspecified;
}

// docs:end:pm_state_oo

// docs:start:pm_handler_oo
/// <summary>
/// Hand Flow Process Manager using OO-style attributes.
///
/// This PM orchestrates poker hand flow by:
/// - Tracking when hands start and complete
/// - Coordinating between table and hand domains
/// </summary>
public class HandFlowPM : ProcessManager<PMState>
{
    public override string Name => "hand-flow";

    public HandFlowPM()
        : base() { }

    public HandFlowPM(EventBook? processState)
        : base(processState) { }

    protected override PMState CreateEmptyState() => new();

    protected override void ApplyEvent(PMState state, Any eventAny)
    {
        // In this simplified example, we don't persist PM events.
    }

    /// <summary>
    /// Process the HandStarted event.
    ///
    /// Initialize hand process (not persisted in this simplified version).
    /// The saga-table-hand will send DealCards, so we don't emit commands here.
    /// </summary>
    [Handles(typeof(HandStarted), InputDomain = "table")]
    public List<CommandBook> HandleHandStarted(HandStarted evt, List<EventBook> destinations)
    {
        return new List<CommandBook>();
    }

    /// <summary>
    /// Process the CardsDealt event.
    ///
    /// Post small blind command. In a real implementation, we'd track state
    /// to know which blind to post.
    /// </summary>
    [Handles(typeof(CardsDealt), InputDomain = "hand")]
    public List<CommandBook> HandleCardsDealt(CardsDealt evt, List<EventBook> destinations)
    {
        return new List<CommandBook>();
    }

    /// <summary>
    /// Process the BlindPosted event.
    ///
    /// In a full implementation, we'd check if both blinds are posted
    /// and then start the betting round.
    /// </summary>
    [Handles(typeof(BlindPosted), InputDomain = "hand")]
    public List<CommandBook> HandleBlindPosted(BlindPosted evt, List<EventBook> destinations)
    {
        return new List<CommandBook>();
    }

    /// <summary>
    /// Process the ActionTaken event.
    ///
    /// In a full implementation, we'd check if betting is complete
    /// and advance to the next phase.
    /// </summary>
    [Handles(typeof(ActionTaken), InputDomain = "hand")]
    public List<CommandBook> HandleActionTaken(ActionTaken evt, List<EventBook> destinations)
    {
        return new List<CommandBook>();
    }

    /// <summary>
    /// Process the CommunityCardsDealt event.
    ///
    /// Start new betting round after community cards.
    /// </summary>
    [Handles(typeof(CommunityCardsDealt), InputDomain = "hand")]
    public List<CommandBook> HandleCommunityCardsDealt(
        CommunityCardsDealt evt,
        List<EventBook> destinations
    )
    {
        return new List<CommandBook>();
    }

    /// <summary>
    /// Process the PotAwarded event.
    ///
    /// Hand is complete. Clean up.
    /// </summary>
    [Handles(typeof(PotAwarded), InputDomain = "hand")]
    public List<CommandBook> HandlePotAwarded(PotAwarded evt, List<EventBook> destinations)
    {
        return new List<CommandBook>();
    }
}
// docs:end:pm_handler_oo
