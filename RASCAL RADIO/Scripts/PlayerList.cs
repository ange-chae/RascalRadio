using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class PlayerList : Control
{
    private NetworkManager _networkManager;

    private Label _lblRoomName;
    private LineEdit _txtRoomCode;
    private Container _playerSlotsContainer;
    private Button _btnStartGame;
    private Button _btnLeave;
    private Label _lblStatus;

    // Track players to detect joins and leaves
    private readonly Dictionary<long, string> _knownPlayers = new Dictionary<long, string>();
    private bool _isInitialLoad = true;

    // --- NEW: Network Synced State ---
    public Dictionary<long, string> PlayerRoles = new Dictionary<long, string>();
    public Dictionary<long, bool> PlayerReady = new Dictionary<long, bool>();
    // ---------------------------------

    [Export] public Texture2D DefaultAvatarTexture;

    public override void _Ready()
    {
        _networkManager = GetNodeOrNull<NetworkManager>("/root/NetworkManager");

        _lblRoomName = FindChild("lblRoomName", true, false) as Label;
        _txtRoomCode = FindChild("txtRoomCode", true, false) as LineEdit;
        _playerSlotsContainer = FindChild("playerSlotsContainer", true, false) as Container;
        _btnStartGame = FindChild("btnStartGame", true, false) as Button;
        _btnLeave = FindChild("btnLeave", true, false) as Button;
        _lblStatus = FindChild("lblStatus", true, false) as Label;

        if (_networkManager == null) return;

        if (_txtRoomCode != null)
        {
            _txtRoomCode.Editable = false;
            _txtRoomCode.Text = _networkManager.RoomCode;
            _txtRoomCode.FocusEntered += () =>
            {
                _txtRoomCode.CallDeferred(LineEdit.MethodName.SelectAll);
                DisplayServer.ClipboardSet(_networkManager.RoomCode);
            };
        }

        if (_lblRoomName != null)
        {
            _lblRoomName.Text = string.IsNullOrEmpty(_networkManager.RoomName) ? "Lobby" : _networkManager.RoomName;
        }

        if (_btnStartGame != null)
        {
            _btnStartGame.Visible = Multiplayer.IsServer();
            _btnStartGame.Pressed += OnStartGamePressed;
        }

        if (_btnLeave != null) _btnLeave.Pressed += OnLeaveButtonPressed;

        _networkManager.PlayerListUpdated += RefreshPlayerList;
        _networkManager.RoomTerminated += OnRoomTerminated;

        RefreshPlayerList();
        _isInitialLoad = false;

        // --- FADE IN EFFECT ---
        CanvasLayer fadeLayer = new CanvasLayer { Layer = 100 };
        AddChild(fadeLayer);
        ColorRect fadeRect = new ColorRect { Color = new Color(0, 0, 0, 1) };
        fadeLayer.AddChild(fadeRect);
        fadeRect.SetAnchorsPreset(LayoutPreset.FullRect);
        fadeRect.Size = GetViewportRect().Size;

        Tween tween = CreateTween();
        tween.TweenProperty(fadeRect, "color", new Color(0, 0, 0, 0), 0.8f);
        tween.TweenCallback(Callable.From(fadeLayer.QueueFree));
    }

    // --- NEW: RPC to Sync State Across All Players ---
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void UpdatePlayerStateRpc(long peerId, string role, bool isReady)
    {
        PlayerRoles[peerId] = role;
        PlayerReady[peerId] = isReady;

        RefreshPlayerList();

        // Update the role selection UI if it happens to be open
        var roleSelection = GetNodeOrNull<RoleSelection>("RoleSelectionOverlay");
        if (roleSelection != null)
        {
            roleSelection.RefreshFromPlayerList();
        }
    }

    public int GetPlayerCount() => _networkManager?.Players.Count ?? 0;

    private void RefreshPlayerList()
    {
        // 1. Cleanup disconnected players from our state
        var activeIds = _networkManager.Players.Keys.ToList();
        var keysToRemove = PlayerRoles.Keys.Where(k => !activeIds.Contains(k)).ToList();
        foreach (var k in keysToRemove)
        {
            PlayerRoles.Remove(k);
            PlayerReady.Remove(k);
        }

        // 2. Count roles to detect duplicates (Conflicts)
        var roleCounts = new Dictionary<string, int>();
        foreach (var role in PlayerRoles.Values)
        {
            if (!string.IsNullOrEmpty(role))
            {
                if (roleCounts.ContainsKey(role)) roleCounts[role]++;
                else roleCounts[role] = 1;
            }
        }

        bool hasRoleConflict = roleCounts.Values.Any(count => count > 1);
        bool everyoneReady = true;
        int playerCount = activeIds.Count;

        // Detect joins/leaves
        foreach (var knownPlayer in _knownPlayers)
        {
            if (!_networkManager.Players.ContainsKey(knownPlayer.Key) && !_isInitialLoad)
                ShowStatusMessage($"{knownPlayer.Value} left!", new Color("#F44336"));
        }
        foreach (var player in _networkManager.Players)
        {
            if (!_knownPlayers.ContainsKey(player.Key) && !_isInitialLoad)
                ShowStatusMessage($"{player.Value} joined!", new Color("#4CAF50"));
        }

        _knownPlayers.Clear();
        foreach (var player in _networkManager.Players) _knownPlayers.Add(player.Key, player.Value);

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

                PlayerRoles.TryGetValue(peerId, out string pRole);
                PlayerReady.TryGetValue(peerId, out bool pReady);

                if (!pReady) everyoneReady = false;

                bool isConflict = !string.IsNullOrEmpty(pRole) && roleCounts.ContainsKey(pRole) && roleCounts[pRole] > 1;

                if (nameLabel != null) nameLabel.Text = playerName;

                // Set dynamic status text and color
                if (statusLabel != null)
                {
                    if (isConflict)
                    {
                        statusLabel.Text = $"Conflict: {pRole}!";
                        statusLabel.AddThemeColorOverride("font_color", new Color("#F44336")); // Red
                    }
                    else if (pReady)
                    {
                        statusLabel.Text = $"Ready ({pRole})";
                        statusLabel.AddThemeColorOverride("font_color", new Color("#4CAF50")); // Green
                    }
                    else if (!string.IsNullOrEmpty(pRole))
                    {
                        statusLabel.Text = $"Selected: {pRole}";
                        statusLabel.RemoveThemeColorOverride("font_color"); // Default white
                    }
                    else
                    {
                        statusLabel.Text = "Selecting...";
                        statusLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.7f));
                    }
                }

                if (avatarBtn != null)
                {
                    if (avatarBtn.TextureNormal == null && DefaultAvatarTexture != null)
                        avatarBtn.TextureNormal = DefaultAvatarTexture;

                    bool isLocalPlayer = (peerId == localPeerId);
                    avatarBtn.Disabled = !isLocalPlayer;

                    // APPLY RED OUTLINE/TINT IF CONFLICT
                    if (isConflict)
                    {
                        avatarBtn.Modulate = new Color("#F44336"); // Red
                    }
                    else
                    {
                        avatarBtn.Modulate = isLocalPlayer ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0.7f);
                    }

                    var clickCallable = Callable.From(OnLocalAvatarClicked);
                    if (avatarBtn.IsConnected(BaseButton.SignalName.Pressed, clickCallable))
                        avatarBtn.Disconnect(BaseButton.SignalName.Pressed, clickCallable);

                    if (isLocalPlayer) avatarBtn.Connect(BaseButton.SignalName.Pressed, clickCallable);
                }
            }
            else
            {
                if (nameLabel != null) nameLabel.Text = "Open Slot";
                if (statusLabel != null)
                {
                    statusLabel.Text = "Waiting...";
                    statusLabel.RemoveThemeColorOverride("font_color");
                }
                if (avatarBtn != null)
                {
                    avatarBtn.Disabled = true;
                    avatarBtn.Modulate = new Color(1, 1, 1, 1);
                    var clickCallable = Callable.From(OnLocalAvatarClicked);
                    if (avatarBtn.IsConnected(BaseButton.SignalName.Pressed, clickCallable))
                        avatarBtn.Disconnect(BaseButton.SignalName.Pressed, clickCallable);
                }
            }
        }

        // --- START GAME BUTTON LOGIC ---
        if (_btnStartGame != null)
        {
            bool enoughPlayers = playerCount > 2; // Needs at least 2 players to start

            // Disable if not enough players, not everyone is ready, or there is a duplicate role
            _btnStartGame.Disabled = !everyoneReady || hasRoleConflict || !enoughPlayers;

            if (!enoughPlayers)
                _btnStartGame.Text = "Waiting for players...";
            else if (!everyoneReady)
                _btnStartGame.Text = "Waiting for ready...";
            else
                _btnStartGame.Text = "Start Game";
        }
    }

    private async void ShowStatusMessage(string message, Color color)
    {
        if (_lblStatus == null) return;
        _lblStatus.Text = message;
        _lblStatus.AddThemeColorOverride("font_color", color);
        _lblStatus.Visible = true;
        await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
        if (GodotObject.IsInstanceValid(_lblStatus) && _lblStatus.Text == message) _lblStatus.Text = "";
    }

    public void CheckStartConditionsAndNotify()
    {
        int playerCount = GetPlayerCount();

        // Count the roles to check for duplicates
        var roleCounts = new Dictionary<string, int>();
        foreach (var role in PlayerRoles.Values)
        {
            if (!string.IsNullOrEmpty(role))
            {
                if (roleCounts.ContainsKey(role)) roleCounts[role]++;
                else roleCounts[role] = 1;
            }
        }

        bool hasRoleConflict = roleCounts.Values.Any(count => count > 1);

        // Display the correct warning in lblStatus
        if (playerCount < 2)
        {
            ShowStatusMessage("Waiting for more players to join...", new Color("#FF9800")); // Orange Warning
        }
        else if (hasRoleConflict)
        {
            ShowStatusMessage("Cannot start: Role conflict detected!", new Color("#F44336")); // Red Warning
        }
        else
        {
            ShowStatusMessage("You are ready!", new Color("#4CAF50")); // Green Success
        }
    }

    private void OnLeaveButtonPressed()
    {
        var confirmDialog = new ConfirmationDialog
        {
            Title = "Leave Studio",
            DialogText = "Are you sure you want to leave the studio?",
            OkButtonText = "Leave",
            CancelButtonText = "Stay"
        };
        AddChild(confirmDialog);
        confirmDialog.PopupCentered();
        confirmDialog.Confirmed += () => { ConfirmLeave(); confirmDialog.QueueFree(); };
        confirmDialog.Canceled += () => confirmDialog.QueueFree();
    }

    private void ConfirmLeave()
    {
        _networkManager?.DisconnectFromRoom();
        GetTree().ChangeSceneToFile("res://scenes/lobby.tscn");
    }

    private void OnLocalAvatarClicked() => OpenRoleSelection();

    private void OpenRoleSelection()
    {
        if (HasNode("RoleSelectionOverlay")) return;

        var roleSelectScene = GD.Load<PackedScene>("res://scenes/RoleSelect/role_selection.tscn");
        if (roleSelectScene != null)
        {
            var roleSelectOverlay = roleSelectScene.Instantiate<Control>();
            roleSelectOverlay.Name = "RoleSelectionOverlay";
            AddChild(roleSelectOverlay);
            roleSelectOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
            roleSelectOverlay.Size = GetViewportRect().Size;
            roleSelectOverlay.GlobalPosition = Vector2.Zero;
        }
    }

    private void OnRoomTerminated(string reason) => GetTree().ChangeSceneToFile("res://scenes/lobby.tscn");
    private void OnStartGamePressed() => Rpc(MethodName.StartGameRpc);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StartGameRpc() => GetTree().ChangeSceneToFile("res://scenes/main_game.tscn");

    public override void _ExitTree()
    {
        if (_btnStartGame != null && GodotObject.IsInstanceValid(_btnStartGame)) _btnStartGame.Pressed -= OnStartGamePressed;
        if (_btnLeave != null && GodotObject.IsInstanceValid(_btnLeave)) _btnLeave.Pressed -= OnLeaveButtonPressed;
        if (_networkManager != null && GodotObject.IsInstanceValid(_networkManager))
        {
            _networkManager.PlayerListUpdated -= RefreshPlayerList;
            _networkManager.RoomTerminated -= OnRoomTerminated;
        }
    }
}