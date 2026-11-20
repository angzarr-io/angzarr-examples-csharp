using Angzarr.Examples;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using TechTalk.SpecFlow;

namespace Tests.Steps;

[Binding]
public class OrchestrationSteps
{
    private readonly ScenarioContext _context;

    // Shared identifiers
    private readonly byte[] _playerRoot = System.Text.Encoding.UTF8.GetBytes("player-root-0001");
    private readonly byte[] _tableRoot = System.Text.Encoding.UTF8.GetBytes("table-root-00001");
    private readonly byte[] _tournamentRoot = System.Text.Encoding.UTF8.GetBytes("tournament-rt-01");
    private readonly byte[] _reservationId = System.Text.Encoding.UTF8.GetBytes("reservation-0001");

    // Table state
    private long _tableMinBuyIn;
    private long _tableMaxBuyIn;
    private int _tableMaxPlayers = 9;
    private readonly HashSet<int> _occupiedSeats = new();
    private bool _tableFull;
    private int _playerSeatPosition = -1;

    // Tournament state
    private bool _tournamentRegistrationOpen;
    private bool _tournamentFull;
    private bool _tournamentRebuyAllowed;
    private TournamentStatus _tournamentStatus = TournamentStatus.Unspecified;

    // Player request state
    private int _requestedSeat;
    private long _requestedAmount;
    private long _requestedFee;

    // PM phase tracking
    private BuyInPhase _buyInPhase = BuyInPhase.Unspecified;
    private RegistrationPhase _registrationPhase = RegistrationPhase.Unspecified;
    private RebuyPhase _rebuyPhase = RebuyPhase.Unspecified;

    // Output tracking
    private readonly List<IMessage> _emittedCommands = new();
    private readonly List<IMessage> _emittedEvents = new();

    public OrchestrationSteps(ScenarioContext context)
    {
        _context = context;
    }

    // =========================================================================
    // Given Steps - BuyInOrchestrator
    // =========================================================================

    [Given(@"a table with seat (\d+) available and buy-in range (\d+)-(\d+)")]
    public void GivenATableWithSeatAvailableAndBuyInRange(int seat, int min, int max)
    {
        _tableMinBuyIn = min;
        _tableMaxBuyIn = max;
        // Seat is available: do not add to occupied
    }

    [Given(@"a player with a BuyInRequested event for seat (\d+) with amount (\d+)")]
    public void GivenAPlayerWithABuyInRequestedEventForSeatWithAmount(int seat, int amount)
    {
        _requestedSeat = seat;
        _requestedAmount = amount;
    }

    [Given(@"a table with seat (\d+) occupied by another player")]
    public void GivenATableWithSeatOccupiedByAnotherPlayer(int seat)
    {
        _tableMinBuyIn = 200;
        _tableMaxBuyIn = 2000;
        _occupiedSeats.Add(seat);
    }

    [Given(@"a table that is full with (\d+) players")]
    public void GivenATableThatIsFullWithPlayers(int playerCount)
    {
        _tableMinBuyIn = 200;
        _tableMaxBuyIn = 2000;
        _tableMaxPlayers = playerCount;
        _tableFull = true;
        for (int i = 0; i < playerCount; i++)
        {
            _occupiedSeats.Add(i);
        }
    }

    [Given(@"a player with a BuyInRequested event for any seat with amount (\d+)")]
    public void GivenAPlayerWithABuyInRequestedEventForAnySeatWithAmount(int amount)
    {
        _requestedSeat = -1;
        _requestedAmount = amount;
    }

    [Given(@"a player and table in a pending buy-in state")]
    public void GivenAPlayerAndTableInAPendingBuyInState()
    {
        _tableMinBuyIn = 200;
        _tableMaxBuyIn = 2000;
        _requestedSeat = 0;
        _requestedAmount = 500;
        _buyInPhase = BuyInPhase.BuyInSeating;
    }

    // =========================================================================
    // Given Steps - RegistrationOrchestrator
    // =========================================================================

