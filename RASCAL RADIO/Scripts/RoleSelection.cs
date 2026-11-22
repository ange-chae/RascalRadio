using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RascalRadio.Core.Models;

public partial class RoleSelection : Control
{
    private PlayerList _playerList;
    private int LocalPlayerId => (int)Multiplayer.GetUniqueId();

    // Add the image paths to the tuple
    private static readonly Dictionary<Role, (string Name, string Blurb, string ImagePath)> RoleInfo = new()
    {
        { Role.Broadcaster,     ("Broadcaster", "Runs the show. Picks the callers.", "res://assets/images/broadcaster.png") },
        { Role.RadioTechnician, ("Technician",  "Keeps the gear alive. Fixes what the raccoon breaks.", "res://assets/images/tech.png") },
        { Role.RadioOperator,   ("Operator",    "Sets the quota and keeps the schedule.", "res://assets/images/operator.png") },
    };

    private const string CardScenePath = "res://scenes/RoleSelect/role_card.tscn";
    private readonly Dictionary<Role, RoleCard> _cards = new();

    private HBoxContainer _cardsRow;
    private Button _randomizeButton;
    private Button _pickButton;
    private Button _backButton;
    private Label _statusLabel;

    // The role currently highlighted (always mirrors the network state).
    private string _selectedRole = "";

    // The role the player has locked in with Pick (or already had when this screen opened).
    // Pick stays disabled while _selectedRole == _pickedRole.
    private string _pickedRole = "";

    // Lobby ready state is owned by the lobby's own Ready button now.
    // We only carry it along so role changes don't overwrite it.
    private bool _isReady = false;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        GetNodeOrNull<Control>("Background")?.SetAnchorsPreset(LayoutPreset.FullRect);

        var margin = GetNodeOrNull<MarginContainer>("Margin");
        if (margin != null) margin.SetAnchorsPreset(LayoutPreset.FullRect);

        var mainContainer = GetNodeOrNull<VBoxContainer>("Margin/Main");
        if (mainContainer != null)
        {
            mainContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
            mainContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            mainContainer.Alignment = BoxContainer.AlignmentMode.Center;
        }

        _cardsRow = GetNode<HBoxContainer>("Margin/Main/CardsRow");
        if (_cardsRow != null)
        {
            _cardsRow.SizeFlagsVertical = SizeFlags.ExpandFill;
            _cardsRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _cardsRow.Alignment = BoxContainer.AlignmentMode.Center;
        }

        _randomizeButton = GetNode<Button>("Margin/Main/BottomBar/RandomizeButton");
        _pickButton = GetNode<Button>("Margin/Main/BottomBar/PickButton");
        _statusLabel = GetNodeOrNull<Label>("Margin/Main/BottomBar/StatusLabel");
        _backButton = GetNode<Button>("Margin/Main/TopBar/BackButton");

        _randomizeButton.Visible = Multiplayer.IsServer();

        foreach (Node child in _cardsRow.GetChildren()) child.QueueFree();

        // Get the parent lobby to handle networking
        _playerList = GetParent<PlayerList>();

        // If they already had a role when opening this screen, count it as picked
        // so the Pick button starts disabled until they choose something different.
        _playerList.PlayerRoles.TryGetValue(LocalPlayerId, out string existingRole);
        _pickedRole = existingRole ?? "";

        BuildCards();

        _randomizeButton.Pressed += OnRandomizePressed;
        _pickButton.Pressed += OnPickPressed;
        _backButton.Pressed += QueueFree;

        RefreshFromPlayerList();
    }

    // Esc closes the screen, same as the Back button.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            QueueFree();
        }
    }

    private void BuildCards()
    {
        var cardScene = GD.Load<PackedScene>(CardScenePath);

        foreach (Role role in RoleInfo.Keys)
        {
            var card = cardScene.Instantiate<RoleCard>();
            card.CustomMinimumSize = new Vector2(250, 400);
            _cardsRow.AddChild(card);

            var info = RoleInfo[role];

            Texture2D roleArt = GD.Load<Texture2D>(info.ImagePath);
            card.Setup(info.Name, info.Blurb, roleArt);

            string capturedRoleStr = role.ToString();

            card.SelectPressed += () =>
            {
                _selectedRole = capturedRoleStr;
                _playerList.Rpc(PlayerList.MethodName.UpdatePlayerStateRpc, LocalPlayerId, _selectedRole, _isReady);
                UpdatePickButton(); // enables Pick right away if this differs from the picked role
            };

            _cards[role] = card;
        }
    }

    private void OnRandomizePressed()
    {
        if (!Multiplayer.IsServer()) return;
        _playerList.RandomizeAllRoles();
    }

    private void OnPickPressed()
    {
        if (string.IsNullOrEmpty(_selectedRole)) return;

        _pickedRole = _selectedRole;
        _playerList.Rpc(PlayerList.MethodName.UpdatePlayerStateRpc, LocalPlayerId, _selectedRole, _isReady);

        UpdatePickButton(); // disables it
        QueueFree();        // close the screen
    }

    private void UpdatePickButton()
    {
        if (_pickButton == null) return;

        bool hasRole = !string.IsNullOrEmpty(_selectedRole);
        bool alreadyPicked = hasRole && _selectedRole == _pickedRole;

        _pickButton.Disabled = !hasRole || alreadyPicked;

        if (_statusLabel != null)
        {
            _statusLabel.Text = !hasRole ? "Pick a role to continue"
                : alreadyPicked ? "Role locked in"
                : "Press Pick to confirm";
        }
    }

    // Called automatically by PlayerList when network state updates
    public void RefreshFromPlayerList()
    {
        if (_playerList == null) return;

        // Fetch our latest state from the network dictionary
        _playerList.PlayerRoles.TryGetValue(LocalPlayerId, out _selectedRole);
        _selectedRole ??= "";
        _playerList.PlayerReady.TryGetValue(LocalPlayerId, out _isReady);

        // Count how many people have each role to find duplicates
        var roleCounts = new Dictionary<string, int>();
        foreach (var r in _playerList.PlayerRoles.Values)
        {
            if (!string.IsNullOrEmpty(r))
            {
                if (roleCounts.ContainsKey(r)) roleCounts[r]++;
                else roleCounts[r] = 1;
            }
        }

        foreach (var pair in _cards)
        {
            string rStr = pair.Key.ToString();
            bool isMine = _selectedRole == rStr;

            // Cards are never "Taken", they are always clickable
            pair.Value.SetState(isMine ? RoleCard.CardState.Mine : RoleCard.CardState.Available);

            // Turn the card red if there is a conflict
            if (roleCounts.ContainsKey(rStr) && roleCounts[rStr] > 1)
                pair.Value.Modulate = new Color("#F44336");
            else
                pair.Value.Modulate = new Color(1, 1, 1);
        }

        UpdatePickButton();
    }
}