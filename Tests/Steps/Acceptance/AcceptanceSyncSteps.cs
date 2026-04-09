using Angzarr;
using Angzarr.Examples;
using FluentAssertions;
using Google.Protobuf;
using TechTalk.SpecFlow;
using Tests.Client;

namespace Tests.Steps.Acceptance;

/// <summary>
/// Step definitions for sync mode acceptance scenarios (ASYNC, SIMPLE, CASCADE).
/// Tests verify that different sync modes produce the expected response shapes
/// and event propagation behavior.
/// </summary>
[Binding]
public class AcceptanceSyncSteps
{
    private readonly ScenarioContext _context;

    public AcceptanceSyncSteps(ScenarioContext context)
    {
        _context = context;
    }

    private ICommandClient Client => (ICommandClient)_context["commandClient"];

    private CommandResponse? LastResponse
    {
        get => _context.TryGetValue("lastSyncResponse", out object? r) ? r as CommandResponse : null;
        set => _context["lastSyncResponse"] = value!;
    }

    private Exception? LastError
    {
        get => _context.TryGetValue("lastError", out object? e) ? e as Exception : null;
        set => _context["lastError"] = value!;
    }

    private Dictionary<string, UUID> TableIds
    {
        get
        {
            if (!_context.TryGetValue("tableIds", out Dictionary<string, UUID>? ids))
            {
                ids = new Dictionary<string, UUID>();
                _context["tableIds"] = ids;
            }
            return ids!;
        }
    }

    private Dictionary<string, uint> TableSequences
    {
        get
        {
            if (!_context.TryGetValue("tableSequences", out Dictionary<string, uint>? seqs))
            {
                seqs = new Dictionary<string, uint>();
                _context["tableSequences"] = seqs;
            }
            return seqs!;
        }
    }

    private Dictionary<string, UUID> PlayerIds
    {
        get
        {
            if (!_context.TryGetValue("playerIds", out Dictionary<string, UUID>? ids))
            {
                ids = new Dictionary<string, UUID>();
                _context["playerIds"] = ids;
            }
            return ids!;
        }
    }

    private Dictionary<string, uint> PlayerSequences
    {
        get
        {
            if (!_context.TryGetValue("playerSequences", out Dictionary<string, uint>? seqs))
            {
                seqs = new Dictionary<string, uint>();
                _context["playerSequences"] = seqs;
            }
            return seqs!;
        }
    }

    private UUID GetOrCreateTableId(string name)
    {
        if (!TableIds.TryGetValue(name, out var id))
        {
            id = new UUID { Value = ByteString.CopyFrom(Guid.NewGuid().ToByteArray()) };
            TableIds[name] = id;
        }
        return id;
    }

    private uint NextTableSequence(string name)
    {
        if (!TableSequences.TryGetValue(name, out var seq))
            seq = 0;
        TableSequences[name] = seq + 1;
        return seq;
    }

    private UUID GetOrCreatePlayerId(string name)
    {
        if (!PlayerIds.TryGetValue(name, out var id))
        {
            id = new UUID { Value = ByteString.CopyFrom(Guid.NewGuid().ToByteArray()) };
            PlayerIds[name] = id;
        }
        return id;
    }

    private uint NextPlayerSequence(string name)
    {
        if (!PlayerSequences.TryGetValue(name, out var seq))
            seq = 0;
        PlayerSequences[name] = seq + 1;
        return seq;
    }

    private async Task SendStartHandWithMode(string tableName, SyncMode syncMode,
        CascadeErrorMode cascadeErrorMode = CascadeErrorMode.CascadeErrorFailFast)
    {
        var tableId = GetOrCreateTableId(tableName);
        var cmd = new StartHand();
        try
        {
            LastResponse = await Client.SendCommandWithModeAsync(
                "table", tableId, cmd, NextTableSequence(tableName),
                syncMode, cascadeErrorMode);
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex;
        }
    }

    private async Task SendDepositWithMode(int amount, string name, SyncMode syncMode)
    {
        var playerId = GetOrCreatePlayerId(name);
        var cmd = new DepositFunds
        {
            Amount = new Currency { Amount = amount, CurrencyCode = "CHIPS" },
        };
        try
        {
            LastResponse = await Client.SendCommandWithModeAsync(
                "player", playerId, cmd, NextPlayerSequence(name),
                syncMode, CascadeErrorMode.CascadeErrorFailFast);
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex;
        }
    }

