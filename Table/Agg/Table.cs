using System.Security.Cryptography;
using System.Text;
using Angzarr;
using Angzarr.Client;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Table.Agg;

/// <summary>
/// Table aggregate - OO style with decorator-based command dispatch.
/// </summary>
public class TableAggregate : CommandHandler<TableState>
{
    public const string DomainName = "table";

    public override string Domain => DomainName;

    protected override TableState CreateEmptyState() => new TableState();

    protected override void ApplyEvent(TableState state, Any eventAny)
    {
        TableState.Router.ApplySingle(state, eventAny);
    }

    // --- State accessors ---

    public new bool Exists => State.Exists;
    public string TableId => State.TableId;
    public string TableName => State.TableName;
    public GameVariant GameVariant => State.GameVariant;
    public long SmallBlind => State.SmallBlind;
    public long BigBlind => State.BigBlind;
    public long MinBuyIn => State.MinBuyIn;
    public long MaxBuyIn => State.MaxBuyIn;
    public int MaxPlayers => State.MaxPlayers;
    public Dictionary<int, SeatState> Seats => State.Seats;
    public int DealerPosition => State.DealerPosition;
    public long HandCount => State.HandCount;
    public ByteString CurrentHandRoot => State.CurrentHandRoot;
    public string Status => State.Status;
    public int PlayerCount => State.PlayerCount;
    public int ActivePlayerCount => State.ActivePlayerCount;
    public bool IsFull => State.IsFull;

    public string? GetSeatOccupant(int seat)
    {
        var seatState = State.GetSeat(seat);
        return seatState?.PlayerRoot.ToStringUtf8();
    }

    // --- Command handlers ---