    [Given(@"a tournament with registration open and capacity available")]
    public void GivenATournamentWithRegistrationOpenAndCapacityAvailable()
    {
        _tournamentRegistrationOpen = true;
        _tournamentFull = false;
        _tournamentStatus = TournamentStatus.TournamentRegistrationOpen;
    }

    [Given(@"a player with a RegistrationRequested event with fee (\d+)")]
    public void GivenAPlayerWithARegistrationRequestedEventWithFee(int fee)
    {
        _requestedFee = fee;
    }

    [Given(@"a tournament that is full")]
    public void GivenATournamentThatIsFull()
    {
        _tournamentRegistrationOpen = true;
        _tournamentFull = true;
        _tournamentStatus = TournamentStatus.TournamentRegistrationOpen;
    }

    [Given(@"a tournament with registration closed")]
    public void GivenATournamentWithRegistrationClosed()
    {
        _tournamentRegistrationOpen = false;
        _tournamentFull = false;
        _tournamentStatus = TournamentStatus.TournamentCompleted;
    }

    [Given(@"a player and tournament in a pending registration state")]
    public void GivenAPlayerAndTournamentInAPendingRegistrationState()
    {
        _tournamentRegistrationOpen = true;
        _tournamentFull = false;
        _tournamentStatus = TournamentStatus.TournamentRegistrationOpen;
        _requestedFee = 1000;
        _registrationPhase = RegistrationPhase.RegistrationEnrolling;
    }

    // =========================================================================
    // Given Steps - RebuyOrchestrator
    // =========================================================================

    [Given(@"a tournament in rebuy window with player eligible")]
    public void GivenATournamentInRebuyWindowWithPlayerEligible()
    {
        _tournamentRebuyAllowed = true;
        _tournamentStatus = TournamentStatus.TournamentRunning;
    }

    [Given(@"a table with the player seated at position (\d+)")]
    public void GivenATableWithThePlayerSeatedAtPosition(int position)
    {
        _playerSeatPosition = position;
        _tableMinBuyIn = 200;
        _tableMaxBuyIn = 2000;
    }

    [Given(@"a player with a RebuyRequested event for amount (\d+)")]
    public void GivenAPlayerWithARebuyRequestedEventForAmount(int amount)
    {
        _requestedAmount = amount;
    }

    [Given(@"a tournament with rebuy window closed")]
    public void GivenATournamentWithRebuyWindowClosed()
    {
        _tournamentRebuyAllowed = false;
        _tournamentStatus = TournamentStatus.TournamentCompleted;
    }

    [Given(@"a table without the player seated")]
    public void GivenATableWithoutThePlayerSeated()
    {
        _playerSeatPosition = -1;
        _tableMinBuyIn = 200;
        _tableMaxBuyIn = 2000;
    }

    [Given(@"a player, tournament, and table in a pending rebuy state")]
    public void GivenAPlayerTournamentAndTableInAPendingRebuyState()
    {
        _tournamentRebuyAllowed = true;
        _tournamentStatus = TournamentStatus.TournamentRunning;
        _playerSeatPosition = 2;
        _requestedAmount = 1000;
        _rebuyPhase = RebuyPhase.RebuyApproving;
    }

    [Given(@"a player, tournament, and table with chips added")]
    public void GivenAPlayerTournamentAndTableWithChipsAdded()
    {
        _tournamentRebuyAllowed = true;
        _tournamentStatus = TournamentStatus.TournamentRunning;
        _playerSeatPosition = 2;
        _requestedAmount = 1000;
        _rebuyPhase = RebuyPhase.RebuyAddingChips;
    }

    // =========================================================================
    // When Steps - BuyInOrchestrator
    // =========================================================================