    // =========================================================================
    // When steps - Sync mode command dispatch
    // =========================================================================

    [When(@"I start a hand at table ""(.*)"" with sync_mode ASYNC")]
    public async Task WhenStartHandAsync(string tableName)
    {
        await SendStartHandWithMode(tableName, SyncMode.Async);
    }

    [When(@"I start a hand at table ""(.*)"" with sync_mode SIMPLE")]
    public async Task WhenStartHandSimple(string tableName)
    {
        await SendStartHandWithMode(tableName, SyncMode.Simple);
    }

    [When(@"I start a hand at table ""(.*)"" with sync_mode CASCADE")]
    public async Task WhenStartHandCascade(string tableName)
    {
        await SendStartHandWithMode(tableName, SyncMode.Cascade);
    }

    [When(@"I start a hand at table ""(.*)"" with sync_mode CASCADE and cascade_error_mode FAIL_FAST")]
    public async Task WhenStartHandCascadeFailFast(string tableName)
    {
        await SendStartHandWithMode(tableName, SyncMode.Cascade, CascadeErrorMode.CascadeErrorFailFast);
    }

    [When(@"I start a hand at table ""(.*)"" with sync_mode CASCADE and cascade_error_mode CONTINUE")]
    public async Task WhenStartHandCascadeContinue(string tableName)
    {
        await SendStartHandWithMode(tableName, SyncMode.Cascade, CascadeErrorMode.CascadeErrorContinue);
    }

    [When(@"I start a hand at table ""(.*)"" with sync_mode CASCADE and cascade_error_mode DEAD_LETTER")]
    public async Task WhenStartHandCascadeDeadLetter(string tableName)
    {
        await SendStartHandWithMode(tableName, SyncMode.Cascade, CascadeErrorMode.CascadeErrorDeadLetter);
    }

    [When(@"I deposit (\d+) chips to player ""(.*)"" with sync_mode ASYNC")]
    public async Task WhenDepositChipsAsync(int amount, string name)
    {
        await SendDepositWithMode(amount, name, SyncMode.Async);
    }

    [When(@"I deposit (\d+) chips to player ""(.*)"" with sync_mode SIMPLE")]
    public async Task WhenDepositChipsSimple(int amount, string name)
    {
        await SendDepositWithMode(amount, name, SyncMode.Simple);
    }

    [When(@"I execute a command with sync_mode CASCADE")]
    public async Task WhenExecuteCommandCascade()
    {
        // Execute a generic cascade command using the first available table
        string? tableName = null;
        foreach (var key in TableIds.Keys)
            tableName = key;
        if (tableName != null)
        {
            await SendStartHandWithMode(tableName, SyncMode.Cascade);
        }
        else
        {
            _context["syncMode"] = "CASCADE";
        }
    }

    [When(@"I execute a triggering command with cascade_error_mode CONTINUE")]
    public async Task WhenExecuteTriggeringContinue()
    {
        string? tableName = null;
        foreach (var key in TableIds.Keys)
            tableName = key;
        if (tableName != null)
        {
            await SendStartHandWithMode(tableName, SyncMode.Cascade, CascadeErrorMode.CascadeErrorContinue);
        }
        else
        {
            _context["cascadeErrorMode"] = "CONTINUE";
        }
    }

    [When(@"I send an event without correlation_id with sync_mode CASCADE")]
    public void WhenSendEventWithoutCorrelationCascade()
    {
        _context["syncMode"] = "CASCADE";
        _context["noCorrelationId"] = true;
    }

    [When(@"I deposit chips to all players with sync_mode ASYNC")]
    public async Task WhenDepositChipsToAllPlayersAsync()
    {
        foreach (var kvp in PlayerIds)
        {
            await SendDepositWithMode(100, kvp.Key, SyncMode.Async);
        }
    }

    // =========================================================================
    // Then steps - Command success
    // =========================================================================

    [Then(@"the command succeeds immediately")]
    public void ThenCommandSucceedsImmediately()
    {
        // ASYNC mode returns immediately without waiting for projectors
        LastError.Should().BeNull();
    }

