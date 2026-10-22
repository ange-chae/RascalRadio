using System.Collections.Generic;
using System.Linq;
using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;

/// <summary>
/// Role selection screen connected to real NetworkManager player IDs.
/// </summary>
public partial class RoleSelection : Control
{
    private NetworkManager _networkManager;

    // Use real multiplayer peer ID
    private int LocalPlayerId => (int)Multiplayer.GetUniqueId();

    // All role text definitions
    private static readonly Dictionary<Role, (string Name, string Blurb)> RoleInfo = new()
    {
        { Role.Broadcaster,     ("Broadcaster", "Runs the show. Picks the callers.") },
        { Role.RadioTechnician, ("Technician",  "Keeps the gear alive. Fixes what the raccoon breaks.") },
        { Role.RadioOperator,   ("Operator",    "Sets the quota and keeps the schedule.") },
    };

    private const string CardScenePath = "res://scenes/RoleSelect/role_card.tscn";

    private RoleSelectionLogic _logic;
    private readonly Dictionary<Role, RoleCard> _cards = new();

    private HBoxContainer _cardsRow;
    private Button _randomizeButton;
    private Button _readyButton;
    private Button _backButton;
    private Label _statusLabel;

    public override void _Ready()
    {
        _networkManager = GetNodeOrNull<NetworkManager>("/root/NetworkManager");

        _cardsRow = GetNode<HBoxContainer>("Margin/Main/CardsRow");
        _randomizeButton = GetNode<Button>("Margin/Main/BottomBar/RandomizeButton");
        _readyButton = GetNode<Button>("Margin/Main/BottomBar/ReadyButton");
        _statusLabel = GetNode<Label>("Margin/Main/BottomBar/StatusLabel");
        _backButton = GetNode<Button>("Margin/Main/TopBar/BackButton");

        // Clean preview cards from the editor
        foreach (Node child in _cardsRow.GetChildren())
            child.QueueFree();

        // Initialize logic backend
        _logic = new RoleSelectionLogic(RoleInfo.Keys);
        _logic.SelectionChanged += Refresh;
        _logic.AllReady += OnAllReady;

        BuildCards();

        _randomizeButton.Pressed += () => _logic.Randomize();
        _readyButton.Pressed += OnReadyPressed;

        // Close overlay and return to room screen when Back is pressed
        _backButton.Pressed += OnBackPressed;

        // Populate real players from NetworkManager
        RegisterRealPlayers();

        Refresh();
    }

    private void RegisterRealPlayers()
    {
        if (_networkManager == null || _networkManager.Players == null)
        {
            // Fallback for standalone testing
            _logic.AddPlayer(LocalPlayerId);
            return;
        }

        foreach (var playerKey in _networkManager.Players.Keys)
        {
            _logic.AddPlayer((int)playerKey);
        }
    }

    private void BuildCards()
    {
        var cardScene = GD.Load<PackedScene>(CardScenePath);

        foreach (Role role in _logic.Roles)
        {
            var card = cardScene.Instantiate<RoleCard>();
            _cardsRow.AddChild(card);

            var info = RoleInfo[role];
            card.Setup(info.Name, info.Blurb);

            Role captured = role;
            card.SelectPressed += () => _logic.TrySelect(LocalPlayerId, captured);

            _cards[role] = card;
        }
    }

    private void Refresh()
    {
        foreach (var pair in _cards)
        {
            int? owner = _logic.GetPlayerWithRole(pair.Key);

            if (owner == null)
                pair.Value.SetState(RoleCard.CardState.Available);
            else if (owner == LocalPlayerId)
                pair.Value.SetState(RoleCard.CardState.Mine);
            else
                pair.Value.SetState(RoleCard.CardState.Taken, PlayerName(owner.Value));
        }

        bool hasRole = _logic.GetRole(LocalPlayerId) != null;
        bool isReady = _logic.IsReady(LocalPlayerId);

        _readyButton.Disabled = !hasRole;
        _readyButton.Text = isReady ? "Not Ready" : "Ready";

        _statusLabel.Text = hasRole
            ? $"{_logic.ReadyCount}/{_logic.Players.Count} ready"
            : "Pick a role to continue";
    }

    private void OnReadyPressed()
    {
        _logic.SetReady(LocalPlayerId, !_logic.IsReady(LocalPlayerId));
    }

    private void OnAllReady()
    {
        _statusLabel.Text = "Everyone is ready!";
    }

    private void OnBackPressed()
    {
        // Removes this overlay and returns to the lobby room screen
        QueueFree();
    }

    private string PlayerName(int playerId)
    {
        if (playerId == LocalPlayerId) return "You";

        if (_networkManager != null && _networkManager.Players.TryGetValue(playerId, out string name))
        {
            return name;
        }

        return $"Player {playerId}";
    }
}