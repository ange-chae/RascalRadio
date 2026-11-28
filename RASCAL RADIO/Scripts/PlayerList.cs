using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class PlayerList : Control
{
    // --- LEVEL DATA SETUP ---
    public class LevelData
    {
        public bool StartsLocked;   // needs an A or S on the level before it
        public string Name;
        public string LevelId;
        public string TapePath;   // cassette picture for this level
    }

    private List<LevelData> _availableLevels = new List<LevelData>()
{
	// Tutorial has no tape picture yet, so it borrows tape1 for now. Make a tape0 later and change this path.
	new LevelData { Name = "Tutorial",                LevelId = "tutorial",            TapePath = "res://assets/images/tape1.png" },
    new LevelData { Name = "01: Morning Broadcast",   LevelId = "morning_broadcast",   TapePath = "res://assets/images/tape2.png" },
    new LevelData { Name = "02: Afternoon Broadcast", LevelId = "afternoon_broadcast", TapePath = "res://assets/images/tape3.png", StartsLocked = true },
    new LevelData { Name = "03: EMERGENCY BROADCAST!", LevelId = "emergency_broadcast", TapePath = "res://assets/images/tape4.png", StartsLocked = true }
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
    private Button _btnReady;
    private BaseButton _btnPrev;
    private BaseButton _btnNext;
    private Label _lblLevelName;
    private Button _readyBtn;
    private const float ReadyGapBelowSlots = 6f;   // nudge up/down if it touches the hint text
    private CassetteSelector _cassette;

    private readonly Dictionary<long, string> _knownPlayers = new Dictionary<long, string>();
    private bool _isInitialLoad = true;

    private static readonly Dictionary<long, string> _savedRoles = new();
    public Dictionary<long, string> PlayerRoles = _savedRoles;
    public Dictionary<long, bool> PlayerReady = new Dictionary<long, bool>();

    [Export] public Texture2D DefaultAvatarTexture;

    // --- ROLE AVATAR IMAGES ---
    // Change the image paths here if you rename or move the pictures.
    // Keys must match the role names used in PlayerRoles.
    private static readonly Dictionary<string, string> RoleAvatarPaths = new()
        {
            { "Broadcaster",     "res://assets/images/broadcaster-pfp.png" },
            { "RadioTechnician", "res://assets/images/technician-pfp.png" },
            { "RadioOperator",   "res://assets/images/operator-pfp.png" },
        };

    private static readonly Color BtnInk = new Color(0.1254902f, 0.09019608f, 0.25490198f);
    private const int ReadyBorder = 3;          // was 3
    private const int ReadyBottomBorder = 6;
    private Texture2D GetAvatarForRole(string role)
    {
        if (!string.IsNullOrEmpty(role) && RoleAvatarPaths.TryGetValue(role, out string path))
        {
            var tex = GD.Load<Texture2D>(path);
            if (tex != null) return tex;
        }
        var defaultTex = GD.Load<Texture2D>("res://assets/images/PLAYERS ID.png");
        return defaultTex ?? DefaultAvatarTexture;
    }
    private static int _savedLevelIndex = 0;   // survives going into a level and coming back

    private bool IsLevelLocked(int index)
    {
        if (!GameSession.LevelLockingEnabled) { GD.Print("[Lock] locking is OFF"); return false; }
        if (index <= 0 || index >= _availableLevels.Count) return false;
        if (!_availableLevels[index].StartsLocked) { GD.Print($"[Lock] {_availableLevels[index].Name}: StartsLocked is false"); return false; }

        var gs = GameSession.Instance;
        string prev = _availableLevels[index - 1].LevelId;
        bool cleared = gs != null && gs.IsLevelCleared(prev);
        GD.Print($"[Lock] {_availableLevels[index].Name}: previous '{prev}' cleared = {cleared}");
        return !cleared;
    }

    public override void _Ready()
    {
        _networkManager = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
        var playerListBgm = GetNodeOrNull<AudioStreamPlayer>("PlayerListBgm");
        if (playerListBgm?.Stream is AudioStreamMP3 lobbyTrack)
        {
            lobbyTrack.Loop = true;
            playerListBgm.Play();
        }

        _lblRoomName = FindChild("lblRoomName", true, false) as Label;
        _txtRoomCode = FindChild("txtRoomCode", true, false) as LineEdit;
        _playerSlotsContainer = FindChild("playerSlotsContainer", true, false) as Container;
        _btnStartGame = FindChild("btnStartGame", true, false) as Button;
        _btnLeave = FindChild("btnLeave", true, false) as Button;
        _btnReady = FindChild("ReadyBtn", true, false) as Button;
        _lblStatus = FindChild("lblStatus", true, false) as Label;

        _btnPrev = FindChild("btnPrev", true, false) as BaseButton;
        _btnNext = FindChild("btnNext", true, false) as BaseButton;
        _lblLevelName = FindChild("LblLevelName", true, false) as Label;
        _cassette = FindChildren("*", "VBoxContainer", true, false).OfType<CassetteSelector>().FirstOrDefault();

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
            _btnStartGame.Pressed += OnStartGamePressed;
        }

        if (_btnLeave != null) _btnLeave.Pressed += OnLeaveButtonPressed;
        if (_btnReady != null) _btnReady.Pressed += OnLocalReadyClicked;

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
        if (GameSession.Instance != null) GameSession.Instance.ProgressChanged += OnProgressChanged;

        RefreshPlayerList();
        if (!Multiplayer.IsServer())
            GetTree().CreateTimer(0.6).Timeout += () =>
            {
                if (IsInstanceValid(this) && IsInsideTree()) RpcId(1, MethodName.RequestStateRpc);
            };
        _currentLevelIndex = Mathf.Clamp(_savedLevelIndex, 0, _availableLevels.Count - 1);
        UpdateLevelVisualLocal(_currentLevelIndex);
        _isInitialLoad = false;

        // Someone left in the middle of a game: the game sent us back here, so say so.
        var gs = GameSession.Instance;
        if (gs != null && !string.IsNullOrEmpty(gs.LobbyNotice))
        {
            string notice = gs.LobbyNotice;
            gs.LobbyNotice = "";
            GetTree().CreateTimer(0.5).Timeout += () =>   // after the fade-in from black
            {
                if (!IsInstanceValid(this) || !IsInsideTree()) return;
                ShowStatusMessage(notice, new Color("#F44336"));
                Sfx.Play(this, "cancel_sfx", -8f);
            };
        }

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

        if (IsLevelLocked(newIndex))
            ShowStatusMessage($"Locked! Get an A or S on {_availableLevels[newIndex - 1].Name} first.", new Color("#FF9800"));

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
        _savedLevelIndex = newIndex;     // not 0
        var level = _availableLevels[_currentLevelIndex];

        bool locked = IsLevelLocked(newIndex);

        var tape = ResourceLoader.Exists(level.TapePath) ? GD.Load<Texture2D>(level.TapePath) : null;
        _cassette?.UpdateTapeVisual(level.Name, tape);
        if (_cassette != null) _cassette.Modulate = locked ? new Color(0.45f, 0.45f, 0.45f) : Colors.White;   // delete this line if you don't want the dimming

        RefreshPlayerList();   // re-checks the Start Game button for this level
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
            PlayerRoles[peerIds[i]] = roles[i];   // ready state is kept

        ShowStatusMessage(
            Multiplayer.IsServer() ? "You randomized the roles!" : "The host randomized the roles!",
            new Color("#FF9800"));

        RefreshPlayerList();
        GetNodeOrNull<RoleSelection>("RoleSelectionOverlay")?.RefreshFromPlayerList();
    }

    // --- LATE JOIN SYNC ---
    // A newly joined player asks the host for everyone's current roles/ready states.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestStateRpc()
    {
        if (!Multiplayer.IsServer()) return;

        long requester = Multiplayer.GetRemoteSenderId();

        var ids = PlayerRoles.Keys.Union(PlayerReady.Keys).ToArray();
        var roles = ids.Select(id => PlayerRoles.TryGetValue(id, out var r) ? r : "").ToArray();
        var ready = ids.Select(id => (PlayerReady.TryGetValue(id, out var b) && b) ? 1 : 0).ToArray();
        GameSession.Instance?.SendProgressTo(requester);
        RpcId(requester, MethodName.SyncAllStateRpc, ids, roles, ready, _currentLevelIndex);
    }

    // Host -> the new player only. Fills in everything they missed.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncAllStateRpc(long[] peerIds, string[] roles, int[] ready, int levelIndex)
    {
        for (int i = 0; i < peerIds.Length; i++)
        {
            PlayerRoles[peerIds[i]] = roles[i];
            PlayerReady[peerIds[i]] = ready[i] == 1;
        }
        UpdateLevelVisualLocal(levelIndex);
        RefreshPlayerList();
        GetNodeOrNull<RoleSelection>("RoleSelectionOverlay")?.RefreshFromPlayerList();
    }

    public int GetPlayerCount() => _networkManager?.Players.Count ?? 0;

    private void RefreshPlayerList()
    {
        if (_networkManager == null) return;

        //if (Multiplayer.IsServer())
        //{
        //    Rpc(MethodName.UpdateTapeVisualRpc, _currentLevelIndex);
        //}

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
            {
                ShowStatusMessage($"{knownPlayer.Value} left!", new Color("#F44336"));
                Sfx.Play(this, "cancel_sfx", -8f); // someone left
            }
        }
        foreach (var player in _networkManager.Players)
        {
            if (!_knownPlayers.ContainsKey(player.Key) && !_isInitialLoad)
            {
                ShowStatusMessage($"{player.Value} joined!", new Color("#2E7D32"));
                Sfx.Play(this, "blip_sfx", -6f); // someone joined
            }
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

                    if (nameLabel != null)
                        nameLabel.Text = (peerId == localPeerId) ? $"{playerName} (you)" : playerName;

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
                        avatarBtn.TextureNormal = GetAvatarForRole(pRole);

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
                        avatarBtn.TextureNormal = GetAvatarForRole(null);   // NEW: back to the default icon
                        avatarBtn.Disabled = true;
                        avatarBtn.Modulate = new Color(1, 1, 1, 1);
                        if (avatarBtn.IsConnected(BaseButton.SignalName.Pressed, clickCallable))
                            avatarBtn.Disconnect(BaseButton.SignalName.Pressed, clickCallable);
                    }

                    var openReadyBtn = slotNode.FindChild("btnReady", true, false) as Button;
                    if (openReadyBtn != null) openReadyBtn.Visible = false;
                }
            }
        }

        // FIX: The button is no longer invisible for clients. Instead, it is greyed out.
        if (_btnStartGame != null)
        {
            if (Multiplayer.IsServer())
            {
                bool enoughPlayers = playerCount >= 2;
                bool levelLocked = IsLevelLocked(_currentLevelIndex);
                _btnStartGame.Disabled = !everyoneReady || hasRoleConflict || !enoughPlayers || levelLocked;

                if (levelLocked) _btnStartGame.Text = "Locked";
                else if (!enoughPlayers) _btnStartGame.Text = "Waiting..";
                else if (!everyoneReady) _btnStartGame.Text = "Not Ready";
                else _btnStartGame.Text = "Start Game";
            }
            else
            {
                _btnStartGame.Disabled = true;
                _btnStartGame.Text = "Waiting..";
            }
        }

        RefreshReadyButton();
    }

    private CanvasLayer _toastLayer;

    private void ShowStatusMessage(string message, Color color)
    {
        if (IsInstanceValid(_toastLayer)) _toastLayer.QueueFree();

        _toastLayer = new CanvasLayer { Layer = 50 };
        AddChild(_toastLayer);

        var toast = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        StyleBox cardStyle = UiStyle.Card();
        if (cardStyle.Duplicate() is StyleBoxFlat thinCard)
        {
            thinCard.BorderWidthLeft = Mathf.Min(thinCard.BorderWidthLeft, 2);
            thinCard.BorderWidthTop = Mathf.Min(thinCard.BorderWidthTop, 2);
            thinCard.BorderWidthRight = Mathf.Min(thinCard.BorderWidthRight, 2);
            thinCard.BorderWidthBottom = Mathf.Min(thinCard.BorderWidthBottom, 3);
            cardStyle = thinCard;
        }
        toast.AddThemeStyleboxOverride("panel", cardStyle);

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
        Sfx.Play(this, "cancel_sfx", -8f); // left sound
        _savedRoles.Clear();
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

    // Centers the button under the player slots, on the paper
    private void PositionReadyButton()
    {
        if (!IsInstanceValid(_readyBtn)) return;

        Vector2 size = _readyBtn.GetCombinedMinimumSize();
        _readyBtn.Size = size;
        _readyBtn.PivotOffset = size / 2f;

        Vector2 viewport = GetViewportRect().Size;
        Vector2 pos;

        if (_playerSlotsContainer != null)
        {
            Rect2 box = _playerSlotsContainer.GetGlobalRect();

            // Use the bottom of the last real slot, not the container, since the container may stretch past the screen
            float bottom = box.Position.Y;
            foreach (Node child in _playerSlotsContainer.GetChildren())
                if (child is Control c && c.Visible)
                    bottom = Mathf.Max(bottom, c.GetGlobalRect().End.Y);

            pos = new Vector2(
                box.Position.X + (box.Size.X - size.X) / 2f,
                bottom + ReadyGapBelowSlots);
        }
        else
        {
            pos = new Vector2((viewport.X - size.X) / 2f, viewport.Y - size.Y - 40);
        }

        // Never let it leave the window
        pos.X = Mathf.Clamp(pos.X, 8, viewport.X - size.X - 8);
        pos.Y = Mathf.Clamp(pos.Y, 8, viewport.Y - size.Y - 8);
        _readyBtn.Position = pos;

        GD.Print($"[ReadyBtn] pos={pos} size={size} viewport={viewport}");
    }
    private void OnLocalReadyClicked()
    {
        long me = Multiplayer.GetUniqueId();
        PlayerRoles.TryGetValue(me, out string role);
        PlayerReady.TryGetValue(me, out bool ready);

        if (string.IsNullOrEmpty(role))
        {
            ShowStatusMessage("Pick a role first! Click your icon.", new Color("#FF9800"));
            return;
        }

        bool conflict = PlayerRoles.Values.Count(r => r == role) > 1;
        if (conflict && !ready)
        {
            ShowStatusMessage("Someone already has that role!", new Color("#F44336"));
            return;
        }

        bool nowReady = !ready;
        Rpc(MethodName.UpdatePlayerStateRpc, me, role, nowReady);
        Sfx.Play(this, nowReady ? "confirm_sfx" : "cancel_sfx", -6f);
        if (nowReady) CheckStartConditionsAndNotify();
    }

    private void RefreshReadyButton()
    {
        if (_btnReady == null) return;
        PlayerReady.TryGetValue(Multiplayer.GetUniqueId(), out bool ready);
        _btnReady.Text = ready ? "UNREADY" : "READY";
    }

    private void OnRoomTerminated(string reason)
    {
        _savedRoles.Clear();
        GetTree().ChangeSceneToFile("res://scenes/lobby.tscn");
    }

    private void OnStartGamePressed()
    {
        if (!Multiplayer.IsServer()) return;
        if (IsLevelLocked(_currentLevelIndex)) return;
        Rpc(MethodName.StartGameRpc, _currentLevelIndex);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StartGameRpc(int selectedLevelIndex)
    {
        Sfx.Play(this, "confirm_sfx", -6f); // game is starting
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

    private void OnProgressChanged()
    {
        UpdateLevelVisualLocal(_currentLevelIndex);
        RefreshPlayerList();
    }

    public override void _ExitTree()
    {
        if (GameSession.Instance != null) GameSession.Instance.ProgressChanged -= OnProgressChanged;
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