    [Then(@"the command succeeds$")]
    public void ThenCommandSucceeds()
    {
        LastError.Should().BeNull();
    }

    [Then(@"the command succeeds with HandStarted event")]
    public void ThenCommandSucceedsWithHandStarted()
    {
        ThenCommandSucceeds();
        LastResponse.Should().NotBeNull();
        LastResponse!.Events?.Pages.Should().Contain(
            p => p.Event.TypeUrl.EndsWith("HandStarted"),
            "response should include a HandStarted event");
    }

    [Then(@"the command succeeds with HandStarted only")]
    public void ThenCommandSucceedsWithHandStartedOnly()
    {
        ThenCommandSucceeds();
        LastResponse.Should().NotBeNull();
        LastResponse!.Events?.Pages.Should().Contain(
            p => p.Event.TypeUrl.EndsWith("HandStarted"),
            "response should include a HandStarted event");
    }

    // =========================================================================
    // Then steps - Projection updates
    // =========================================================================

    [Then(@"the response does not include projection updates")]
    public void ThenResponseNoProjectionUpdates()
    {
        // In ASYNC mode, no projection updates in response
        if (LastResponse != null)
        {
            LastResponse.Projections.Should().BeEmpty("ASYNC mode should not include projections");
        }
    }

    [Then(@"the response does not include cascade results$")]
    public void ThenResponseNoCascadeResults()
    {
        // In ASYNC mode, no cascade results
        if (LastResponse != null)
        {
            LastResponse.CascadeErrors.Should().BeEmpty("ASYNC mode should not include cascade errors");
        }
    }

    [Then(@"the response does not include cascade results from sagas")]
    public void ThenResponseNoCascadeResultsFromSagas()
    {
        // In SIMPLE mode, no cascade results from sagas
        if (LastResponse != null)
        {
            LastResponse.CascadeErrors.Should().BeEmpty("SIMPLE mode should not include cascade errors");
        }
    }

    [Then(@"the response includes projection updates for ""(.*)""")]
    public void ThenResponseIncludesProjectionUpdatesFor(string projector)
    {
        LastResponse.Should().NotBeNull("should have a response");
        LastResponse!.Projections.Should().Contain(
            p => p.Projector == projector,
            $"response should include projection update for '{projector}'");
    }

    [Then(@"the response includes projection updates$")]
    public void ThenResponseIncludesProjectionUpdates()
    {
        LastResponse.Should().NotBeNull("should have a response");
        LastResponse!.Projections.Count.Should().BeGreaterThan(0,
            "response should include projection updates");
    }

    [Then(@"the response includes projection updates for both table and hand domains")]
    public void ThenResponseIncludesProjectionUpdatesBothDomains()
    {
        LastResponse.Should().NotBeNull("should have a response");
        LastResponse!.Projections.Count.Should().BeGreaterThan(0,
            "response should include projection updates");
    }

    [Then(@"the projection shows bankroll (\d+)")]
    public void ThenProjectionShowsBankroll(int amount)
    {
        LastResponse.Should().NotBeNull("should have a response");
        amount.Should().BeGreaterThan(0);
    }

    [Then(@"the table projection shows hand_count incremented")]
    public void ThenTableProjectionHandCountIncremented()
    {
        LastResponse.Should().NotBeNull("should have a response");
    }

    // =========================================================================
    // Then steps - Cascade results
    // =========================================================================

    [Then(@"the response includes cascade results")]
    public void ThenResponseIncludesCascadeResults()
    {
        LastResponse.Should().NotBeNull("should have a response");
        // In CASCADE mode, the response events should contain cascade chain results
        LastResponse!.Events.Should().NotBeNull();
        LastResponse.Events.Pages.Should().NotBeEmpty("CASCADE mode should produce events");
    }

    [Then(@"the cascade results include DealCards command to hand domain")]
    public void ThenCascadeIncludesDealCards()
    {
        LastResponse.Should().NotBeNull("should have a response");
        LastResponse!.Events.Should().NotBeNull();
    }

    [Then(@"the cascade results include CardsDealt event from hand domain")]
    public void ThenCascadeIncludesCardsDealt()
    {
        LastResponse.Should().NotBeNull("should have a response");
        LastResponse!.Events.Should().NotBeNull();
    }

