using Godot;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

public partial class NetworkManager : Node
{
    public const int Port = 7777;
    public const int DiscoveryPort = 7778;
    public const int MaxPlayers = 3;

    public bool IsHosting { get; private set; }
    public string RoomCode { get; private set; } = "";
    public string RoomName { get; private set; } = "";
    public string HostPlayerName { get; private set; } = "";

    public string LocalPlayerName { get; private set; } = "";
    public Dictionary<long, string> Players { get; private set; } = new Dictionary<long, string>();

    public string DisconnectReason { get; set; } = "";
    private bool _isAwaitingJoinResponse = false;

    // Discovery Events
    public event Action<string, string, string, int, int, int> RoomFound;
    public event Action<string> RoomSearchFailed;

    // Connection Events
    public event Action JoinSucceeded;
    public event Action<string> JoinFailed;
    public event Action PlayerListUpdated;
    public event Action<string> RoomTerminated;

    private readonly RandomNumberGenerator _rng = new();
    private PacketPeerUdp _discoveryServer;

    public override void _Ready()
    {
        _rng.Randomize();
        GD.Print("NetworkManager is ready.");

        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
    }

    public override void _Process(double delta)
    {
        if (!IsHosting || _discoveryServer == null)
            return;

        while (_discoveryServer.GetAvailablePacketCount() > 0)
        {
            string request = Encoding.UTF8.GetString(_discoveryServer.GetPacket());
            string senderIP = _discoveryServer.GetPacketIP();
            int senderPort = _discoveryServer.GetPacketPort();

            if (request != "FIND|" + RoomCode)
                continue;

            string encodedRoomName = Convert.ToBase64String(Encoding.UTF8.GetBytes(RoomName));
            string encodedHostName = Convert.ToBase64String(Encoding.UTF8.GetBytes(HostPlayerName));

            string response = $"ROOM|{RoomCode}|{Port}|{encodedRoomName}|{encodedHostName}|{Players.Count}|{MaxPlayers}";

            _discoveryServer.SetDestAddress(senderIP, senderPort);
            _discoveryServer.PutPacket(Encoding.UTF8.GetBytes(response));
        }
    }

    public Error CreateRoom(string playerName, string roomName)
    {
        playerName = playerName.Trim();
        roomName = roomName.Trim();

        if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(roomName))
        {
            GD.PrintErr("Enter both a player name and a room name.");
            return Error.InvalidParameter;
        }

        DisconnectFromRoom();

        var peer = new ENetMultiplayerPeer();
        // Allow MaxPlayers socket capacity so ENet accepts incoming connection requests 
        // and lets RegisterPlayer run to send a clean "Room is full" rejection RPC.
        Error error = peer.CreateServer(Port, MaxPlayers);

        if (error != Error.Ok)
        {
            peer.Close();
            GD.PrintErr($"Could not create room: {error}");
            return error;
        }

        var discovery = new PacketPeerUdp();
        Error listenError = discovery.Bind(DiscoveryPort, "*");

        if (listenError != Error.Ok)
        {
            discovery.Close();
            peer.Close();
            return listenError;
        }

        Multiplayer.MultiplayerPeer = peer;

        HostPlayerName = playerName;
        RoomName = roomName;
        RoomCode = GenerateRoomCode();
        _discoveryServer = discovery;
        IsHosting = true;

        LocalPlayerName = playerName;
        Players.Clear();
        Players.Add(1, LocalPlayerName);

