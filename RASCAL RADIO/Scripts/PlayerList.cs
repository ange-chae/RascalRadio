using Godot;
using System;
using System.Collections.Generic;

public partial class PlayerList : Control
{
    private NetworkManager _networkManager;

    private Label _lblRoomName;
    private LineEdit _txtRoomCode;
    private ItemList _itemListPlayers;
    private Button _btnStartGame;
    private Label _lblStatus; // Label for status notifications

    // Tracks known player IDs to detect when a new player joins
    private readonly HashSet<long> _knownPlayerIds = new HashSet<long>();
    private bool _isInitialLoad = true;

    public override void _Ready()
    {
        _networkManager = GetNodeOrNull<NetworkManager>("/root/NetworkManager");

        // Use FindChild with recursive=true to search inside any parent Panels or Containers
        _lblRoomName = FindChild("lblRoomName", true, false) as Label;
        _txtRoomCode = FindChild("txtRoomCode", true, false) as LineEdit;
        _itemListPlayers = FindChild("itemListPlayers", true, false) as ItemList;
        _btnStartGame = FindChild("btnStartGame", true, false) as Button;
        _lblStatus = FindChild("lblStatus", true, false) as Label
                     ?? FindChild("lblNotification", true, false) as Label;

        if (_networkManager == null)
        {
            GD.PrintErr("PlayerList: NetworkManager autoload not found.");
            return;
        }

        // Configure read-only room code display with auto-copy
        if (_txtRoomCode != null)
        {
            _txtRoomCode.Editable = false;
            _txtRoomCode.Text = _networkManager.RoomCode;
            _txtRoomCode.FocusEntered += () =>
            {
                _txtRoomCode.CallDeferred(LineEdit.MethodName.SelectAll);
                DisplayServer.ClipboardSet(_networkManager.RoomCode);
                GD.Print("Room code copied to clipboard!");
            };
        }

        if (_lblRoomName != null)
        {
            _lblRoomName.Text = string.IsNullOrEmpty(_networkManager.RoomName) ? "Lobby" : _networkManager.RoomName;
        }

        // Only the host sees the Start Game button
        if (_btnStartGame != null)
        {
            _btnStartGame.Visible = Multiplayer.IsServer();
            _btnStartGame.Pressed += OnStartGamePressed;
        }

        _networkManager.PlayerListUpdated += RefreshPlayerList;
        _networkManager.RoomTerminated += OnRoomTerminated;

        RefreshPlayerList();
        _isInitialLoad = false; // Mark initial setup complete
    }

    private void RefreshPlayerList()
    {
        if (_lblRoomName != null)
        {
            _lblRoomName.Text = string.IsNullOrEmpty(_networkManager.RoomName) ? "Lobby" : _networkManager.RoomName;
        }

        if (_txtRoomCode != null)
        {
            _txtRoomCode.Text = _networkManager.RoomCode;
        }

        if (_itemListPlayers == null) return;

        // Detect new player joins after the initial load
        foreach (var player in _networkManager.Players)
        {
            long id = player.Key;
            string name = player.Value;

            if (!_knownPlayerIds.Contains(id) && !_isInitialLoad)
            {
                ShowStatusMessage($"{name} joined!", new Color("#4CAF50")); // Green color
            }
        }

        // Update tracked known player IDs
        _knownPlayerIds.Clear();
        foreach (var id in _networkManager.Players.Keys)
        {
            _knownPlayerIds.Add(id);
        }

        // Repopulate ItemList
        _itemListPlayers.Clear();

        foreach (var player in _networkManager.Players)
        {
            _itemListPlayers.AddItem(player.Value);
        }
    }

    private async void ShowStatusMessage(string message, Color color)
    {
        if (_lblStatus == null) return;

        _lblStatus.Text = message;
        _lblStatus.AddThemeColorOverride("font_color", color);
        _lblStatus.Visible = true;

        // Auto-clear the message after 3 seconds
        await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
        if (GodotObject.IsInstanceValid(_lblStatus) && _lblStatus.Text == message)
        {
            _lblStatus.Text = "";
        }
    }

    private void OnRoomTerminated(string reason)
    {
        GetTree().ChangeSceneToFile("res://scenes/lobby.tscn");
    }

    private void OnStartGamePressed()
    {
        Rpc(MethodName.StartGameRpc);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StartGameRpc()
    {
        GD.Print("Starting game...");
        GetTree().ChangeSceneToFile("res://scenes/main_game.tscn");
    }

    public override void _ExitTree()
    {
        if (_btnStartGame != null && GodotObject.IsInstanceValid(_btnStartGame))
        {
            _btnStartGame.Pressed -= OnStartGamePressed;
        }

        if (_networkManager != null && GodotObject.IsInstanceValid(_networkManager))
        {
            _networkManager.PlayerListUpdated -= RefreshPlayerList;
            _networkManager.RoomTerminated -= OnRoomTerminated;
        }
    }
}