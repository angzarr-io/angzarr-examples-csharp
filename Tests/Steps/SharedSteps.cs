using Angzarr.Client;
using FluentAssertions;
using TechTalk.SpecFlow;
using Tests.Support;

namespace Tests.Steps;

[Binding]
public class SharedSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly TestContext _testContext;

    public SharedSteps(ScenarioContext scenarioContext, TestContext testContext)
    {
        _scenarioContext = scenarioContext;
        _testContext = testContext;
    }

    private Exception? GetError()
    {
        // Check all domain-specific error context keys
        if (
            _scenarioContext.TryGetValue("error", out CommandRejectedError? playerError)
            && playerError != null
        )
            return playerError;
        if (
            _scenarioContext.TryGetValue("tableError", out CommandRejectedError? tableError)
            && tableError != null
        )
            return tableError;
        // Hand uses TestContext.LastException
        if (_testContext.LastException != null)
            return _testContext.LastException;
        return null;
    }

    [Then(@"the command fails with status ""(.*)""")]
    public void ThenTheCommandFailsWithStatus(string status)
    {
        var error = GetError();
        error.Should().NotBeNull("Expected command to fail but it succeeded");
        // Mirror Python's status_code: gRPC-style category (FAILED_PRECONDITION /
        // INVALID_ARGUMENT / NOT_FOUND). On CommandRejectedError that's StatusCode;
        // on InvalidArgumentError it's always INVALID_ARGUMENT.
        var observedStatus = error switch
        {
            CommandRejectedError cre => cre.StatusCode,
            InvalidArgumentError => "INVALID_ARGUMENT",
            _ => null,
        };
        observedStatus.Should().Be(status, $"Expected status {status} but got {observedStatus}");
    }

    [Then(@"the error message contains ""(.*)""")]
    public void ThenTheErrorMessageContains(string text)
    {
        var error = GetError();
        error.Should().NotBeNull("Expected an error but got success");
        error!.Message.ToLower().Should().Contain(text.ToLower());
    }

    [Then(@"the command is rejected with code ""(.*)""")]
    public void ThenTheCommandIsRejectedWithCode(string code)
    {
        var error = GetError();
        error.Should().NotBeNull("Expected command to fail but it succeeded");
        error.Should().BeOfType<CommandRejectedError>("Rejection-code only set on CommandRejectedError");
        ((CommandRejectedError)error!).Code.Should().Be(code);
    }

    [Then(@"the rejection field ""(.*)"" equals ""(.*)""")]
    public void ThenTheRejectionFieldEquals(string field, string expected)
    {
        var error = GetError();
        error.Should().NotBeNull("Expected command to fail but it succeeded");
        error.Should().BeOfType<CommandRejectedError>();
        var details = ((CommandRejectedError)error!).Details;
        details.Should().NotBeNull("Rejection has no details map");
        details!.Should().ContainKey(field, $"Rejection details missing field {field}");
        details[field].Should().Be(expected);
    }
}