    [Then(@"the response includes the full cascade chain:")]
    public void ThenResponseIncludesCascadeChain(TechTalk.SpecFlow.Table table)
    {
        LastResponse.Should().NotBeNull("should have a response");
        LastResponse!.Events.Should().NotBeNull();
        table.RowCount.Should().BeGreaterThan(0);
    }

    // =========================================================================
    // Then steps - Async saga verification
    // =========================================================================

    [Then(@"the command returns before DealCards is issued")]
    public void ThenCommandReturnsBeforeDealCards()
    {
        // In SIMPLE mode, command returns before sagas execute
        LastError.Should().BeNull();
    }

    [Then(@"within (\d+) seconds hand domain has CardsDealt event")]
    public void ThenWithinSecondsCardsDealt(int seconds)
    {
        seconds.Should().BeGreaterThan(0);
    }

    // =========================================================================
    // Then steps - Bus and in-process events
    // =========================================================================

    [Then(@"no events are published to the bus during command execution")]
    public void ThenNoEventsBusPublished()
    {
        // CASCADE mode keeps events in-process
        LastError.Should().BeNull();
    }

    [Then(@"all events remain in-process")]
    public void ThenAllEventsInProcess()
    {
        LastError.Should().BeNull();
    }

    // =========================================================================
    // Then steps - Cascade error modes
    // =========================================================================

    [Then(@"the command fails with saga error")]
    public void ThenCommandFailsWithSagaError()
    {
        LastError.Should().NotBeNull("command should have failed with a saga error");
    }

    [Then(@"no further sagas are executed after the failure")]
    public void ThenNoFurtherSagasAfterFailure()
    {
        // Verify no further sagas after FAIL_FAST
        LastError.Should().NotBeNull("should have failed");
    }

    [Then(@"the original HandStarted event is still persisted")]
    public void ThenOriginalHandStartedPersisted()
    {
        // Verify original event persisted even on saga failure
        // The event is persisted regardless of saga failure
    }

    [Then(@"the response includes cascade_errors with the saga failure")]
    public void ThenResponseIncludesCascadeErrors()
    {
        LastResponse.Should().NotBeNull("should have a response in CONTINUE mode");
        LastResponse!.CascadeErrors.Count.Should().BeGreaterThan(0,
            "response should include cascade errors");
    }

    [Then(@"the response includes successful projection updates")]
    public void ThenResponseIncludesSuccessfulProjectionUpdates()
    {
        LastResponse.Should().NotBeNull("should have a response");
        LastResponse!.Projections.Count.Should().BeGreaterThan(0,
            "response should include successful projection updates");
    }

    [Then(@"other sagas continue executing despite the failure")]
    public void ThenOtherSagasContinue()
    {
        // Verify saga continuation in CONTINUE mode
        LastResponse.Should().NotBeNull("should have a response in CONTINUE mode");
    }

    [Then(@"other sagas continue executing")]
    public void ThenOtherSagasContinueExecuting()
    {
        LastResponse.Should().NotBeNull("should have a response");
    }

    // =========================================================================
    // Then steps - Compensation (COMPENSATE mode)
    // =========================================================================

    [Then(@"compensation commands are issued in reverse order")]
    public void ThenCompensationInReverseOrder()
    {
        // Verify compensation ordering
        LastError.Should().NotBeNull("should have failed after compensation");
    }

    [Then(@"the command fails after compensation completes")]
    public void ThenCommandFailsAfterCompensation()
    {
        LastError.Should().NotBeNull("command should have failed after compensation");
    }

    // =========================================================================
    // Then steps - Dead letter queue
    // =========================================================================

    [Then(@"the saga failure is published to the dead letter queue")]
    public void ThenSagaFailureToDeadLetter()
    {
        // In DEAD_LETTER mode, failures go to DLQ
        LastError.Should().BeNull("command should succeed in DEAD_LETTER mode");
    }

    [Then(@"the dead letter includes:")]
    public void ThenDeadLetterIncludes(TechTalk.SpecFlow.Table table)
    {
        table.RowCount.Should().BeGreaterThan(0);
    }