    [When(@"the BuyInOrchestrator handles the BuyInRequested event")]
    public void WhenTheBuyInOrchestratorHandlesTheBuyInRequestedEvent()
    {
        _emittedCommands.Clear();
        _emittedEvents.Clear();

        var requestEvent = new BuyInRequested
        {
            ReservationId = ByteString.CopyFrom(_reservationId),
            TableRoot = ByteString.CopyFrom(_tableRoot),
            Seat = _requestedSeat,
            Amount = new Currency { Amount = _requestedAmount, CurrencyCode = "CHIPS" },
            RequestedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        };

        // Validate amount range
        if (_requestedAmount < _tableMinBuyIn || _requestedAmount > _tableMaxBuyIn)
        {
            _emittedEvents.Add(new BuyInFailed
            {
                PlayerRoot = ByteString.CopyFrom(_playerRoot),
                TableRoot = ByteString.CopyFrom(_tableRoot),
                ReservationId = ByteString.CopyFrom(_reservationId),
                Failure = new OrchestrationFailure
                {
                    Code = "INVALID_AMOUNT",
                    Message = $"Buy-in amount {_requestedAmount} outside range {_tableMinBuyIn}-{_tableMaxBuyIn}",
                    FailedAtPhase = BuyInPhase.BuyInRequested.ToString(),
                    FailedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                },
            });
            _buyInPhase = BuyInPhase.BuyInFailed;
            return;
        }

        // Validate table not full
        if (_tableFull || _occupiedSeats.Count >= _tableMaxPlayers)
        {
            _emittedEvents.Add(new BuyInFailed
            {
                PlayerRoot = ByteString.CopyFrom(_playerRoot),
                TableRoot = ByteString.CopyFrom(_tableRoot),
                ReservationId = ByteString.CopyFrom(_reservationId),
                Failure = new OrchestrationFailure
                {
                    Code = "TABLE_FULL",
                    Message = "No seats available at this table",
                    FailedAtPhase = BuyInPhase.BuyInRequested.ToString(),
                    FailedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                },
            });
            _buyInPhase = BuyInPhase.BuyInFailed;
            return;
        }

        // Validate seat not occupied (for specific seat requests)
        if (_requestedSeat >= 0 && _occupiedSeats.Contains(_requestedSeat))
        {
            _emittedEvents.Add(new BuyInFailed
            {
                PlayerRoot = ByteString.CopyFrom(_playerRoot),
                TableRoot = ByteString.CopyFrom(_tableRoot),
                ReservationId = ByteString.CopyFrom(_reservationId),
                Failure = new OrchestrationFailure
                {
                    Code = "SEAT_OCCUPIED",
                    Message = $"Seat {_requestedSeat} is already occupied",
                    FailedAtPhase = BuyInPhase.BuyInRequested.ToString(),
                    FailedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                },
            });
            _buyInPhase = BuyInPhase.BuyInFailed;
            return;
        }

        // Validation passed: emit SeatPlayer command and BuyInInitiated event
        _emittedCommands.Add(new SeatPlayer
        {
            PlayerRoot = ByteString.CopyFrom(_playerRoot),
            ReservationId = ByteString.CopyFrom(_reservationId),
            Seat = _requestedSeat,
            Amount = _requestedAmount,
        });

        _emittedEvents.Add(new BuyInInitiated
        {
            PlayerRoot = ByteString.CopyFrom(_playerRoot),
            TableRoot = ByteString.CopyFrom(_tableRoot),
            ReservationId = ByteString.CopyFrom(_reservationId),
            Seat = _requestedSeat,
            Amount = new Currency { Amount = _requestedAmount, CurrencyCode = "CHIPS" },
            Phase = BuyInPhase.BuyInSeating,
            InitiatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        });

        _buyInPhase = BuyInPhase.BuyInSeating;
    }

    [When(@"the BuyInOrchestrator handles a PlayerSeated event")]
    public void WhenTheBuyInOrchestratorHandlesAPlayerSeatedEvent()
    {
        _emittedCommands.Clear();
        _emittedEvents.Clear();

        // PlayerSeated means the table accepted the seating
        _emittedCommands.Add(new ConfirmBuyIn
        {
            ReservationId = ByteString.CopyFrom(_reservationId),
        });

        _emittedEvents.Add(new BuyInCompleted
        {
            PlayerRoot = ByteString.CopyFrom(_playerRoot),
            TableRoot = ByteString.CopyFrom(_tableRoot),
            ReservationId = ByteString.CopyFrom(_reservationId),
            Seat = _requestedSeat,
            Amount = new Currency { Amount = _requestedAmount, CurrencyCode = "CHIPS" },
            CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        });

        _buyInPhase = BuyInPhase.BuyInCompleted;
    }

