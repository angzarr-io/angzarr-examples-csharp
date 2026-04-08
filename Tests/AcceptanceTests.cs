// Acceptance tests for poker example applications.
//
// These tests run against a deployed Kubernetes cluster with the poker apps.
// Commands are sent directly to aggregate coordinator sidecars via gRPC.
//
// Environment variables:
// - PLAYER_URL: Player aggregate coordinator (default: http://localhost:1310)

using Angzarr;
using Angzarr.Examples;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using NUnit.Framework;

namespace Tests;

[TestFixture]
[Category("Acceptance")]
public class AcceptanceTests
{
    private static string PlayerUrl =>
        Environment.GetEnvironmentVariable("PLAYER_URL") ?? "http://localhost:1310";

    private static CommandHandlerCoordinatorService.CommandHandlerCoordinatorServiceClient
        CreatePlayerClient()
    {
        var channel = GrpcChannel.ForAddress(PlayerUrl);
        return new CommandHandlerCoordinatorService.CommandHandlerCoordinatorServiceClient(channel);
    }

    private static UUID NewUuid()
    {
        return new UUID { Value = ByteString.CopyFrom(Guid.NewGuid().ToByteArray()) };
    }

    private static CommandRequest MakeCommandRequest(
        string domain,
        UUID root,
        Google.Protobuf.WellKnownTypes.Any command,
        uint sequence = 0
    )
    {
        return new CommandRequest
        {
            Command = new CommandBook
            {
                Cover = new Cover
                {
                    Domain = domain,
                    Root = root,
                    CorrelationId = Guid.NewGuid().ToString(),
                },
                Pages =
                {
                    new CommandPage
                    {
                        Header = new PageHeader { Sequence = sequence },
                        Command = command,
                    },
                },
            },
            SyncMode = SyncMode.Simple,
        };
    }

    private static Google.Protobuf.WellKnownTypes.Any PackCommand(IMessage cmd, string typeName)
    {
        return new Google.Protobuf.WellKnownTypes.Any
        {
            TypeUrl = $"type.googleapis.com/{typeName}",
            Value = cmd.ToByteString(),
        };
    }

    [Test]
    public async Task TestPlayerAggregateConnectivity()
    {
        var url = PlayerUrl;
        TestContext.WriteLine($"Connecting to player aggregate at {url}");

        using var channel = GrpcChannel.ForAddress(url);
        var client =
            new CommandHandlerCoordinatorService.CommandHandlerCoordinatorServiceClient(channel);

        // Just verify the channel can be created and a call doesn't throw a connection refused.
        // We send a minimal (invalid) request; any gRPC-level response means the service is up.
        try
        {
            var request = MakeCommandRequest(
                "player",
                NewUuid(),
                PackCommand(
                    new RegisterPlayer
                    {
                        DisplayName = "ConnectivityProbe",
                        Email = "probe@example.com",
                        PlayerType = PlayerType.Human,
                    },
                    "examples.RegisterPlayer"
                )
            );
            var response = await client.HandleCommandAsync(request);
            TestContext.WriteLine($"Connectivity OK - got response");
        }
        catch (RpcException ex) when (ex.StatusCode != StatusCode.Unavailable)
        {
            // Any gRPC status other than Unavailable means the service is reachable
            TestContext.WriteLine(
                $"Connectivity OK - service reachable (status: {ex.StatusCode})"
            );
        }
        // Unavailable would propagate and fail the test
    }

    [Test]
    public async Task TestRegisterPlayer()
    {
        var client = CreatePlayerClient();
        var playerId = NewUuid();
        var playerIdHex = BitConverter.ToString(playerId.Value.ToByteArray()).Replace("-", "");
        TestContext.WriteLine($"Registering player with ID: {playerIdHex}");

        var cmd = new RegisterPlayer
        {
            DisplayName = "AcceptanceTestPlayer",
            Email = $"test-{playerIdHex[..8]}@example.com",
            PlayerType = PlayerType.Human,
        };

        var request = MakeCommandRequest(
            "player",
            playerId,
            PackCommand(cmd, "examples.RegisterPlayer")
        );

        var response = await client.HandleCommandAsync(request);

        Assert.That(response.Events, Is.Not.Null, "Response should contain events");
        Assert.That(response.Events.Cover, Is.Not.Null, "Event book should have a cover");
        Assert.That(response.Events.Pages, Is.Not.Empty, "Should have at least one event");
        TestContext.WriteLine(
            $"Successfully registered player, got {response.Events.Pages.Count} event(s)"
        );
    }

