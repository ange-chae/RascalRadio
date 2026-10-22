using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RascalRadio.Core.Models;

public partial class RoleSelection : Control
{
    private PlayerList _playerList;
    private int LocalPlayerId => (int)Multiplayer.GetUniqueId();

    private static readonly Dictionary<Role, (string Name, string Blurb)> RoleInfo = new()
    {
        { Role.Broadcaster,     ("Broadcaster", "Runs the show. Picks the callers.") },
        { Role.RadioTechnician, ("Technician",  "Keeps the gear alive. Fixes what the raccoon breaks.") },
        { Role.RadioOperator,   ("Operator",    "Sets the quota and keeps the schedule.") },
    };

    private const string CardScenePath = "res://scenes/RoleSelect/role_card.tscn";
    private readonly Dictionary<Role, RoleCard> _cards = new();

    private HBoxContainer _cardsRow;
    private Button _randomizeButton;
    private Button _readyButton;
    private Button _backButton;
    private Label _statusLabel;

    private string _selectedRole = "";
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
        _readyButton = GetNode<Button>("Margin/Main/BottomBar/ReadyButton");
        _statusLabel = GetNode<Label>("Margin/Main/BottomBar/StatusLabel");
        _backButton = GetNode<Button>("Margin/Main/TopBar/BackButton");

        foreach (Node child in _cardsRow.GetChildren()) child.QueueFree();

        // Get the parent lobby to handle networking
        _playerList = GetParent<PlayerList>();

        BuildCards();

        _randomizeButton.Pressed += OnRandomizePressed;
        _readyButton.Pressed += OnReadyPressed;
        _backButton.Pressed += QueueFree;

        RefreshFromPlayerList();
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
            card.Setup(info.Name, info.Blurb);

            string capturedRoleStr = role.ToString();

            // Send RPC when a card is clicked
            card.SelectPressed += () =>
            {
                _selectedRole = capturedRoleStr;
                _playerList.Rpc(PlayerList.MethodName.UpdatePlayerStateRpc, LocalPlayerId, _selectedRole, _isReady);
            };

            _cards[role] = card;
        }
    }

    private void OnRandomizePressed()
    {
        var roles = RoleInfo.Keys.ToList();
        _selectedRole = roles[new Random().Next(roles.Count)].ToString();
        _playerList.Rpc(PlayerList.MethodName.UpdatePlayerStateRpc, LocalPlayerId, _selectedRole, _isReady);
    }

    private void OnReadyPressed()
    {
        _isReady = !_isReady;
        _playerList.Rpc(PlayerList.MethodName.UpdatePlayerStateRpc, LocalPlayerId, _selectedRole, _isReady);

        // If the player just clicked "Ready", notify them of the status and close the overlay
        if (_isReady)
        {
            _playerList.CheckStartConditionsAndNotify();
            QueueFree();
        }
    }

    // Called automatically by PlayerList when network state updates
    public void RefreshFromPlayerList()
    {
        if (_playerList == null) return;

        // Fetch our latest state from the network dictionary
        _playerList.PlayerRoles.TryGetValue(LocalPlayerId, out _selectedRole);
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

            // Cards are NEVER "Taken" anymore, they are always "Available" so anyone can click them
            pair.Value.SetState(isMine ? RoleCard.CardState.Mine : RoleCard.CardState.Available);

            // Turn the card red if there is a conflict
            if (roleCounts.ContainsKey(rStr) && roleCounts[rStr] > 1)
            {
                pair.Value.Modulate = new Color("#F44336"); // Red tint
            }
            else
            {
                pair.Value.Modulate = new Color(1, 1, 1); // Normal
            }
        }

        bool hasRole = !string.IsNullOrEmpty(_selectedRole);
        _readyButton.Disabled = !hasRole;
        _readyButton.Text = _isReady ? "Not Ready" : "Ready";

        int readyCount = _playerList.PlayerReady.Values.Count(r => r);
        int totalPlayers = _playerList.GetPlayerCount();

        _statusLabel.Text = hasRole
            ? $"{readyCount}/{totalPlayers} ready"
            : "Pick a role to continue";
    }
}