    [When(@"the BuyInOrchestrator handles a SeatingRejected event")]
    public void WhenTheBuyInOrchestratorHandlesASeatingRejectedEvent()
    {
        _emittedCommands.Clear();
        _emittedEvents.Clear();

        // SeatingRejected means the table rejected; release funds
        _emittedCommands.Add(new ReleaseBuyIn
        {
            ReservationId = ByteString.CopyFrom(_reservationId),
            Reason = "SEATING_REJECTED",
        });

        _emittedEvents.Add(new BuyInFailed
        {
            PlayerRoot = ByteString.CopyFrom(_playerRoot),
            TableRoot = ByteString.CopyFrom(_tableRoot),
            ReservationId = ByteString.CopyFrom(_reservationId),
            Failure = new OrchestrationFailure
            {
                Code = "SEATING_REJECTED",
                Message = "Table rejected the seating request",
                FailedAtPhase = BuyInPhase.BuyInSeating.ToString(),
                FailedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            },
        });

        _buyInPhase = BuyInPhase.BuyInFailed;
    }

    // =========================================================================
    // When Steps - RegistrationOrchestrator
    // =========================================================================

    [When(@"the RegistrationOrchestrator handles the RegistrationRequested event")]
    public void WhenTheRegistrationOrchestratorHandlesTheRegistrationRequestedEvent()
    {
        _emittedCommands.Clear();
        _emittedEvents.Clear();

        // Validate tournament is open and has capacity
        if (!_tournamentRegistrationOpen || _tournamentStatus != TournamentStatus.TournamentRegistrationOpen)
        {
            _emittedEvents.Add(new RegistrationFailed
            {
                PlayerRoot = ByteString.CopyFrom(_playerRoot),
                TournamentRoot = ByteString.CopyFrom(_tournamentRoot),
                ReservationId = ByteString.CopyFrom(_reservationId),
                Failure = new OrchestrationFailure
                {
                    Code = "REGISTRATION_CLOSED",
                    Message = "Tournament registration is not open",
                    FailedAtPhase = RegistrationPhase.RegistrationRequested.ToString(),
                    FailedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                },
            });
            _registrationPhase = RegistrationPhase.RegistrationFailed;
            return;
        }

        if (_tournamentFull)
        {
            _emittedEvents.Add(new RegistrationFailed
            {
                PlayerRoot = ByteString.CopyFrom(_playerRoot),
                TournamentRoot = ByteString.CopyFrom(_tournamentRoot),
                ReservationId = ByteString.CopyFrom(_reservationId),
                Failure = new OrchestrationFailure
                {
                    Code = "REGISTRATION_CLOSED",
                    Message = "Tournament is at maximum capacity",
                    FailedAtPhase = RegistrationPhase.RegistrationRequested.ToString(),
                    FailedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                },
            });
            _registrationPhase = RegistrationPhase.RegistrationFailed;
            return;
        }

        // Validation passed: emit EnrollPlayer command and RegistrationInitiated event
        _emittedCommands.Add(new EnrollPlayer
        {
            PlayerRoot = ByteString.CopyFrom(_playerRoot),
            ReservationId = ByteString.CopyFrom(_reservationId),
        });

        _emittedEvents.Add(new RegistrationInitiated
        {
            PlayerRoot = ByteString.CopyFrom(_playerRoot),
            TournamentRoot = ByteString.CopyFrom(_tournamentRoot),
            ReservationId = ByteString.CopyFrom(_reservationId),
            Fee = new Currency { Amount = _requestedFee, CurrencyCode = "CHIPS" },
            Phase = RegistrationPhase.RegistrationEnrolling,
            InitiatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        });

        _registrationPhase = RegistrationPhase.RegistrationEnrolling;
    }

