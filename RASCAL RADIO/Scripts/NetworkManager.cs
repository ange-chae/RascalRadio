using Godot;
using System;
using System.Text;
using System.Threading.Tasks;

public partial class NetworkManager : Node
{
    public const int Port = 7777;
    public const int DiscoveryPort = 7778;
    public const int MaxPlayers = 3;

    public bool IsHosting { get; private set; }
    public string RoomCode { get; private set; } = "";
    public string RoomName { get; private set; } = "";
    public string HostPlayerName { get; private set; } = "";

    public event Action<string, string, string, int> RoomFound;
    public event Action<string> RoomSearchFailed;

    private readonly RandomNumberGenerator _rng = new();
    private PacketPeerUdp _discoveryServer;

    public override void _Ready()
    {
        _rng.Randomize();
        GD.Print("NetworkManager is ready.");
    }

    public override void _Process(double delta)
    {
        if (!IsHosting || _discoveryServer == null)
            return;

        while (_discoveryServer.GetAvailablePacketCount() > 0)
        {
            string request = Encoding.UTF8.GetString(
                _discoveryServer.GetPacket()
            );

            string senderIP = _discoveryServer.GetPacketIP();
            int senderPort = _discoveryServer.GetPacketPort();

            if (request != "FIND|" + RoomCode)
                continue;

            string encodedRoomName = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(RoomName)
            );

            string encodedHostName = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(HostPlayerName)
            );

            string response =
                $"ROOM|{RoomCode}|{Port}|{encodedRoomName}|{encodedHostName}";

            _discoveryServer.SetDestAddress(senderIP, senderPort);
            _discoveryServer.PutPacket(Encoding.UTF8.GetBytes(response));

            GD.Print($"Room discovery response sent to {senderIP}");
        }
    }

    public Error CreateRoom(string playerName, string roomName)
    {
        playerName = playerName.Trim();
        roomName = roomName.Trim();

        if (string.IsNullOrWhiteSpace(playerName) ||
            string.IsNullOrWhiteSpace(roomName))
        {
            GD.PrintErr("Enter both a player name and a room name.");
            return Error.InvalidParameter;
        }

        // Always disconnect and close existing peers/sockets before creating a new server
        DisconnectFromRoom();

        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateServer(Port, MaxPlayers - 1);

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
            GD.PrintErr(
                $"Could not start LAN discovery on port {DiscoveryPort}: {listenError}"
            );
            return listenError;
        }

        Multiplayer.MultiplayerPeer = peer;

        HostPlayerName = playerName;
        RoomName = roomName;
        RoomCode = GenerateRoomCode();
        _discoveryServer = discovery;
        IsHosting = true;

        GD.Print($"Room created: {RoomName} | Code: {RoomCode}");
        GD.Print($"LAN discovery listening on port {DiscoveryPort}.");

        return Error.Ok;
    }

    public Error JoinRoom(string hostIP, int port)
    {
        DisconnectFromRoom();

        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateClient(hostIP, port);

        if (error != Error.Ok)
        {
            peer.Close();
            GD.PrintErr($"Could not connect to room at {hostIP}:{port} - Error: {error}");
            return error;
        }

        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"Connecting to room at {hostIP}:{port}...");
        return Error.Ok;
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

        // Local test: find a host running on this same laptop.
        searcher.SetDestAddress("127.0.0.1", DiscoveryPort);
        searcher.PutPacket(packet);

        // LAN search: find hosts on the local network.
        searcher.SetDestAddress("255.255.255.255", DiscoveryPort);
        searcher.PutPacket(packet);

        GD.Print($"Searching for room code {code}...");

        double elapsed = 0.0;

        while (elapsed < 5.0)
        {
            while (searcher.GetAvailablePacketCount() > 0)
            {
                string response = Encoding.UTF8.GetString(
                    searcher.GetPacket()
                );

                string hostIP = searcher.GetPacketIP();
                string[] parts = response.Split('|');

                if (parts.Length == 5 &&
                    parts[0] == "ROOM" &&
                    parts[1] == code &&
                    int.TryParse(parts[2], out int roomPort))
                {
                    try
                    {
                        string foundRoomName = Encoding.UTF8.GetString(
                            Convert.FromBase64String(parts[3])
                        );

                        string foundHostName = Encoding.UTF8.GetString(
                            Convert.FromBase64String(parts[4])
                        );

                        searcher.Close();

                        GD.Print(
                            $"Found room '{foundRoomName}' hosted by {foundHostName} at {hostIP}."
                        );

                        RoomFound?.Invoke(
                            foundRoomName,
                            foundHostName,
                            hostIP,
                            roomPort
                        );

                        return;
                    }
                    catch (FormatException)
                    {
                        GD.PrintErr("Received an invalid room discovery response.");
                    }
                }
            }

            await ToSignal(
                GetTree().CreateTimer(0.1),
                SceneTreeTimer.SignalName.Timeout
            );

            elapsed += 0.1;
        }

        searcher.Close();

        GD.Print($"No room found for code {code}.");
        RoomSearchFailed?.Invoke(
            "Room not found. Check the code and try again."
        );
    }

    private string GenerateRoomCode()
    {
        const string characters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string code = "";

        for (int i = 0; i < 6; i++)
        {
            int index = _rng.RandiRange(0, characters.Length - 1);
            code += characters[index];
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
        RoomCode = "";
        RoomName = "";
        HostPlayerName = "";

        GD.Print("Disconnected from room.");
    }
}