using Godot;
using System;
using System.Collections.Generic;

public partial class PlayerList : Control
{
    private NetworkManager _networkManager;

    private Label _lblRoomName;
    private LineEdit _txtRoomCode;
    private Container _playerSlotsContainer;
    private Button _btnStartGame;
    private Label _lblStatus;

    // Tracks players (ID and Name) to detect joins and leaves
    private readonly Dictionary<long, string> _knownPlayers = new Dictionary<long, string>();
    private bool _isInitialLoad = true;

    [Export] public Texture2D DefaultAvatarTexture;

    public override void _Ready()
    {
        _networkManager = GetNodeOrNull<NetworkManager>("/root/NetworkManager");

        _lblRoomName = FindChild("lblRoomName", true, false) as Label;
        _txtRoomCode = FindChild("txtRoomCode", true, false) as LineEdit;
        _playerSlotsContainer = FindChild("playerSlotsContainer", true, false) as Container;
        _btnStartGame = FindChild("btnStartGame", true, false) as Button;
        _lblStatus = FindChild("lblStatus", true, false) as Label;

        if (_networkManager == null)
        {
            GD.PrintErr("PlayerList: NetworkManager autoload not found.");
            return;
        }

        // Room Code Input Box in the middle
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

        // Only Host sees the Start Game button
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

        // Detect LEAVES (Red Notification)
        foreach (var knownPlayer in _knownPlayers)
        {
            if (!_networkManager.Players.ContainsKey(knownPlayer.Key) && !_isInitialLoad)
            {
                ShowStatusMessage($"{knownPlayer.Value} left!", new Color("#F44336")); // Red
            }
        }

        // Detect JOINS (Green Notification)
        foreach (var player in _networkManager.Players)
        {
            if (!_knownPlayers.ContainsKey(player.Key) && !_isInitialLoad)
            {
                ShowStatusMessage($"{player.Value} joined!", new Color("#4CAF50")); // Green
            }
        }

        // Update known players list
        _knownPlayers.Clear();
        foreach (var player in _networkManager.Players)
        {
            _knownPlayers.Add(player.Key, player.Value);
        }

        if (_playerSlotsContainer == null) return;

        var slots = _playerSlotsContainer.GetChildren();
        var playerList = new List<KeyValuePair<long, string>>(_networkManager.Players);

        long localPeerId = Multiplayer.GetUniqueId();

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] is not Control slotNode) continue;

            var avatarBtn = slotNode.FindChild("btnAvatar", true, false) as TextureButton;
            var nameLabel = slotNode.FindChild("lblPlayerName", true, false) as Label;
            var statusLabel = slotNode.FindChild("lblRoleStatus", true, false) as Label;

            if (i < playerList.Count)
            {
                long peerId = playerList[i].Key;
                string playerName = playerList[i].Value;

                if (nameLabel != null) nameLabel.Text = playerName;
                if (statusLabel != null) statusLabel.Text = "Connected";

                // Enable avatar button ONLY for the local player so they can click it to choose a role
                if (avatarBtn != null)
                {
                    // Apply the default texture if none is set
                    if (avatarBtn.TextureNormal == null && DefaultAvatarTexture != null)
                    {
                        avatarBtn.TextureNormal = DefaultAvatarTexture;
                    }

                    bool isLocalPlayer = (peerId == localPeerId);
                    avatarBtn.Disabled = !isLocalPlayer;

                    // Slightly dim non-local players
                    avatarBtn.Modulate = isLocalPlayer ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0.7f);

                    // Disconnect old clicks before reconnecting
                    avatarBtn.Pressed -= OnLocalAvatarClicked;
                    if (isLocalPlayer)
                    {
                        avatarBtn.Pressed += OnLocalAvatarClicked;
                    }
                }
            }
            else
            {
                // Vacant slot
                if (nameLabel != null) nameLabel.Text = "Open Slot";
                if (statusLabel != null) statusLabel.Text = "Waiting...";
                if (avatarBtn != null)
                {
                    avatarBtn.Disabled = true;
                    avatarBtn.Pressed -= OnLocalAvatarClicked;
                }
            }
        }
    }

    private async void ShowStatusMessage(string message, Color color)
    {
        if (_lblStatus == null) return;

        _lblStatus.Text = message;
        _lblStatus.AddThemeColorOverride("font_color", color);
        _lblStatus.Visible = true;

        // Message stays visible for 3 seconds then disappears
        await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
        if (GodotObject.IsInstanceValid(_lblStatus) && _lblStatus.Text == message)
        {
            _lblStatus.Text = "";
        }
    }

    private void OnLocalAvatarClicked()
    {
        GD.Print("Local player clicked their avatar! Opening Role Selection...");
        OpenRoleSelection();
    }

    private void OpenRoleSelection()
    {
        // Prevent opening multiple instances if it's already open
        if (HasNode("RoleSelectionOverlay")) return;

        var roleSelectScene = GD.Load<PackedScene>("res://scenes/RoleSelect/role_selection.tscn");
        if (roleSelectScene != null)
        {
            var roleSelectOverlay = roleSelectScene.Instantiate<Control>();
            roleSelectOverlay.Name = "RoleSelectionOverlay";

            // Stretch overlay to cover full screen
            roleSelectOverlay.SetAnchorsPreset(LayoutPreset.FullRect);

            AddChild(roleSelectOverlay);
            GD.Print("Opened Role Selection overlay.");
        }
        else
        {
            GD.PrintErr("Failed to load scene at res://scenes/RoleSelect/role_selection.tscn");
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
        GetTree().ChangeSceneToFile("res://scenes/main_game.tscn");
    }

    public override void _ExitTree()
    {
        if (_btnStartGame != null && GodotObject.IsInstanceValid(_btnStartGame))
            _btnStartGame.Pressed -= OnStartGamePressed;

        if (_networkManager != null && GodotObject.IsInstanceValid(_networkManager))
        {
            _networkManager.PlayerListUpdated -= RefreshPlayerList;
            _networkManager.RoomTerminated -= OnRoomTerminated;
        }
    }
}