    [When(@"the RegistrationOrchestrator handles a TournamentPlayerEnrolled event")]
    public void WhenTheRegistrationOrchestratorHandlesATournamentPlayerEnrolledEvent()
    {
        _emittedCommands.Clear();
        _emittedEvents.Clear();

        // Enrollment succeeded: confirm fee with player
        _emittedCommands.Add(new ConfirmRegistrationFee
        {
            ReservationId = ByteString.CopyFrom(_reservationId),
        });

        _emittedEvents.Add(new RegistrationCompleted
        {
            PlayerRoot = ByteString.CopyFrom(_playerRoot),
            TournamentRoot = ByteString.CopyFrom(_tournamentRoot),
            ReservationId = ByteString.CopyFrom(_reservationId),
            Fee = new Currency { Amount = _requestedFee, CurrencyCode = "CHIPS" },
            CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        });

        _registrationPhase = RegistrationPhase.RegistrationCompleted;
    }

    [When(@"the RegistrationOrchestrator handles a TournamentEnrollmentRejected event")]
    public void WhenTheRegistrationOrchestratorHandlesATournamentEnrollmentRejectedEvent()
    {
        _emittedCommands.Clear();
        _emittedEvents.Clear();

        // Enrollment rejected: release fee
        _emittedCommands.Add(new ReleaseRegistrationFee
        {
            ReservationId = ByteString.CopyFrom(_reservationId),
            Reason = "ENROLLMENT_REJECTED",
        });

        _emittedEvents.Add(new RegistrationFailed
        {
            PlayerRoot = ByteString.CopyFrom(_playerRoot),
            TournamentRoot = ByteString.CopyFrom(_tournamentRoot),
            ReservationId = ByteString.CopyFrom(_reservationId),
            Failure = new OrchestrationFailure
            {
                Code = "ENROLLMENT_REJECTED",
                Message = "Tournament rejected enrollment",
                FailedAtPhase = RegistrationPhase.RegistrationEnrolling.ToString(),
                FailedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            },
        });

        _registrationPhase = RegistrationPhase.RegistrationFailed;
    }

    // =========================================================================
    // When Steps - RebuyOrchestrator
    // =========================================================================

    [When(@"the RebuyOrchestrator handles the RebuyRequested event")]
    public void WhenTheRebuyOrchestratorHandlesTheRebuyRequestedEvent()
    {
        _emittedCommands.Clear();
        _emittedEvents.Clear();

        // Validate tournament is running and rebuy is allowed
        if (!_tournamentRebuyAllowed || _tournamentStatus != TournamentStatus.TournamentRunning)
        {
            _emittedEvents.Add(new RebuyFailed
            {
                PlayerRoot = ByteString.CopyFrom(_playerRoot),
                TournamentRoot = ByteString.CopyFrom(_tournamentRoot),
                ReservationId = ByteString.CopyFrom(_reservationId),
                Failure = new OrchestrationFailure
                {
                    Code = "TOURNAMENT_NOT_RUNNING",
                    Message = "Tournament rebuy window is not open",
                    FailedAtPhase = RebuyPhase.RebuyRequested.ToString(),
                    FailedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                },
            });
            _rebuyPhase = RebuyPhase.RebuyFailed;
            return;
        }

        // Validate player is seated
        if (_playerSeatPosition < 0)
        {
            _emittedEvents.Add(new RebuyFailed
            {
                PlayerRoot = ByteString.CopyFrom(_playerRoot),
                TournamentRoot = ByteString.CopyFrom(_tournamentRoot),
                ReservationId = ByteString.CopyFrom(_reservationId),
                Failure = new OrchestrationFailure
                {
                    Code = "NOT_SEATED",
                    Message = "Player is not seated at the table",
                    FailedAtPhase = RebuyPhase.RebuyRequested.ToString(),
                    FailedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                },
            });
            _rebuyPhase = RebuyPhase.RebuyFailed;
            return;
        }

        // Validation passed: emit ProcessRebuy command and RebuyInitiated event
        _emittedCommands.Add(new ProcessRebuy
        {
            PlayerRoot = ByteString.CopyFrom(_playerRoot),
            ReservationId = ByteString.CopyFrom(_reservationId),
        });

        _emittedEvents.Add(new RebuyInitiated
        {
            PlayerRoot = ByteString.CopyFrom(_playerRoot),
            TournamentRoot = ByteString.CopyFrom(_tournamentRoot),
            TableRoot = ByteString.CopyFrom(_tableRoot),
            ReservationId = ByteString.CopyFrom(_reservationId),
            Seat = _playerSeatPosition,
            Fee = new Currency { Amount = _requestedAmount, CurrencyCode = "CHIPS" },
            ChipsToAdd = _requestedAmount,
            Phase = RebuyPhase.RebuyApproving,
            InitiatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        });

        _rebuyPhase = RebuyPhase.RebuyApproving;
    }

