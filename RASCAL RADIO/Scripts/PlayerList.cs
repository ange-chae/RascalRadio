using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class PlayerList : Control
{
    // --- LEVEL DATA SETUP ---
    public class LevelData
    {
        public string Name;
        public string LevelId;
    }

    private List<LevelData> _availableLevels = new List<LevelData>()
    {
        new LevelData { Name = "Tutorial", LevelId = "tutorial" },
        new LevelData { Name = "01: Morning Broadcast", LevelId = "morning_broadcast" },
        new LevelData { Name = "02: Afternoon Broadcast", LevelId = "afternoon_broadcast" },
        new LevelData { Name = "03: EMERGENCY BROADCAST!", LevelId = "emergency_broadcast" }
    };
    private int _currentLevelIndex = 0;
    // -------------------------

    private NetworkManager _networkManager;

    private Label _lblRoomName;
    private LineEdit _txtRoomCode;
    private Container _playerSlotsContainer;
    private Button _btnStartGame;
    private Button _btnLeave;
    private Label _lblStatus;

    private BaseButton _btnPrev;
    private BaseButton _btnNext;
    private Label _lblLevelName;

    private readonly Dictionary<long, string> _knownPlayers = new Dictionary<long, string>();
    private bool _isInitialLoad = true;

    public Dictionary<long, string> PlayerRoles = new Dictionary<long, string>();
    public Dictionary<long, bool> PlayerReady = new Dictionary<long, bool>();

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

        _btnPrev = FindChild("btnPrev", true, false) as BaseButton;
        _btnNext = FindChild("btnNext", true, false) as BaseButton;
        _lblLevelName = FindChild("LblLevelName", true, false) as Label;

        if (_networkManager == null) return;

        if (_txtRoomCode != null)
        {
            _txtRoomCode.Editable = false;
            _txtRoomCode.Text = _networkManager.RoomCode;
            _txtRoomCode.FocusEntered += () =>
            {
                _txtRoomCode.CallDeferred(LineEdit.MethodName.SelectAll);
                DisplayServer.ClipboardSet(_networkManager.RoomCode);
                ShowStatusMessage("Room code copied!", new Color("#523729"));
            };
        }

        if (_lblRoomName != null)
            _lblRoomName.Text = string.IsNullOrEmpty(_networkManager.RoomName) ? "Lobby" : _networkManager.RoomName;

        if (_btnStartGame != null)
        {
            // FIX: Removed the code that turns the button invisible.
            // It will always stay physical to preserve your layout.
            _btnStartGame.Pressed += OnStartGamePressed;
        }

        if (_btnLeave != null) _btnLeave.Pressed += OnLeaveButtonPressed;

        if (Multiplayer.IsServer())
        {
            if (_btnPrev != null) _btnPrev.Pressed += OnPrevPressed;
            if (_btnNext != null) _btnNext.Pressed += OnNextPressed;
        }
        else
        {
            if (_btnPrev != null) _btnPrev.Visible = false;
            if (_btnNext != null) _btnNext.Visible = false;
        }

        _networkManager.PlayerListUpdated += RefreshPlayerList;
        _networkManager.RoomTerminated += OnRoomTerminated;

        RefreshPlayerList();

        UpdateLevelVisualLocal(_currentLevelIndex);
        _isInitialLoad = false;

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

    private void OnPrevPressed() => ChangeLevel(-1);
    private void OnNextPressed() => ChangeLevel(1);

    private void ChangeLevel(int direction)
    {
        int newIndex = _currentLevelIndex + direction;

        if (newIndex < 0) newIndex = _availableLevels.Count - 1;
        if (newIndex >= _availableLevels.Count) newIndex = 0;

        Rpc(MethodName.UpdateTapeVisualRpc, newIndex);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void UpdateTapeVisualRpc(int newIndex)
    {
        UpdateLevelVisualLocal(newIndex);
    }

    private void UpdateLevelVisualLocal(int newIndex)
    {
        _currentLevelIndex = newIndex;
        if (_lblLevelName != null)
        {
            _lblLevelName.Text = _availableLevels[_currentLevelIndex].Name;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void UpdatePlayerStateRpc(long peerId, string role, bool isReady)
    {
        long senderId = Multiplayer.GetRemoteSenderId();
        if (senderId != 0 && senderId != peerId) return;

        PlayerRoles[peerId] = role;
        PlayerReady[peerId] = isReady;

        RefreshPlayerList();

        var roleSelection = GetNodeOrNull<RoleSelection>("RoleSelectionOverlay");
        if (roleSelection != null)
        {
            roleSelection.RefreshFromPlayerList();
        }
    }
    public void RandomizeAllRoles()
    {
        if (!Multiplayer.IsServer()) return;

        var rng = new Random();
        var roles = new List<string> { "Broadcaster", "RadioTechnician", "RadioOperator" }
            .OrderBy(_ => rng.Next()).ToList();

        var ids = _networkManager.Players.Keys.ToArray();
        var assigned = new string[ids.Length];
        for (int i = 0; i < ids.Length; i++) assigned[i] = roles[i % roles.Count];

        Rpc(MethodName.ApplyRandomRolesRpc, ids, assigned);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ApplyRandomRolesRpc(long[] peerIds, string[] roles)
    {
        for (int i = 0; i < peerIds.Length; i++)
        {
            PlayerRoles[peerIds[i]] = roles[i];
            PlayerReady[peerIds[i]] = false;   // roles changed, so everyone re-confirms
        }
        RefreshPlayerList();
        GetNodeOrNull<RoleSelection>("RoleSelectionOverlay")?.RefreshFromPlayerList();
    }

    public int GetPlayerCount() => _networkManager?.Players.Count ?? 0;

    private void RefreshPlayerList()
    {
        if (_networkManager == null) return;

        if (Multiplayer.IsServer())
        {
            Rpc(MethodName.UpdateTapeVisualRpc, _currentLevelIndex);
        }

        var activeIds = _networkManager.Players.Keys.ToList();
        var keysToRemove = PlayerRoles.Keys.Where(k => !activeIds.Contains(k)).ToList();
        foreach (var k in keysToRemove)
        {
            PlayerRoles.Remove(k);
            PlayerReady.Remove(k);
        }

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

        foreach (var knownPlayer in _knownPlayers)
        {
            if (!_networkManager.Players.ContainsKey(knownPlayer.Key) && !_isInitialLoad)
                ShowStatusMessage($"{knownPlayer.Value} left!", new Color("#F44336"));
        }
        foreach (var player in _networkManager.Players)
        {
            if (!_networkManager.Players.ContainsKey(player.Key) && !_isInitialLoad)
                ShowStatusMessage($"{player.Value} joined!", new Color("#2E7D32"));
        }

        _knownPlayers.Clear();
        foreach (var player in _networkManager.Players) _knownPlayers.Add(player.Key, player.Value);

        if (_playerSlotsContainer != null)
        {
            var children = _playerSlotsContainer.GetChildren();
            var playerList = new List<KeyValuePair<long, string>>(_networkManager.Players);
            long localPeerId = Multiplayer.GetUniqueId();
            int playerIndex = 0;
            Callable clickCallable = new Callable(this, MethodName.OnLocalAvatarClicked);

            foreach (Node child in children)
            {
                if (child is not Control slotNode) continue;

                var avatarBtn = slotNode.FindChild("btnAvatar", true, false) as TextureButton;
                var nameLabel = slotNode.FindChild("lblPlayerName", true, false) as Label;
                var statusLabel = slotNode.FindChild("lblRoleStatus", true, false) as Label;

                if (avatarBtn == null && nameLabel == null && statusLabel == null) continue;

                if (playerIndex < playerList.Count)
                {
                    long peerId = playerList[playerIndex].Key;
                    string playerName = playerList[playerIndex].Value;

                    PlayerRoles.TryGetValue(peerId, out string pRole);
                    PlayerReady.TryGetValue(peerId, out bool pReady);

                    if (!pReady) everyoneReady = false;

                    bool isConflict = !string.IsNullOrEmpty(pRole) && roleCounts.ContainsKey(pRole) && roleCounts[pRole] > 1;

                    if (nameLabel != null) nameLabel.Text = playerName;

                    if (statusLabel != null)
                    {
                        if (isConflict)
                        {
                            statusLabel.Text = $"Conflict: {pRole}!";
                            statusLabel.AddThemeColorOverride("font_color", new Color("#F44336"));
                        }
                        else if (pReady)
                        {
                            statusLabel.Text = $"Ready ({pRole})";
                            statusLabel.AddThemeColorOverride("font_color", new Color("#2E7D32"));
                        }
                        else if (!string.IsNullOrEmpty(pRole))
                        {
                            statusLabel.Text = $"Selected: {pRole}";
                            statusLabel.AddThemeColorOverride("font_color", new Color("#523729"));
                        }
                        else
                        {
                            statusLabel.Text = "Selecting...";
                            statusLabel.AddThemeColorOverride("font_color", new Color("#523729"));
                        }
                    }

                    if (avatarBtn != null)
                    {
                        if (avatarBtn.TextureNormal == null && DefaultAvatarTexture != null)
                            avatarBtn.TextureNormal = DefaultAvatarTexture;

                        bool isLocalPlayer = (peerId == localPeerId);
                        avatarBtn.Disabled = !isLocalPlayer;

                        if (isConflict) avatarBtn.Modulate = new Color("#F44336");
                        else avatarBtn.Modulate = isLocalPlayer ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0.7f);

                        if (avatarBtn.IsConnected(BaseButton.SignalName.Pressed, clickCallable))
                            avatarBtn.Disconnect(BaseButton.SignalName.Pressed, clickCallable);

                        if (isLocalPlayer) avatarBtn.Connect(BaseButton.SignalName.Pressed, clickCallable);
                    }
                    playerIndex++;
                }
                else
                {
                    if (nameLabel != null) nameLabel.Text = "Open Slot";
                    if (statusLabel != null)
                    {
                        statusLabel.Text = "Waiting...";
                        statusLabel.AddThemeColorOverride("font_color", new Color("#523729"));
                    }
                    if (avatarBtn != null)
                    {
                        avatarBtn.Disabled = true;
                        avatarBtn.Modulate = new Color(1, 1, 1, 1);
                        if (avatarBtn.IsConnected(BaseButton.SignalName.Pressed, clickCallable))
                            avatarBtn.Disconnect(BaseButton.SignalName.Pressed, clickCallable);
                    }
                }
            }
        }

        // FIX: The button is no longer invisible for clients. Instead, it is greyed out.
        if (_btnStartGame != null)
        {
            if (Multiplayer.IsServer())
            {
                bool enoughPlayers = playerCount >= 2;

                _btnStartGame.Disabled = !everyoneReady || hasRoleConflict || !enoughPlayers;

                if (!enoughPlayers)
                    _btnStartGame.Text = "Waiting..";
                else if (!everyoneReady)
                    _btnStartGame.Text = "Not Ready";
                else
                    _btnStartGame.Text = "Start Game";
            }
            else
            {
                _btnStartGame.Disabled = true;
                _btnStartGame.Text = "Waiting..";
            }
        }
    }

    private CanvasLayer _toastLayer;

    private void ShowStatusMessage(string message, Color color)
    {
        if (IsInstanceValid(_toastLayer)) _toastLayer.QueueFree();

        _toastLayer = new CanvasLayer { Layer = 50 };
        AddChild(_toastLayer);

        var toast = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        toast.AddThemeStyleboxOverride("panel", UiStyle.Card());

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        toast.AddChild(row);
        row.AddChild(new ColorRect { Color = color, CustomMinimumSize = new Vector2(8, 0) });

        var label = new Label { Text = message };
        label.AddThemeColorOverride("font_color", UiStyle.Ink);
        row.AddChild(label);

        _toastLayer.AddChild(toast);
        toast.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop, Control.LayoutPresetMode.Minsize, 24);
        toast.GrowHorizontal = Control.GrowDirection.Both;

        var tween = toast.CreateTween();
        tween.TweenProperty(toast, "modulate:a", 1.0f, 0.2f);
        tween.TweenInterval(2.5);
        tween.TweenProperty(toast, "modulate:a", 0.0f, 0.4f);
        tween.TweenCallback(Callable.From(_toastLayer.QueueFree));
    }

    public void CheckStartConditionsAndNotify()
    {
        int playerCount = GetPlayerCount();

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

        if (playerCount < 2)
        {
            ShowStatusMessage("Waiting for more players to join...", new Color("#FF9800"));
        }
        else if (hasRoleConflict)
        {
            ShowStatusMessage("Cannot start: Role conflict detected!", new Color("#F44336"));
        }
        else
        {
            ShowStatusMessage("You are ready!", new Color("#2E7D32"));
        }
    }

    private void OnLeaveButtonPressed()
    {
        ConfirmPopup.Show(this, "Leave Studio",
            Multiplayer.IsServer() ? "You're the host. Leaving ends the studio for everyone."
                                   : "Are you sure you want to leave the studio?",
            "Leave", "Stay", ConfirmLeave);
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

    private void OnStartGamePressed()
    {
        if (!Multiplayer.IsServer()) return;

        Rpc(MethodName.StartGameRpc, _currentLevelIndex);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StartGameRpc(int selectedLevelIndex)
    {
        if (selectedLevelIndex >= 0 && selectedLevelIndex < _availableLevels.Count)
            GameSession.Instance.BeginLevel(_availableLevels[selectedLevelIndex].LevelId);

        long myPeerId = Multiplayer.GetUniqueId();

        if (PlayerRoles.TryGetValue(myPeerId, out string myRole))
        {
            string sceneToLoad = "";

            // Standardize and match the exact role names coming from your UI
            switch (myRole.Trim().ToLower())
            {
                case "radiotechnician":
                case "technician":
                    sceneToLoad = "res://scenes/technician_interface.tscn";
                    break;

                case "radiooperator":
                case "radio operator":
                    sceneToLoad = "res://scenes/operator_interface.tscn";
                    break;
                case "broadcaster":
                    sceneToLoad = "res://scenes/broadcaster_interface.tscn";
                    break;
            }

            if (!string.IsNullOrEmpty(sceneToLoad))
            {
                CallDeferred(MethodName.LoadSceneDeferred, sceneToLoad);
            }
            else
            {
                GD.PrintErr($"Could not find scene mapping for role: {myRole}");
            }
        }
    }

    // Helper method called by CallDeferred above
    private void LoadSceneDeferred(string scenePath)
    {
        GetTree().ChangeSceneToFile(scenePath);
    }

    public override void _ExitTree()
    {
        if (_btnStartGame != null && GodotObject.IsInstanceValid(_btnStartGame)) _btnStartGame.Pressed -= OnStartGamePressed;
        if (_btnLeave != null && GodotObject.IsInstanceValid(_btnLeave)) _btnLeave.Pressed -= OnLeaveButtonPressed;
        if (_btnPrev != null && GodotObject.IsInstanceValid(_btnPrev) && _btnPrev.IsConnected(BaseButton.SignalName.Pressed, Callable.From(OnPrevPressed))) _btnPrev.Pressed -= OnPrevPressed;
        if (_btnNext != null && GodotObject.IsInstanceValid(_btnNext) && _btnNext.IsConnected(BaseButton.SignalName.Pressed, Callable.From(OnNextPressed))) _btnNext.Pressed -= OnNextPressed;

        if (_networkManager != null && GodotObject.IsInstanceValid(_networkManager))
        {
            _networkManager.PlayerListUpdated -= RefreshPlayerList;
            _networkManager.RoomTerminated -= OnRoomTerminated;
        }
    }
}