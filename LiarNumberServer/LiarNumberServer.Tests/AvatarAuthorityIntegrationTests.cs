using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LiarNumberServer.Handlers;
using LiarNumberServer.Managers;
using LiarNumberServer.Messages;
using LiarNumberServer.Messages.ClientToServer;
using LiarNumberServer.Network;
using Xunit;

namespace LiarNumberServer.Tests;

public class AvatarAuthorityIntegrationTests
{
    [Fact]
    public async Task JoinLobby_Returns_AvatarId_In_Valid_Range()
    {
        await using var harness = await TestHarness.CreateAsync();
        var client = await harness.ConnectClientAsync();

        await client.SendAsync("JoinLobby", new { nickname = "HostA" });
        var lobbyJoined = await client.ReadUntilTypeAsync("LobbyJoined");

        var avatarId = lobbyJoined.Payload.GetProperty("avatarId").GetInt32();
        Assert.InRange(avatarId, 0, 11);
    }

    [Fact]
    public async Task CreateRoom_Ignores_Spoofed_AvatarId_And_Uses_Authoritative()
    {
        await using var harness = await TestHarness.CreateAsync();
        var client = await harness.ConnectClientAsync();

        var lobby = await JoinLobbyAsync(client, "HostB");
        var spoofAvatar = (lobby.AvatarId + 1) % 12;

        await client.SendAsync("CreateRoom", new { playerId = lobby.PlayerId, nickname = lobby.Nickname, avatarId = spoofAvatar });

        var roomCreated = await client.ReadUntilTypeAsync("RoomCreated");
        var hostAvatarId = roomCreated.Payload.GetProperty("hostAvatarId").GetInt32();
        Assert.Equal(lobby.AvatarId, hostAvatarId);

        var roomState = await client.ReadUntilTypeAsync("RoomState");
        var hostStateAvatar = FindAvatarByName(roomState.Payload, lobby.Nickname);
        Assert.Equal(lobby.AvatarId, hostStateAvatar);
    }

    [Fact]
    public async Task JoinRoom_Ignores_Spoofed_AvatarId_And_Uses_Authoritative()
    {
        await using var harness = await TestHarness.CreateAsync();
        var host = await harness.ConnectClientAsync();
        var joiner = await harness.ConnectClientAsync();

        var hostLobby = await JoinLobbyAsync(host, "HostC");
        await host.SendAsync("CreateRoom", new { playerId = hostLobby.PlayerId, nickname = hostLobby.Nickname, avatarId = hostLobby.AvatarId });
        var created = await host.ReadUntilTypeAsync("RoomCreated");
        var roomId = created.Payload.GetProperty("roomId").GetString()!;
        await host.ReadUntilTypeAsync("RoomState");

        var joinerLobby = await JoinLobbyAsync(joiner, "JoinerC");
        var spoofAvatar = (joinerLobby.AvatarId + 1) % 12;
        await joiner.SendAsync("JoinRoom", new { roomId, playerId = joinerLobby.PlayerId, nickname = joinerLobby.Nickname, avatarId = spoofAvatar });

        await joiner.ReadUntilTypeAsync("RoomJoined");
        var joinerRoomState = await joiner.ReadUntilTypeAsync("RoomState");
        var joinerStateAvatar = FindAvatarByName(joinerRoomState.Payload, joinerLobby.Nickname);

        Assert.Equal(joinerLobby.AvatarId, joinerStateAvatar);
    }

    [Fact]
    public async Task RoomState_Always_Contains_Authoritative_Avatar_For_All_Players()
    {
        await using var harness = await TestHarness.CreateAsync();
        var host = await harness.ConnectClientAsync();
        var joiner = await harness.ConnectClientAsync();

        var hostLobby = await JoinLobbyAsync(host, "HostD");
        await host.SendAsync("CreateRoom", new { playerId = hostLobby.PlayerId, nickname = hostLobby.Nickname, avatarId = (hostLobby.AvatarId + 3) % 12 });
        var created = await host.ReadUntilTypeAsync("RoomCreated");
        var roomId = created.Payload.GetProperty("roomId").GetString()!;
        await host.ReadUntilTypeAsync("RoomState");

        var joinerLobby = await JoinLobbyAsync(joiner, "JoinerD");
        await joiner.SendAsync("JoinRoom", new { roomId, playerId = joinerLobby.PlayerId, nickname = joinerLobby.Nickname, avatarId = (joinerLobby.AvatarId + 5) % 12 });

        await joiner.ReadUntilTypeAsync("RoomJoined");
        var roomState = await joiner.ReadUntilTypeAsync("RoomState");

        Assert.Equal(hostLobby.AvatarId, FindAvatarByName(roomState.Payload, hostLobby.Nickname));
        Assert.Equal(joinerLobby.AvatarId, FindAvatarByName(roomState.Payload, joinerLobby.Nickname));
    }

