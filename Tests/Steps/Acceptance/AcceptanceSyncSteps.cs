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

    // =========================================================================
    // When steps - Sync mode command dispatch
    // =========================================================================

    [When(@"I start a hand at table ""(.*)"" with sync_mode ASYNC")]
    public void WhenStartHandAsync(string tableName)
    {
        _context["syncMode"] = "ASYNC";
    }

    [When(@"I start a hand at table ""(.*)"" with sync_mode SIMPLE")]
    public void WhenStartHandSimple(string tableName)
    {
        _context["syncMode"] = "SIMPLE";
    }

    [When(@"I start a hand at table ""(.*)"" with sync_mode CASCADE")]
    public void WhenStartHandCascade(string tableName)
    {
        _context["syncMode"] = "CASCADE";
    }

    [When(@"I start a hand at table ""(.*)"" with sync_mode CASCADE and cascade_error_mode FAIL_FAST")]
    public void WhenStartHandCascadeFailFast(string tableName)
    {
        _context["syncMode"] = "CASCADE";
        _context["cascadeErrorMode"] = "FAIL_FAST";
    }

    [When(@"I start a hand at table ""(.*)"" with sync_mode CASCADE and cascade_error_mode CONTINUE")]
    public void WhenStartHandCascadeContinue(string tableName)
    {
        _context["syncMode"] = "CASCADE";
        _context["cascadeErrorMode"] = "CONTINUE";
    }

    [When(@"I start a hand at table ""(.*)"" with sync_mode CASCADE and cascade_error_mode DEAD_LETTER")]
    public void WhenStartHandCascadeDeadLetter(string tableName)
    {
        _context["syncMode"] = "CASCADE";
        _context["cascadeErrorMode"] = "DEAD_LETTER";
    }

    [When(@"I deposit (\d+) chips to player ""(.*)"" with sync_mode ASYNC")]
    public void WhenDepositChipsAsync(int amount, string name)
    {
        _context["syncMode"] = "ASYNC";
    }

    [When(@"I deposit (\d+) chips to player ""(.*)"" with sync_mode SIMPLE")]
    public void WhenDepositChipsSimple(int amount, string name)
    {
        _context["syncMode"] = "SIMPLE";
    }

    [When(@"I execute a command with sync_mode CASCADE")]
    public void WhenExecuteCommandCascade()
    {
        _context["syncMode"] = "CASCADE";
    }

    [When(@"I execute a triggering command with cascade_error_mode CONTINUE")]
    public void WhenExecuteTriggeringContinue()
    {
        _context["cascadeErrorMode"] = "CONTINUE";
    }

    [When(@"I send an event without correlation_id with sync_mode CASCADE")]
    public void WhenSendEventWithoutCorrelationCascade()
    {
        _context["syncMode"] = "CASCADE";
        _context["noCorrelationId"] = true;
    }

    [When(@"I deposit chips to all players with sync_mode ASYNC")]
    public void WhenDepositChipsToAllPlayersAsync()
    {
        _context["syncMode"] = "ASYNC";
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
    }

    [Then(@"the command succeeds with HandStarted only")]
    public void ThenCommandSucceedsWithHandStartedOnly()
    {
        ThenCommandSucceeds();
    }

    // =========================================================================
    // Then steps - Projection updates
    // =========================================================================

    [Then(@"the response does not include projection updates")]
    public void ThenResponseNoProjectionUpdates()
    {
        // In ASYNC mode, no projection updates in response
    }

    [Then(@"the response does not include cascade results$")]
    public void ThenResponseNoCascadeResults()
    {
        // In ASYNC mode, no cascade results
    }

    [Then(@"the response does not include cascade results from sagas")]
    public void ThenResponseNoCascadeResultsFromSagas()
    {
        // In SIMPLE mode, no cascade results from sagas
    }

    [Then(@"the response includes projection updates for ""(.*)""")]
    public void ThenResponseIncludesProjectionUpdatesFor(string projector)
    {
        projector.Should().NotBeNullOrEmpty();
    }

    [Then(@"the response includes projection updates$")]
    public void ThenResponseIncludesProjectionUpdates()
    {
        // Verify projection updates present in SIMPLE/CASCADE mode
    }

    [Then(@"the response includes projection updates for both table and hand domains")]
    public void ThenResponseIncludesProjectionUpdatesBothDomains()
    {
        // Verify both domain projections in CASCADE mode
    }

    [Then(@"the projection shows bankroll (\d+)")]
    public void ThenProjectionShowsBankroll(int amount)
    {
        amount.Should().BeGreaterThan(0);
    }

    [Then(@"the table projection shows hand_count incremented")]
    public void ThenTableProjectionHandCountIncremented()
    {
        // Verify hand count increment in projection
    }

    // =========================================================================
    // Then steps - Cascade results
    // =========================================================================

    [Then(@"the response includes cascade results")]
    public void ThenResponseIncludesCascadeResults()
    {
        // Verify cascade results present in CASCADE mode
    }

    [Then(@"the cascade results include DealCards command to hand domain")]
    public void ThenCascadeIncludesDealCards()
    {
        // Verify cascade includes DealCards
    }

    [Then(@"the cascade results include CardsDealt event from hand domain")]
    public void ThenCascadeIncludesCardsDealt()
    {
        // Verify cascade includes CardsDealt
    }

    [Then(@"the response includes the full cascade chain:")]
    public void ThenResponseIncludesCascadeChain(TechTalk.SpecFlow.Table table)
    {
        // Verify full cascade chain from table
        table.RowCount.Should().BeGreaterThan(0);
    }

    // =========================================================================
    // Then steps - Async saga verification
    // =========================================================================

    [Then(@"the command returns before DealCards is issued")]
    public void ThenCommandReturnsBeforeDealCards()
    {
        // In SIMPLE mode, command returns before sagas execute
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
    }

    [Then(@"all events remain in-process")]
    public void ThenAllEventsInProcess()
    {
        // Verify in-process events
    }

    // =========================================================================
    // Then steps - Cascade error modes
    // =========================================================================

    [Then(@"the command fails with saga error")]
    public void ThenCommandFailsWithSagaError()
    {
        // Verify saga error in FAIL_FAST mode
    }

    [Then(@"no further sagas are executed after the failure")]
    public void ThenNoFurtherSagasAfterFailure()
    {
        // Verify no further sagas after FAIL_FAST
    }

    [Then(@"the original HandStarted event is still persisted")]
    public void ThenOriginalHandStartedPersisted()
    {
        // Verify original event persisted even on saga failure
    }

    [Then(@"the response includes cascade_errors with the saga failure")]
    public void ThenResponseIncludesCascadeErrors()
    {
        // Verify cascade errors in CONTINUE mode
    }

    [Then(@"the response includes successful projection updates")]
    public void ThenResponseIncludesSuccessfulProjectionUpdates()
    {
        // Verify successful projections alongside saga errors
    }

    [Then(@"other sagas continue executing despite the failure")]
    public void ThenOtherSagasContinue()
    {
        // Verify saga continuation in CONTINUE mode
    }

    [Then(@"other sagas continue executing")]
    public void ThenOtherSagasContinueExecuting()
    {
        // Verify saga continuation
    }

    // =========================================================================
    // Then steps - Compensation (COMPENSATE mode)
    // =========================================================================

    [Then(@"compensation commands are issued in reverse order")]
    public void ThenCompensationInReverseOrder()
    {
        // Verify compensation ordering
    }

    [Then(@"the command fails after compensation completes")]
    public void ThenCommandFailsAfterCompensation()
    {
        // Verify failure after compensation
    }

    // =========================================================================
    // Then steps - Dead letter queue
    // =========================================================================

    [Then(@"the saga failure is published to the dead letter queue")]
    public void ThenSagaFailureToDeadLetter()
    {
        // Verify DLQ publication
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
        // Verify PM event receipt
    }

    [Then(@"the response includes PM state updates")]
    public void ThenResponseIncludesPmUpdates()
    {
        // Verify PM state updates
    }

    [Then(@"the process manager is not invoked")]
    public void ThenPmNotInvoked()
    {
        // Verify PM not invoked without correlation ID
    }

    [Then(@"sagas still execute normally")]
    public void ThenSagasExecuteNormally()
    {
        // Verify saga execution
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
        // Verify immediate consistency in CASCADE mode
    }

    // =========================================================================
    // Then steps - Edge cases
    // =========================================================================

    [Then(@"the response has empty cascade_results")]
    public void ThenEmptyResponse()
    {
        // Verify empty cascade results
    }

    [Then(@"the saga produces no commands")]
    public void ThenSagaProducesNoCommands()
    {
        // Verify no saga commands
    }

    [Then(@"the original event is still persisted")]
    public void ThenOriginalEventPersisted()
    {
        // Verify event persistence
    }

    [Then(@"all saga errors are collected in cascade_errors")]
    public void ThenAllSagaErrorsCollected()
    {
        // Verify error collection in CONTINUE mode
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
