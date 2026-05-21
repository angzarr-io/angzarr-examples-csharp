using Angzarr.Examples;
using Google.Protobuf;

namespace Tournament.Agg;

public class TournamentState
{
    public string Name { get; set; } = "";
    public GameVariant GameVariant { get; set; } = GameVariant.Unspecified;
    public TournamentStatus Status { get; set; } = TournamentStatus.Unspecified;
    public long BuyIn { get; set; }
    public long StartingStack { get; set; }
    public int MaxPlayers { get; set; }
    public int MinPlayers { get; set; }
    public RebuyConfig? RebuyConfig { get; set; }
    public List<BlindLevel> BlindStructure { get; set; } = new();
    public int CurrentLevel { get; set; }
    public Dictionary<string, PlayerRegistration> RegisteredPlayers { get; } = new();
    public int PlayersRemaining { get; set; }
    public long TotalPrizePool { get; set; }

    // Advanced fields for HIGH-EX-2.4.2 handlers
    public bool HandForHand { get; set; }
    public int HandForHandRound { get; set; }
    public HashSet<string> HandForHandActiveTables { get; } = new();
    public HashSet<string> HandForHandPendingTables { get; } = new();
    public Dictionary<string, int> ActivePenalties { get; } = new();  // player_hex -> rounds_remaining
    public HashSet<string> NoShowPlayers { get; } = new();
    public HashSet<string> DisqualifiedPlayers { get; } = new();
    public bool NewHandsHalted { get; set; }
    public List<PlayerBagSnapshot> BagSnapshots { get; } = new();
    public int MixedGameIndex { get; set; }
    public Dictionary<string, long> BountyTotals { get; } = new();  // eliminator_hex -> total
    public long TotalChipsInPlay { get; set; }

    // Payout structure carried from TournamentCreated (used by CompleteTournament).
    public List<PayoutPosition> PayoutStructure { get; } = new();

    // Per-player chip stacks (used by DQ/no-show chip-removal handling and
    // chip-race scenarios). Distinct from RegisteredPlayers, which keys on
    // rebuys/registration ledger fields.
    public Dictionary<string, long> PlayerStacks { get; } = new();

    // Late-registration cutoff (TDA Rule 30 / WSOP §I-12). Zero disables.
    public int RegistrationCutoffLevel { get; set; }

    // Per-player chip inventory by denomination (chip-race scenarios).
    public Dictionary<string, Dictionary<int, int>> PlayerChipInventories { get; } = new();

    // Level clock for H4H pacing (TDA RP-8B / RP-8C).
    public int LevelSecondsRemaining { get; set; }

    // Same-table simultaneous bust orderings keyed by hand_root.hex().
    // Each entry: ordered list of player_root.hex() sorted by pre-hand stack desc.
    public List<List<string>> SimultaneousBustOrderings { get; } = new();
    public List<List<string>> SimultaneousBustGroups { get; } = new();

    public bool Exists => !string.IsNullOrEmpty(Name);
    public bool IsRegistrationOpen => Status == TournamentStatus.TournamentRegistrationOpen;
    public bool IsRunning => Status == TournamentStatus.TournamentRunning;
    public bool IsFull => RegisteredPlayers.Count >= MaxPlayers;
    public bool IsPlayerRegistered(string rootHex) => RegisteredPlayers.ContainsKey(rootHex);

    public int PlayerRebuyCount(string rootHex)
    {
        return RegisteredPlayers.TryGetValue(rootHex, out var reg) ? reg.RebuysUsed : 0;
    }

    // Event appliers

    public void ApplyCreated(TournamentCreated e)
    {
        Name = e.Name;
        GameVariant = e.GameVariant;
        Status = TournamentStatus.TournamentCreated;
        BuyIn = e.BuyIn;
        StartingStack = e.StartingStack;
        MaxPlayers = e.MaxPlayers;
        MinPlayers = e.MinPlayers;
        RebuyConfig = e.RebuyConfig;
        BlindStructure.Clear();
        BlindStructure.AddRange(e.BlindStructure);
        // EU-0842: CurrentLevel starts at 1 (matches Python aggregate semantics —
        // the tournament is conceptually "at level 1" the moment it's created).
        CurrentLevel = 1;
        PlayersRemaining = 0;
        TotalPrizePool = 0;
        RegistrationCutoffLevel = e.RegistrationCutoffLevel;
        PayoutStructure.Clear();
        PayoutStructure.AddRange(e.PayoutStructure);
    }