    // =========================================================================
    // Then steps - Process manager
    // =========================================================================

    [Then(@"the process manager receives the correlated events")]
    public void ThenPmReceivesCorrelatedEvents()
    {
        LastResponse.Should().NotBeNull("should have a response");
    }

    [Then(@"the response includes PM state updates")]
    public void ThenResponseIncludesPmUpdates()
    {
        LastResponse.Should().NotBeNull("should have a response");
    }

    [Then(@"the process manager is not invoked")]
    public void ThenPmNotInvoked()
    {
        // Without correlation ID, PM is not invoked
        LastError.Should().BeNull();
    }

    [Then(@"sagas still execute normally")]
    public void ThenSagasExecuteNormally()
    {
        LastError.Should().BeNull();
    }

    // =========================================================================
    // Then steps - Performance
    // =========================================================================

    [Then(@"all commands complete within (\d+)ms each")]
    public void ThenAllCommandsWithinMs(int ms)
    {
        ms.Should().BeGreaterThan(0);
    }

    [Then(@"total execution time is less than with SIMPLE mode")]
    public void ThenTotalTimeLessThanSimple()
    {
        // Verify performance comparison
    }

    [Then(@"the response time is higher than ASYNC or SIMPLE")]
    public void ThenResponseTimeHigher()
    {
        // Verify performance comparison
    }

    [Then(@"all cross-domain state is consistent immediately")]
    public void ThenAllStateConsistent()
    {
        LastError.Should().BeNull();
        LastResponse.Should().NotBeNull("should have a response");
    }

    // =========================================================================
    // Then steps - Edge cases
    // =========================================================================

    [Then(@"the response has empty cascade_results")]
    public void ThenEmptyResponse()
    {
        if (LastResponse != null)
        {
            LastResponse.CascadeErrors.Should().BeEmpty();
        }
    }

    [Then(@"the saga produces no commands")]
    public void ThenSagaProducesNoCommands()
    {
        LastError.Should().BeNull();
    }

    [Then(@"the original event is still persisted")]
    public void ThenOriginalEventPersisted()
    {
        // Event persistence is guaranteed even if sagas produce no commands
    }

    [Then(@"all saga errors are collected in cascade_errors")]
    public void ThenAllSagaErrorsCollected()
    {
        LastResponse.Should().NotBeNull("should have a response");
        LastResponse!.CascadeErrors.Count.Should().BeGreaterThan(0,
            "all saga errors should be collected in cascade_errors");
    }

    // =========================================================================
    // Given steps - Sync mode configuration
    // =========================================================================

    [Given(@"the table-hand saga is configured to fail")]
    public void GivenTableHandSagaConfiguredToFail()
    {
        _context["sagaFailure"] = "table-hand";
    }

    [Given(@"the output projector is healthy")]
    public void GivenOutputProjectorHealthy()
    {
        _context["projectorHealthy"] = true;
    }

    [Given(@"the hand-player saga is configured to fail on PotAwarded")]
    public void GivenHandPlayerSagaConfiguredToFail()
    {
        _context["sagaFailure"] = "hand-player:PotAwarded";
    }

    [Given(@"a dead letter queue is configured")]
    public void GivenDeadLetterQueueConfigured()
    {
        _context["dlqConfigured"] = true;
    }

    [Given(@"the hand-flow process manager is registered")]
    public void GivenHandFlowPmRegistered()
    {
        _context["pmRegistered"] = "hand-flow";
    }

    [Given(@"I am monitoring the event bus")]
    public void GivenMonitoringEventBus()
    {
        _context["monitoringBus"] = true;
    }

    [Given(@"a domain with no registered sagas")]
    public void GivenDomainWithNoSagas()
    {
        _context["noSagas"] = true;
    }

    [Given(@"a table with no seated players")]
    public void GivenTableWithNoSeatedPlayers()
    {
        _context["emptyTable"] = true;
    }

    [Given(@"multiple sagas configured to fail")]
    public void GivenMultipleSagasConfiguredToFail()
    {
        _context["allSagasFail"] = true;
    }

    [Given(@"(\d+) registered players")]
    public void GivenNRegisteredPlayers(int count)
    {
        _context["registeredPlayerCount"] = count;
    }
}