    [Test]
    public async Task TestRegisterAndDeposit()
    {
        var client = CreatePlayerClient();
        var playerId = NewUuid();
        var playerIdHex = BitConverter.ToString(playerId.Value.ToByteArray()).Replace("-", "");
        TestContext.WriteLine($"Test: Register and deposit for player {playerIdHex}");

        // Register a new player
        var registerCmd = new RegisterPlayer
        {
            DisplayName = "DepositTestPlayer",
            Email = $"deposit-{playerIdHex[..8]}@example.com",
            PlayerType = PlayerType.Human,
        };

        var registerRequest = MakeCommandRequest(
            "player",
            playerId,
            PackCommand(registerCmd, "examples.RegisterPlayer")
        );

        var registerResponse = await client.HandleCommandAsync(registerRequest);
        Assert.That(registerResponse.Events, Is.Not.Null, "Registration should succeed");
        TestContext.WriteLine("Player registered successfully");

        // Now deposit funds (sequence 1 since registration was sequence 0).
        // Retry with backoff because the aggregate may not have finished
        // processing the registration event yet (eventual consistency).
        var depositCmd = new DepositFunds
        {
            Amount = new Currency { Amount = 1000, CurrencyCode = "USD" },
        };

        const int maxAttempts = 30;
        RpcException? lastErr = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var depositRequest = MakeCommandRequest(
                    "player",
                    playerId,
                    PackCommand(depositCmd, "examples.DepositFunds"),
                    sequence: 1
                );

                var depositResponse = await client.HandleCommandAsync(depositRequest);

                Assert.That(depositResponse.Events, Is.Not.Null, "Response should contain events");
                Assert.That(
                    depositResponse.Events.Pages,
                    Is.Not.Empty,
                    "Should have deposited event"
                );
                TestContext.WriteLine(
                    $"Successfully deposited funds, got {depositResponse.Events.Pages.Count} event(s)"
                );
                lastErr = null;
                break;
            }
            catch (RpcException ex)
            {
                TestContext.WriteLine(
                    $"DepositFunds attempt {attempt}/{maxAttempts} failed: {ex.StatusCode}"
                );
                lastErr = ex;
                await Task.Delay(500 * attempt);
            }
        }

        if (lastErr != null)
        {
            Assert.Fail($"DepositFunds failed after {maxAttempts} attempts: {lastErr}");
        }
    }

    [Test]
    public async Task TestDuplicateRegistrationFails()
    {
        var client = CreatePlayerClient();
        var playerId = NewUuid();
        var playerIdHex = BitConverter.ToString(playerId.Value.ToByteArray()).Replace("-", "");
        TestContext.WriteLine($"Test: Duplicate registration for player {playerIdHex}");

        var cmd = new RegisterPlayer
        {
            DisplayName = "DuplicateTestPlayer",
            Email = $"dup-{playerIdHex[..8]}@example.com",
            PlayerType = PlayerType.Human,
        };

        // First registration should succeed
        var request1 = MakeCommandRequest(
            "player",
            playerId,
            PackCommand(cmd, "examples.RegisterPlayer")
        );

        var response1 = await client.HandleCommandAsync(request1);
        Assert.That(response1.Events, Is.Not.Null, "First registration should succeed");
        TestContext.WriteLine("First registration succeeded");

        // Second registration with same ID should fail
        var request2 = MakeCommandRequest(
            "player",
            playerId,
            PackCommand(cmd, "examples.RegisterPlayer")
        );

        try
        {
            await client.HandleCommandAsync(request2);
            Assert.Fail("Duplicate registration should have failed");
        }
        catch (RpcException ex)
        {
            TestContext.WriteLine($"Duplicate registration correctly rejected: {ex.StatusCode}");
            Assert.That(
                ex.StatusCode,
                Is.EqualTo(StatusCode.AlreadyExists).Or.EqualTo(StatusCode.FailedPrecondition),
                $"Expected AlreadyExists or FailedPrecondition, got {ex.StatusCode}"
            );
        }
    }
}