    public void ApplyRegistrationOpened(RegistrationOpened _) =>
        Status = TournamentStatus.TournamentRegistrationOpen;

    public void ApplyRegistrationClosed(RegistrationClosed _) =>
        Status = TournamentStatus.TournamentCreated;

    public void ApplyPlayerEnrolled(TournamentPlayerEnrolled e)
    {
        var rootHex = Convert.ToHexString(e.PlayerRoot.ToByteArray()).ToLowerInvariant();
        RegisteredPlayers[rootHex] = new PlayerRegistration
        {
            PlayerRoot = e.PlayerRoot,
            FeePaid = e.FeePaid,
            StartingStack = e.StartingStack,
        };
        PlayersRemaining++;
        TotalPrizePool += e.FeePaid;
    }

    public void ApplyTournamentStarted(TournamentStarted _) =>
        Status = TournamentStatus.TournamentRunning;

    public void ApplyRebuyProcessed(RebuyProcessed e)
    {
        var rootHex = Convert.ToHexString(e.PlayerRoot.ToByteArray()).ToLowerInvariant();
        if (RegisteredPlayers.TryGetValue(rootHex, out var reg))
        {
            RegisteredPlayers[rootHex] = new PlayerRegistration(reg) { RebuysUsed = e.RebuyCount };
        }
        TotalPrizePool += e.RebuyCost;
    }

    public void ApplyBlindAdvanced(BlindLevelAdvanced e) => CurrentLevel = e.Level;

    public void ApplyPlayerEliminated(PlayerEliminated e)
    {
        var rootHex = Convert.ToHexString(e.PlayerRoot.ToByteArray()).ToLowerInvariant();
        RegisteredPlayers.Remove(rootHex);
        PlayersRemaining--;
    }

    public void ApplyPaused(TournamentPaused _) => Status = TournamentStatus.TournamentPaused;
    public void ApplyResumed(TournamentResumed _) => Status = TournamentStatus.TournamentRunning;
    public void ApplyCompleted(TournamentCompleted _) => Status = TournamentStatus.TournamentCompleted;

    // ========================================================================
    // Advanced handler appliers — for HIGH-EX-2.4.2 closure
    // ========================================================================

    public void ApplyHandForHandStarted(HandForHandStarted e)
    {
        HandForHand = true;
        HandForHandRound = 0;
        HandForHandActiveTables.Clear();
        HandForHandPendingTables.Clear();
        foreach (var root in e.ActiveTableRoots)
        {
            var hex = Convert.ToHexString(root.ToByteArray()).ToLowerInvariant();
            HandForHandActiveTables.Add(hex);
            HandForHandPendingTables.Add(hex);
        }
    }

    public void ApplyHandForHandRoundComplete(HandForHandRoundComplete e)
    {
        HandForHandRound = e.RoundNumber;
        // Reseed pending tables for the next round
        HandForHandPendingTables.Clear();
        foreach (var hex in HandForHandActiveTables)
            HandForHandPendingTables.Add(hex);
    }

    public void ApplyHandForHandHandRecorded(HandForHandHandRecorded e) { /* clock bookkeeping noop */ }

    public void ApplyHandForHandEnded(HandForHandEnded _)
    {
        HandForHand = false;
        HandForHandActiveTables.Clear();
        HandForHandPendingTables.Clear();
        HandForHandRound = 0;
    }

    public void ApplyPlayerMovedTables(PlayerMovedTables e)
    {
        // Used as a marker event for RecordTableHandComplete.
        // The from_table_root identifies which table just acknowledged.
        var hex = Convert.ToHexString(e.FromTableRoot.ToByteArray()).ToLowerInvariant();
        HandForHandPendingTables.Remove(hex);
    }

    public void ApplyPlayerMovedBetweenTables(PlayerMovedBetweenTables _) { /* table-side bookkeeping */ }

    public void ApplyColorUpCompleted(ColorUpCompleted e)
    {
        // Rule 24A/24C: total_chips_in_play moves by rescue gain minus race loss.
        TotalChipsInPlay += e.ChipsAddedByRescue - e.ChipsRemovedByRace;
    }