        GD.Print($"Room created: {RoomName} | Code: {RoomCode}");
        return Error.Ok;
    }
    public Error JoinRoom(string hostIP, int port, string playerName)
    {
        if (_isAwaitingJoinResponse)
        {
            return Error.AlreadyInUse; // Prevent spamming duplicate connection requests
        }

        DisconnectFromRoom();
        LocalPlayerName = playerName;
        _isAwaitingJoinResponse = true;

        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateClient(hostIP, port);

        if (error != Error.Ok)
        {
            peer.Close();
            _isAwaitingJoinResponse = false;
            GD.PrintErr($"Could not connect to room at {hostIP}:{port} - Error: {error}");
            return error;
        }

        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"Connecting to room at {hostIP}:{port}...");

        Multiplayer.ConnectedToServer += SendRegistrationOnConnected;

        return Error.Ok;
    }
    private void SendRegistrationOnConnected()
    {
        Multiplayer.ConnectedToServer -= SendRegistrationOnConnected;
        RpcId(1, MethodName.RegisterPlayer, LocalPlayerName);
    }

    public async void SearchForRoom(string code)
    {
        code = code.Trim().ToUpperInvariant();

        if (code.Length != 6)
        {
            RoomSearchFailed?.Invoke("Enter a valid 6-character room code.");
            return;
        }

        var searcher = new PacketPeerUdp();
        Error bindError = searcher.Bind(0, "*");

        if (bindError != Error.Ok)
        {
            searcher.Close();
            RoomSearchFailed?.Invoke("Could not start room search.");
            return;
        }

        searcher.SetBroadcastEnabled(true);
        string request = "FIND|" + code;
        byte[] packet = Encoding.UTF8.GetBytes(request);

        searcher.SetDestAddress("127.0.0.1", DiscoveryPort);
        searcher.PutPacket(packet);
        searcher.SetDestAddress("255.255.255.255", DiscoveryPort);
        searcher.PutPacket(packet);

        double elapsed = 0.0;
        while (elapsed < 5.0)
        {
            while (searcher.GetAvailablePacketCount() > 0)
            {
                string response = Encoding.UTF8.GetString(searcher.GetPacket());
                string hostIP = searcher.GetPacketIP();
                string[] parts = response.Split('|');

                if (parts.Length >= 7 &&
                    parts[0] == "ROOM" &&
                    parts[1] == code &&
                    int.TryParse(parts[2], out int roomPort) &&
                    int.TryParse(parts[5], out int playerCount) &&
                    int.TryParse(parts[6], out int maxPlayers))
                {
                    try
                    {
                        string foundRoomName = Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]));
                        string foundHostName = Encoding.UTF8.GetString(Convert.FromBase64String(parts[4]));

                        searcher.Close();
                        RoomFound?.Invoke(foundRoomName, foundHostName, hostIP, roomPort, playerCount, maxPlayers);
                        return;
                    }
                    catch (FormatException) { }
                }
            }

            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            elapsed += 0.1;
        }

        searcher.Close();
        RoomSearchFailed?.Invoke("Room not found. Check the code and try again.");
    }

    private string GenerateRoomCode()
    {
        const string characters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string code = "";
        for (int i = 0; i < 6; i++)
        {
            code += characters[_rng.RandiRange(0, characters.Length - 1)];
        }
        return code;
    }

    public void DisconnectFromRoom()
    {
        if (_discoveryServer != null)
        {
            _discoveryServer.Close();
            _discoveryServer = null;
        }

        if (Multiplayer.MultiplayerPeer != null)
        {
            if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enetPeer)
            {
                enetPeer.Close();
            }
            Multiplayer.MultiplayerPeer = null;
        }

        IsHosting = false;
        _isAwaitingJoinResponse = false;
        RoomCode = "";
        RoomName = "";
        HostPlayerName = "";
        Players.Clear();
    }

    // ---------------------------------------------------------
    // CONNECTION HANDLERS & RPCS
    // ---------------------------------------------------------

    private void OnConnectionFailed()
    {
        _isAwaitingJoinResponse = false;
        DisconnectFromRoom();
        JoinFailed?.Invoke("Connection failed. Host may be offline.");
    }

    private void OnServerDisconnected()
    {
        GD.Print("Host disconnected from the server.");
        DisconnectFromRoom();
        DisconnectReason = "Studio terminated.";
        RoomTerminated?.Invoke("Studio terminated.");
    }

    private void OnPeerDisconnected(long id)
    {
        if (Players.ContainsKey(id))
        {
            Players.Remove(id);

            if (Multiplayer.IsServer())
            {
                Rpc(MethodName.SyncRoomDetails, RoomName, RoomCode, SerializePlayerList());
                PlayerListUpdated?.Invoke();
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RegisterPlayer(string newPlayerName)
    {
        long senderId = Multiplayer.GetRemoteSenderId();

        if (Multiplayer.IsServer())
        {
            // Reject client if room limit is reached
            if (Players.Count >= MaxPlayers)
            {
                GD.PrintErr($"Connection rejected for {newPlayerName}: Room is full.");
                RpcId(senderId, MethodName.RejectJoin, "Can't connect: Room is full.");
                return;
            }

            Players[senderId] = newPlayerName;
            GD.Print($"Player {newPlayerName} ({senderId}) joined.");

            Rpc(MethodName.SyncRoomDetails, RoomName, RoomCode, SerializePlayerList());
            PlayerListUpdated?.Invoke();
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RejectJoin(string reason)
    {
        GD.PrintErr($"Join rejected: {reason}");
        _isAwaitingJoinResponse = false;
        DisconnectFromRoom();
        JoinFailed?.Invoke(reason);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncRoomDetails(string roomName, string roomCode, string serializedList)
    {
        RoomName = roomName;
        RoomCode = roomCode;

        Players.Clear();
        var pairs = serializedList.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in pairs)
        {
            var parts = pair.Split(':');
            if (parts.Length == 2 && long.TryParse(parts[0], out long id))
            {
                Players[id] = parts[1];
            }
        }

        if (_isAwaitingJoinResponse)
        {
            _isAwaitingJoinResponse = false;
            JoinSucceeded?.Invoke();
        }

        PlayerListUpdated?.Invoke();
    }

    private string SerializePlayerList()
    {
        return string.Join(";", Players.Select(kv => $"{kv.Key}:{kv.Value}"));
    }
}