    [Fact]
    public async Task PlayerJoined_Contains_Correct_Authoritative_AvatarId()
    {
        await using var harness = await TestHarness.CreateAsync();
        var host = await harness.ConnectClientAsync();
        var joiner = await harness.ConnectClientAsync();

        var hostLobby = await JoinLobbyAsync(host, "HostE");
        await host.SendAsync("CreateRoom", new { playerId = hostLobby.PlayerId, nickname = hostLobby.Nickname, avatarId = hostLobby.AvatarId });
        var created = await host.ReadUntilTypeAsync("RoomCreated");
        var roomId = created.Payload.GetProperty("roomId").GetString()!;
        await host.ReadUntilTypeAsync("RoomState");

        var joinerLobby = await JoinLobbyAsync(joiner, "JoinerE");
        await joiner.SendAsync("JoinRoom", new { roomId, playerId = joinerLobby.PlayerId, nickname = joinerLobby.Nickname, avatarId = (joinerLobby.AvatarId + 2) % 12 });

        var playerJoined = await host.ReadUntilTypeAsync("PlayerJoined");
        Assert.Equal(joinerLobby.Nickname, playerJoined.Payload.GetProperty("playerName").GetString());
        Assert.Equal(joinerLobby.AvatarId, playerJoined.Payload.GetProperty("avatarId").GetInt32());
    }

    [Fact]
    public async Task OutOfRange_Authoritative_Avatar_Is_Clamped_To_Zero()
    {
        await using var harness = await TestHarness.CreateAsync();
        var client = await harness.ConnectClientAsync();

        client.ServerConnection.PlayerId = "manual-p1";
        client.ServerConnection.Nickname = "ManualHost";
        client.ServerConnection.AvatarId = 999;

        var roomHandler = new RoomHandler(new RoomManager(), avatarCount: 12);
        var payload = JsonSerializer.Serialize(new CreateRoomRequest
        {
            playerId = "manual-p1",
            nickname = "ManualHost",
            avatarId = 3
        });

        roomHandler.HandleCreateRoom(payload, client.ServerConnection);

        var roomCreated = await client.ReadUntilTypeAsync("RoomCreated");
        Assert.Equal(0, roomCreated.Payload.GetProperty("hostAvatarId").GetInt32());
    }

    private static async Task<(string PlayerId, string Nickname, int AvatarId)> JoinLobbyAsync(TestClient client, string nickname)
    {
        await client.SendAsync("JoinLobby", new { nickname });
        var lobbyJoined = await client.ReadUntilTypeAsync("LobbyJoined");

        return (
            lobbyJoined.Payload.GetProperty("playerId").GetString()!,
            lobbyJoined.Payload.GetProperty("nickname").GetString()!,
            lobbyJoined.Payload.GetProperty("avatarId").GetInt32());
    }

    private static int FindAvatarByName(JsonElement roomStatePayload, string playerName)
    {
        foreach (var player in roomStatePayload.GetProperty("players").EnumerateArray())
        {
            if (string.Equals(player.GetProperty("playerName").GetString(), playerName, StringComparison.Ordinal))
            {
                return player.GetProperty("avatarId").GetInt32();
            }
        }

        throw new Xunit.Sdk.XunitException($"Player '{playerName}' not found in RoomState");
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly MessageRouter _router;
        private readonly GameServer _server;
        private readonly List<TestClient> _clients = new();

        private TestHarness(TcpListener listener, MessageRouter router, GameServer server)
        {
            _listener = listener;
            _router = router;
            _server = server;
        }

        public static Task<TestHarness> CreateAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var router = new MessageRouter();
            var server = new GameServer("127.0.0.1", 0);
            return Task.FromResult(new TestHarness(listener, router, server));
        }

        public async Task<TestClient> ConnectClientAsync()
        {
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            var outboundClient = new TcpClient();
            var connectTask = outboundClient.ConnectAsync(endpoint.Address, endpoint.Port);
            var inboundClient = await _listener.AcceptTcpClientAsync();
            await connectTask;

            var serverConnection = new ClientConnection(inboundClient, _router, _server);
            var testClient = new TestClient(outboundClient, serverConnection);
            _clients.Add(testClient);
            return testClient;
        }

        public ValueTask DisposeAsync()
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }

            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestClient : IDisposable
    {
        private readonly TcpClient _client;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        public ClientConnection ServerConnection { get; }

        public TestClient(TcpClient client, ClientConnection serverConnection)
        {
            _client = client;
            ServerConnection = serverConnection;
            var stream = _client.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            _writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        }

        public async Task SendAsync(string type, object payload)
        {
            var message = new BaseMessage
            {
                type = type,
                payload = JsonSerializer.Serialize(payload)
            };

            var json = JsonSerializer.Serialize(message);
            await _writer.WriteLineAsync(json);
        }

        public async Task<ReceivedMessage> ReadUntilTypeAsync(string type, int timeoutMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var remaining = Math.Max(1, timeoutMs - (int)sw.ElapsedMilliseconds);
                var received = await ReadNextAsync(remaining);
                if (received != null && string.Equals(received.Type, type, StringComparison.Ordinal))
                {
                    return received;
                }
            }

            throw new Xunit.Sdk.XunitException($"Timeout waiting for message type '{type}'");
        }

        private async Task<ReceivedMessage?> ReadNextAsync(int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);

            string? line;
            try
            {
                line = await _reader.ReadLineAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            var wrapper = JsonSerializer.Deserialize<BaseMessage>(line);
            if (wrapper == null)
            {
                return null;
            }

            using var payloadDoc = JsonDocument.Parse(wrapper.payload);
            return new ReceivedMessage(wrapper.type, payloadDoc.RootElement.Clone());
        }

        public void Dispose()
        {
            ServerConnection.Disconnect();
            _client.Close();
        }
    }

    private sealed record ReceivedMessage(string Type, JsonElement Payload);
}