    [Handles(typeof(CreateTable))]
    public TableCreated HandleCreateTable(CreateTable cmd)
    {
        // Codes mirror examples-python/main/table/agg/errors.py CODE
        // attributes. Status is derived from the Py shape parent (see
        // poker/error_shapes.py): MustBePositive / FieldRequired / ValueOutOfRange
        // → INVALID_ARGUMENT; AggregateAlreadyExists / RelationViolation →
        // FAILED_PRECONDITION. Notably BigBlindMustExceedSmallBlind is a
        // RelationViolation so big_blind <= small_blind is a precondition
        // failure (not an input-shape error).
        if (Exists)
            throw CommandRejectedError.PreconditionFailed("TABLE_ALREADY_EXISTS", "Table already exists");
        if (string.IsNullOrEmpty(cmd.TableName))
            throw CommandRejectedError.InvalidArgument("TABLE_NAME_REQUIRED", "table_name is required");
        if (cmd.SmallBlind <= 0)
            throw CommandRejectedError.InvalidArgument("SMALL_BLIND_MUST_BE_POSITIVE", "small_blind must be positive");
        if (cmd.BigBlind <= 0 || cmd.BigBlind < cmd.SmallBlind)
            throw CommandRejectedError.PreconditionFailed("BIG_BLIND_MUST_EXCEED_SMALL_BLIND", "big_blind must be >= small_blind");
        if (cmd.MaxPlayers < 2 || cmd.MaxPlayers > 10)
            throw CommandRejectedError.InvalidArgument("MAX_PLAYERS_OUT_OF_RANGE", "max_players must be between 2 and 10");

        return new TableCreated
        {
            TableName = cmd.TableName,
            GameVariant = cmd.GameVariant,
            SmallBlind = cmd.SmallBlind,
            BigBlind = cmd.BigBlind,
            MinBuyIn = cmd.MinBuyIn != 0 ? cmd.MinBuyIn : cmd.BigBlind * 20,
            MaxBuyIn = cmd.MaxBuyIn != 0 ? cmd.MaxBuyIn : cmd.BigBlind * 100,
            MaxPlayers = cmd.MaxPlayers != 0 ? cmd.MaxPlayers : 9,
            ActionTimeoutSeconds = cmd.ActionTimeoutSeconds != 0 ? cmd.ActionTimeoutSeconds : 30,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Handles(typeof(JoinTable))]
    public PlayerJoined HandleJoinTable(JoinTable cmd)
    {
        // BoundViolation (BuyInBelowMin / BuyInAboveMax) inherits from
        // PreconditionError in Py error_shapes — buy-in below the table
        // minimum is a precondition mismatch, not an input-shape error.
        if (!Exists)
            throw CommandRejectedError.PreconditionFailed("TABLE_NOT_FOUND", "Table does not exist");
        if (cmd.PlayerRoot.IsEmpty)
            throw CommandRejectedError.InvalidArgument("PLAYER_ROOT_REQUIRED", "player_root is required");
        if (State.FindPlayerSeat(cmd.PlayerRoot) != null)
            throw CommandRejectedError.PreconditionFailed("PLAYER_ALREADY_SEATED", "Player already seated at table");
        if (IsFull)
            throw CommandRejectedError.PreconditionFailed("TABLE_IS_FULL", "Table is full");
        if (cmd.BuyInAmount < MinBuyIn)
            throw CommandRejectedError.PreconditionFailed("BUY_IN_BELOW_MIN", $"Buy-in must be at least {MinBuyIn}");
        if (cmd.BuyInAmount > MaxBuyIn)
            throw CommandRejectedError.PreconditionFailed("BUY_IN_ABOVE_MAX", $"Buy-in cannot exceed {MaxBuyIn}");
        if (cmd.PreferredSeat > 0 && State.GetSeat(cmd.PreferredSeat) != null)
            throw CommandRejectedError.PreconditionFailed("SEAT_OCCUPIED", $"Seat {cmd.PreferredSeat} is occupied");

        var seatPosition = State.FindAvailableSeat(cmd.PreferredSeat) ?? 0;

        return new PlayerJoined
        {
            PlayerRoot = cmd.PlayerRoot,
            SeatPosition = seatPosition,
            BuyInAmount = cmd.BuyInAmount,
            Stack = cmd.BuyInAmount,
            JoinedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Handles(typeof(LeaveTable))]
    public PlayerLeft HandleLeaveTable(LeaveTable cmd)
    {
        if (!Exists)
            throw CommandRejectedError.PreconditionFailed("TABLE_NOT_FOUND", "Table does not exist");
        if (cmd.PlayerRoot.IsEmpty)
            throw CommandRejectedError.InvalidArgument("PLAYER_ROOT_REQUIRED", "player_root is required");

        var seat = State.FindPlayerSeat(cmd.PlayerRoot);
        if (seat == null)
            throw CommandRejectedError.PreconditionFailed("PLAYER_NOT_SEATED", "Player is not seated at table");
        if (Status == "in_hand")
            throw CommandRejectedError.PreconditionFailed("CANNOT_LEAVE_DURING_HAND", "Cannot leave table during a hand");

        return new PlayerLeft
        {
            PlayerRoot = cmd.PlayerRoot,
            SeatPosition = seat.Position,
            ChipsCashedOut = seat.Stack,
            LeftAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    // Note: SitOut/SitIn commands are in the Player domain.
    // Table receives PlayerSatOut/PlayerSatIn as facts via saga.

    [Handles(typeof(StartHand))]
    public HandStarted HandleStartHand(StartHand cmd)
    {
        if (!Exists)
            throw CommandRejectedError.PreconditionFailed("TABLE_NOT_FOUND", "Table does not exist");
        if (Status == "in_hand")
            throw CommandRejectedError.PreconditionFailed("HAND_ALREADY_IN_PROGRESS", "Hand already in progress");
        if (ActivePlayerCount < 2)
            throw CommandRejectedError.PreconditionFailed("NOT_ENOUGH_PLAYERS_TO_START_HAND", "Not enough players to start hand");

        var handNumber = HandCount + 1;
        var handRoot = GenerateHandRoot(TableId, handNumber);
        var dealerPosition = State.NextDealerPosition();

        var activePositions = Seats
            .Values.Where(s => !s.IsSittingOut)
            .Select(s => s.Position)
            .OrderBy(p => p)
            .ToList();

        var dealerIdx = activePositions.IndexOf(dealerPosition);
        if (dealerIdx < 0)
            dealerIdx = 0;

        int sbPosition,
            bbPosition;
        if (activePositions.Count == 2)
        {
            sbPosition = activePositions[dealerIdx];
            bbPosition = activePositions[(dealerIdx + 1) % 2];
        }
        else
        {
            sbPosition = activePositions[(dealerIdx + 1) % activePositions.Count];
            bbPosition = activePositions[(dealerIdx + 2) % activePositions.Count];
        }

        var activePlayers = activePositions
            .Select(pos =>
            {
                var seat = Seats[pos];
                return new SeatSnapshot
                {
                    Position = pos,
                    PlayerRoot = seat.PlayerRoot,
                    Stack = seat.Stack,
                };
            })
            .ToList();

        var evt = new HandStarted
        {
            HandRoot = ByteString.CopyFrom(handRoot),
            HandNumber = handNumber,
            DealerPosition = dealerPosition,
            SmallBlindPosition = sbPosition,
            BigBlindPosition = bbPosition,
            GameVariant = GameVariant,
            SmallBlind = SmallBlind,
            BigBlind = BigBlind,
            StartedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
        evt.ActivePlayers.AddRange(activePlayers);

        return evt;
    }

    [Handles(typeof(EndHand))]
    public HandEnded HandleEndHand(EndHand cmd)
    {
        if (!Exists)
            throw CommandRejectedError.PreconditionFailed("TABLE_NOT_FOUND", "Table does not exist");
        if (Status != "in_hand")
            throw CommandRejectedError.PreconditionFailed("NO_HAND_IN_PROGRESS", "No hand in progress");
        if (!cmd.HandRoot.Equals(CurrentHandRoot))
            throw CommandRejectedError.PreconditionFailed("HAND_ROOT_MISMATCH", "Hand root mismatch");

        var stackChanges = new Dictionary<string, long>();
        foreach (var result in cmd.Results)
        {
            var playerHex = Convert.ToHexString(result.WinnerRoot.ToByteArray()).ToLowerInvariant();
            if (!stackChanges.ContainsKey(playerHex))
                stackChanges[playerHex] = 0;
            stackChanges[playerHex] += result.Amount;
        }

        var evt = new HandEnded
        {
            HandRoot = cmd.HandRoot,
            EndedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
        evt.Results.AddRange(cmd.Results);
        foreach (var kvp in stackChanges)
        {
            evt.StackChanges[kvp.Key] = kvp.Value;
        }

        return evt;
    }

    [Handles(typeof(AddChips))]
    public ChipsAdded HandleAddChips(AddChips cmd)
    {
        if (!Exists)
            throw CommandRejectedError.PreconditionFailed("TABLE_NOT_FOUND", "Table does not exist");

        var seat = State.FindPlayerSeat(cmd.PlayerRoot);
        if (seat == null)
            throw CommandRejectedError.PreconditionFailed("PLAYER_NOT_SEATED", "Player is not seated at table");

        var newStack = seat.Stack + cmd.Amount;
        return new ChipsAdded
        {
            PlayerRoot = cmd.PlayerRoot,
            Amount = cmd.Amount,
            NewStack = newStack,
            AddedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    // ========================================================================
    // SeatPlayer + AddRebuyChips — HIGH-EX-2.2.1 / HIGH-EX-2.2.2 closure
    // ========================================================================

    [Handles(typeof(SeatPlayer))]
    public PlayerSeated HandleSeatPlayer(SeatPlayer cmd)
    {
        if (!State.Exists)
            throw CommandRejectedError.PreconditionFailed("TABLE_NOT_FOUND", "Table does not exist");
        if (cmd.PlayerRoot.IsEmpty)
            throw CommandRejectedError.InvalidArgument("PLAYER_ROOT_REQUIRED", "player_root required");
        if (cmd.Amount <= 0)
            throw CommandRejectedError.InvalidArgument("AMOUNT_MUST_BE_POSITIVE", "amount must be positive");

        var seatPos = cmd.Seat < 0 ? (State.FindAvailableSeat() ?? -1) : cmd.Seat;
        if (seatPos < 0)
            throw CommandRejectedError.PreconditionFailed("TABLE_IS_FULL", "No available seat");
        if (State.Seats.ContainsKey(seatPos))
            throw CommandRejectedError.PreconditionFailed("SEAT_OCCUPIED", $"Seat {seatPos} already occupied");

        return new PlayerSeated
        {
            PlayerRoot = cmd.PlayerRoot,
            ReservationId = cmd.ReservationId,
            SeatPosition = seatPos,
            Stack = cmd.Amount,
            SeatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    [Handles(typeof(AddRebuyChips))]
    public RebuyChipsAdded HandleAddRebuyChips(AddRebuyChips cmd)
    {
        if (!State.Exists)
            throw CommandRejectedError.PreconditionFailed("TABLE_NOT_FOUND", "Table does not exist");
        if (cmd.Amount <= 0)
            throw CommandRejectedError.InvalidArgument("AMOUNT_MUST_BE_POSITIVE", "amount must be positive");

        var seat = State.FindPlayerSeat(cmd.PlayerRoot);
        if (seat == null)
            throw CommandRejectedError.PreconditionFailed("PLAYER_NOT_SEATED", "Player not seated at table");

        return new RebuyChipsAdded
        {
            PlayerRoot = cmd.PlayerRoot,
            ReservationId = cmd.ReservationId,
            Seat = cmd.Seat,
            Amount = cmd.Amount,
            NewStack = seat.Stack + cmd.Amount,
            AddedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };
    }

    // ========================================================================
    // Hand-for-hand handlers (3) — HIGH-EX-2.2.3 closure
    // ========================================================================

    [Handles(typeof(EnterTableHandForHand))]
    public TableHandForHandWaiting HandleEnterTableHandForHand(EnterTableHandForHand cmd)
        => HandForHandHandlers.HandleEnterTableHandForHand(cmd, State);

    [Handles(typeof(MarkTableHandForHandHandComplete))]
    public TableHandForHandRoundComplete HandleMarkTableHandForHandHandComplete(
        MarkTableHandForHandHandComplete cmd)
        => HandForHandHandlers.HandleMarkTableHandForHandHandComplete(cmd, State);

    [Handles(typeof(EndTableHandForHand))]
    public TableHandForHandEnded HandleEndTableHandForHand(EndTableHandForHand cmd)
        => HandForHandHandlers.HandleEndTableHandForHand(cmd, State);

    // ========================================================================
    // PR #12 — ChangeSeats. Design decision 2 (2026-05-18):
    // current_seat == requested_seat → REJECT with code SEATS_IDENTICAL
    // (handler-side, emitted BEFORE any seat-lookup logic). Happy path emits
    // (PlayerLeft, PlayerJoined) tuple mirroring the seat change in the
    // existing event vocabulary (no SeatsChanged event exists in the proto).
    // ========================================================================

    [Handles(typeof(ChangeSeats))]
    public IEnumerable<IMessage> HandleChangeSeats(ChangeSeats cmd)
    {
        // PR #12 decision: SEATS_IDENTICAL emitted BEFORE any seat-lookup.
        if (cmd.CurrentSeat == cmd.RequestedSeat)
            throw CommandRejectedError.InvalidArgument(
                ErrorCodes.SeatsIdentical, ErrorMessages.SeatsIdentical);

        if (!Exists)
            throw CommandRejectedError.PreconditionFailed(
                "TABLE_NOT_FOUND", "Table does not exist");
        if (cmd.PlayerRoot.IsEmpty)
            throw CommandRejectedError.InvalidArgument(
                "PLAYER_ROOT_REQUIRED", "player_root is required");
        if (cmd.CurrentSeat < 0)
            throw CommandRejectedError.InvalidArgument(
                "CURRENT_SEAT_INVALID", "current_seat must be non-negative");
        if (cmd.RequestedSeat < 0)
            throw CommandRejectedError.InvalidArgument(
                "REQUESTED_SEAT_INVALID", "requested_seat must be non-negative");
        if (cmd.RequestedSeat >= MaxPlayers)
            throw CommandRejectedError.InvalidArgument(
                "REQUESTED_SEAT_OUT_OF_RANGE",
                $"requested_seat must be < max_players ({MaxPlayers})");

        var current = State.GetSeat(cmd.CurrentSeat);
        if (current == null || !current.PlayerRoot.Equals(cmd.PlayerRoot))
            throw CommandRejectedError.PreconditionFailed(
                "PLAYER_NOT_AT_CURRENT_SEAT",
                $"Player not seated at seat {cmd.CurrentSeat}");
        if (State.GetSeat(cmd.RequestedSeat) != null)
            throw CommandRejectedError.PreconditionFailed(
                "SEAT_OCCUPIED", $"Seat {cmd.RequestedSeat} is occupied");
        if (Status == "in_hand")
            throw CommandRejectedError.PreconditionFailed(
                "CANNOT_CHANGE_SEAT_DURING_HAND",
                "Cannot change seats during a hand");

        var changedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        var left = new PlayerLeft
        {
            PlayerRoot = cmd.PlayerRoot,
            SeatPosition = cmd.CurrentSeat,
            ChipsCashedOut = current.Stack,
            LeftAt = changedAt,
        };
        var joined = new PlayerJoined
        {
            PlayerRoot = cmd.PlayerRoot,
            SeatPosition = cmd.RequestedSeat,
            BuyInAmount = current.Stack,
            Stack = current.Stack,
            JoinedAt = changedAt,
        };
        return new IMessage[] { left, joined };
    }

    private static byte[] GenerateHandRoot(string tableId, long handNumber)
    {
        using var sha = SHA256.Create();
        var input = $"angzarr.poker.hand.{tableId}.{handNumber}";
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return hash.Take(16).ToArray();
    }
}
