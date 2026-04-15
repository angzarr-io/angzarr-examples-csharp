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
        CurrentLevel = 0;
        PlayersRemaining = 0;
        TotalPrizePool = 0;
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
}