    public void ApplySimultaneousBustsRecorded(SimultaneousBustsRecorded e)
    {
        var roots = e.PlayerRoots
            .Select(r => Convert.ToHexString(r.ToByteArray()).ToLowerInvariant())
            .ToList();
        if (e.SameTable)
        {
            // Ordering by pre-hand stack (descending): the head keeps the paid
            // position; lower stacks are demoted in CompleteTournament.
            var ordered = roots
                .OrderByDescending(h => e.PreHandStacks.TryGetValue(h, out var v) ? v : 0L)
                .ToList();
            SimultaneousBustOrderings.Add(ordered);
        }
        else
        {
            // Cross-table: share the worst-paid position among the group.
            SimultaneousBustGroups.Add(roots);
        }
    }

    public void ApplySeatRedrawTriggered(SeatRedrawTriggered _) { /* seat-redraw signal */ }

    public void ApplyPlayerReEntered(PlayerReEntered e)
    {
        TotalChipsInPlay -= e.ChipsForfeited;
        TotalChipsInPlay += StartingStack;
    }

    public void ApplyAbsentBlindAdvanced(AbsentBlindAdvanced e)
    {
        // WSOP Rule 36 — chip transfer from absent player to lone player.
        var loneHex = Convert.ToHexString(e.LonePlayerRoot.ToByteArray()).ToLowerInvariant();
        var absentHex = Convert.ToHexString(e.AbsentPlayerRoot.ToByteArray()).ToLowerInvariant();
        PlayerStacks[loneHex] = PlayerStacks.GetValueOrDefault(loneHex, 0L) + e.StackDelta;
        PlayerStacks[absentHex] = PlayerStacks.GetValueOrDefault(absentHex, 0L) - e.StackDelta;
    }

    public void ApplyBountyAwarded(BountyAwarded e)
    {
        var hex = Convert.ToHexString(e.EliminatorRoot.ToByteArray()).ToLowerInvariant();
        BountyTotals[hex] = BountyTotals.GetValueOrDefault(hex, 0L) + e.Amount;
    }

    public void ApplyNoShowDetected(NoShowDetected e)
    {
        var hex = Convert.ToHexString(e.PlayerRoot.ToByteArray()).ToLowerInvariant();
        NoShowPlayers.Add(hex);
        RegisteredPlayers.Remove(hex);
        PlayerStacks.Remove(hex);
        TotalChipsInPlay -= e.ChipsRemoved;
        if (PlayersRemaining > 0) PlayersRemaining--;
    }

    public void ApplyPenaltyIssued(PenaltyIssued e)
    {
        var hex = Convert.ToHexString(e.PlayerRoot.ToByteArray()).ToLowerInvariant();
        ActivePenalties[hex] = e.MissedHands;
    }

    public void ApplyPenaltyRoundsDecremented(PenaltyRoundsDecremented e)
    {
        var hex = Convert.ToHexString(e.PlayerRoot.ToByteArray()).ToLowerInvariant();
        if (e.RoundsRemaining <= 0)
            ActivePenalties.Remove(hex);
        else
            ActivePenalties[hex] = e.RoundsRemaining;
    }

    public void ApplyPlayerDisqualified(PlayerDisqualified e)
    {
        var hex = Convert.ToHexString(e.PlayerRoot.ToByteArray()).ToLowerInvariant();
        DisqualifiedPlayers.Add(hex);
        RegisteredPlayers.Remove(hex);
        PlayerStacks.Remove(hex);
        TotalChipsInPlay -= e.ChipsRemoved;
        if (PlayersRemaining > 0) PlayersRemaining--;
    }

    public void ApplyNewHandsHalted(NewHandsHalted _) => NewHandsHalted = true;

    public void ApplyBagAndTagComplete(BagAndTagComplete e)
    {
        BagSnapshots.Clear();
        BagSnapshots.AddRange(e.Snapshots);
        Status = TournamentStatus.TournamentBagged;
    }

    public void ApplyMixedGameVariantRotated(MixedGameVariantRotated e)
    {
        GameVariant = e.ToVariant;
        MixedGameIndex++;
    }
}