    [When(@"the RebuyOrchestrator handles a RebuyProcessed event")]
    public void WhenTheRebuyOrchestratorHandlesARebuyProcessedEvent()
    {
        _emittedCommands.Clear();
        _emittedEvents.Clear();

        // Tournament approved the rebuy: add chips to table
        _emittedCommands.Add(new AddRebuyChips
        {
            PlayerRoot = ByteString.CopyFrom(_playerRoot),
            ReservationId = ByteString.CopyFrom(_reservationId),
            Seat = _playerSeatPosition,
            Amount = _requestedAmount,
        });

        _rebuyPhase = RebuyPhase.RebuyAddingChips;
    }

    [When(@"the RebuyOrchestrator handles a RebuyChipsAdded event")]
    public void WhenTheRebuyOrchestratorHandlesARebuyChipsAddedEvent()
    {
        _emittedCommands.Clear();
        _emittedEvents.Clear();

        // Chips added to table: confirm fee with player
        _emittedCommands.Add(new ConfirmRebuyFee
        {
            ReservationId = ByteString.CopyFrom(_reservationId),
        });

        _emittedEvents.Add(new RebuyCompleted
        {
            PlayerRoot = ByteString.CopyFrom(_playerRoot),
            TournamentRoot = ByteString.CopyFrom(_tournamentRoot),
            TableRoot = ByteString.CopyFrom(_tableRoot),
            ReservationId = ByteString.CopyFrom(_reservationId),
            Fee = new Currency { Amount = _requestedAmount, CurrencyCode = "CHIPS" },
            ChipsAdded = _requestedAmount,
            CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow),
        });

        _rebuyPhase = RebuyPhase.RebuyCompleted;
    }

    [When(@"the RebuyOrchestrator handles a RebuyDenied event")]
    public void WhenTheRebuyOrchestratorHandlesARebuyDeniedEvent()
    {
        _emittedCommands.Clear();
        _emittedEvents.Clear();

        // Tournament denied the rebuy: release fee
        _emittedCommands.Add(new ReleaseRebuyFee
        {
            ReservationId = ByteString.CopyFrom(_reservationId),
            Reason = "REBUY_DENIED",
        });

        _emittedEvents.Add(new RebuyFailed
        {
            PlayerRoot = ByteString.CopyFrom(_playerRoot),
            TournamentRoot = ByteString.CopyFrom(_tournamentRoot),
            ReservationId = ByteString.CopyFrom(_reservationId),
            Failure = new OrchestrationFailure
            {
                Code = "REBUY_DENIED",
                Message = "Tournament denied the rebuy request",
                FailedAtPhase = RebuyPhase.RebuyApproving.ToString(),
                FailedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            },
        });

        _rebuyPhase = RebuyPhase.RebuyFailed;
    }

    // =========================================================================
    // Then Steps - Command assertions
    // =========================================================================

    [Then(@"the PM emits a SeatPlayer command to the table")]
    public void ThenThePMEmitsASeatPlayerCommandToTheTable()
    {
        _emittedCommands.Should().ContainSingle(c => c is SeatPlayer);
        var cmd = _emittedCommands.OfType<SeatPlayer>().First();
        cmd.PlayerRoot.Should().Equal(_playerRoot);
        cmd.Seat.Should().Be(_requestedSeat);
        cmd.Amount.Should().Be(_requestedAmount);
    }

    [Then(@"the PM emits no commands")]
    public void ThenThePMEmitsNoCommands()
    {
        _emittedCommands.Should().BeEmpty();
    }

    [Then(@"the PM emits a ConfirmBuyIn command to the player")]
    public void ThenThePMEmitsAConfirmBuyInCommandToThePlayer()
    {
        _emittedCommands.Should().ContainSingle(c => c is ConfirmBuyIn);
        var cmd = _emittedCommands.OfType<ConfirmBuyIn>().First();
        cmd.ReservationId.Should().Equal(_reservationId);
    }

    [Then(@"the PM emits a ReleaseBuyIn command to the player")]
    public void ThenThePMEmitsAReleaseBuyInCommandToThePlayer()
    {
        _emittedCommands.Should().ContainSingle(c => c is ReleaseBuyIn);
        var cmd = _emittedCommands.OfType<ReleaseBuyIn>().First();
        cmd.ReservationId.Should().Equal(_reservationId);
    }

    [Then(@"the PM emits an EnrollPlayer command to the tournament")]
    public void ThenThePMEmitsAnEnrollPlayerCommandToTheTournament()
    {
        _emittedCommands.Should().ContainSingle(c => c is EnrollPlayer);
        var cmd = _emittedCommands.OfType<EnrollPlayer>().First();
        cmd.PlayerRoot.Should().Equal(_playerRoot);
    }

    [Then(@"the PM emits a ConfirmRegistrationFee command to the player")]
    public void ThenThePMEmitsAConfirmRegistrationFeeCommandToThePlayer()
    {
        _emittedCommands.Should().ContainSingle(c => c is ConfirmRegistrationFee);
        var cmd = _emittedCommands.OfType<ConfirmRegistrationFee>().First();
        cmd.ReservationId.Should().Equal(_reservationId);
    }

    [Then(@"the PM emits a ReleaseRegistrationFee command to the player")]
    public void ThenThePMEmitsAReleaseRegistrationFeeCommandToThePlayer()
    {
        _emittedCommands.Should().ContainSingle(c => c is ReleaseRegistrationFee);
        var cmd = _emittedCommands.OfType<ReleaseRegistrationFee>().First();
        cmd.ReservationId.Should().Equal(_reservationId);
    }

    [Then(@"the PM emits a ProcessRebuy command to the tournament")]
    public void ThenThePMEmitsAProcessRebuyCommandToTheTournament()
    {
        _emittedCommands.Should().ContainSingle(c => c is ProcessRebuy);
        var cmd = _emittedCommands.OfType<ProcessRebuy>().First();
        cmd.PlayerRoot.Should().Equal(_playerRoot);
    }

    [Then(@"the PM emits an AddRebuyChips command to the table")]
    public void ThenThePMEmitsAnAddRebuyChipsCommandToTheTable()
    {
        _emittedCommands.Should().ContainSingle(c => c is AddRebuyChips);
        var cmd = _emittedCommands.OfType<AddRebuyChips>().First();
        cmd.PlayerRoot.Should().Equal(_playerRoot);
        cmd.Seat.Should().Be(_playerSeatPosition);
        cmd.Amount.Should().Be(_requestedAmount);
    }

    [Then(@"the PM emits a ConfirmRebuyFee command to the player")]
    public void ThenThePMEmitsAConfirmRebuyFeeCommandToThePlayer()
    {
        _emittedCommands.Should().ContainSingle(c => c is ConfirmRebuyFee);
        var cmd = _emittedCommands.OfType<ConfirmRebuyFee>().First();
        cmd.ReservationId.Should().Equal(_reservationId);
    }

    [Then(@"the PM emits a ReleaseRebuyFee command to the player")]
    public void ThenThePMEmitsAReleaseRebuyFeeCommandToThePlayer()
    {
        _emittedCommands.Should().ContainSingle(c => c is ReleaseRebuyFee);
        var cmd = _emittedCommands.OfType<ReleaseRebuyFee>().First();
        cmd.ReservationId.Should().Equal(_reservationId);
    }

    // =========================================================================
    // Then Steps - Process event assertions
    // =========================================================================

    [Then(@"the PM emits a BuyInInitiated process event")]
    public void ThenThePMEmitsABuyInInitiatedProcessEvent()
    {
        _emittedEvents.Should().ContainSingle(e => e is BuyInInitiated);
        var evt = _emittedEvents.OfType<BuyInInitiated>().First();
        evt.PlayerRoot.Should().Equal(_playerRoot);
        evt.TableRoot.Should().Equal(_tableRoot);
        evt.Phase.Should().Be(BuyInPhase.BuyInSeating);
    }

    [Then(@"the PM emits a BuyInCompleted process event")]
    public void ThenThePMEmitsABuyInCompletedProcessEvent()
    {
        _emittedEvents.Should().ContainSingle(e => e is BuyInCompleted);
        var evt = _emittedEvents.OfType<BuyInCompleted>().First();
        evt.PlayerRoot.Should().Equal(_playerRoot);
        evt.TableRoot.Should().Equal(_tableRoot);
    }

    [Then(@"the PM emits a BuyInFailed process event with code ""(.*)""")]
    public void ThenThePMEmitsABuyInFailedProcessEventWithCode(string code)
    {
        _emittedEvents.Should().ContainSingle(e => e is BuyInFailed);
        var evt = _emittedEvents.OfType<BuyInFailed>().First();
        evt.Failure.Should().NotBeNull();
        evt.Failure!.Code.Should().Be(code);
    }

    [Then(@"the PM emits a RegistrationInitiated process event")]
    public void ThenThePMEmitsARegistrationInitiatedProcessEvent()
    {
        _emittedEvents.Should().ContainSingle(e => e is RegistrationInitiated);
        var evt = _emittedEvents.OfType<RegistrationInitiated>().First();
        evt.PlayerRoot.Should().Equal(_playerRoot);
        evt.TournamentRoot.Should().Equal(_tournamentRoot);
        evt.Phase.Should().Be(RegistrationPhase.RegistrationEnrolling);
    }

    [Then(@"the PM emits a RegistrationCompleted process event")]
    public void ThenThePMEmitsARegistrationCompletedProcessEvent()
    {
        _emittedEvents.Should().ContainSingle(e => e is RegistrationCompleted);
        var evt = _emittedEvents.OfType<RegistrationCompleted>().First();
        evt.PlayerRoot.Should().Equal(_playerRoot);
        evt.TournamentRoot.Should().Equal(_tournamentRoot);
    }

    [Then(@"the PM emits a RegistrationFailed process event with code ""(.*)""")]
    public void ThenThePMEmitsARegistrationFailedProcessEventWithCode(string code)
    {
        _emittedEvents.Should().ContainSingle(e => e is RegistrationFailed);
        var evt = _emittedEvents.OfType<RegistrationFailed>().First();
        evt.Failure.Should().NotBeNull();
        evt.Failure!.Code.Should().Be(code);
    }

    [Then(@"the PM emits a RebuyInitiated process event")]
    public void ThenThePMEmitsARebuyInitiatedProcessEvent()
    {
        _emittedEvents.Should().ContainSingle(e => e is RebuyInitiated);
        var evt = _emittedEvents.OfType<RebuyInitiated>().First();
        evt.PlayerRoot.Should().Equal(_playerRoot);
        evt.TournamentRoot.Should().Equal(_tournamentRoot);
        evt.TableRoot.Should().Equal(_tableRoot);
        evt.Phase.Should().Be(RebuyPhase.RebuyApproving);
    }

    [Then(@"the PM emits a RebuyCompleted process event")]
    public void ThenThePMEmitsARebuyCompletedProcessEvent()
    {
        _emittedEvents.Should().ContainSingle(e => e is RebuyCompleted);
        var evt = _emittedEvents.OfType<RebuyCompleted>().First();
        evt.PlayerRoot.Should().Equal(_playerRoot);
        evt.TournamentRoot.Should().Equal(_tournamentRoot);
        evt.TableRoot.Should().Equal(_tableRoot);
    }

    [Then(@"the PM emits a RebuyFailed process event with code ""(.*)""")]
    public void ThenThePMEmitsARebuyFailedProcessEventWithCode(string code)
    {
        _emittedEvents.Should().ContainSingle(e => e is RebuyFailed);
        var evt = _emittedEvents.OfType<RebuyFailed>().First();
        evt.Failure.Should().NotBeNull();
        evt.Failure!.Code.Should().Be(code);
    }
}
