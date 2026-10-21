using System.Collections.Generic;
using System.Linq;
using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;

/// <summary>
/// Role selection screen. Display only: every rule lives in RoleSelectionLogic.
/// This script builds the cards, forwards button presses to the logic,
/// and redraws whenever the logic says something changed.
/// </summary>
public partial class RoleSelection : Control
{
	// TODO: replace with the real player ids once GameSession / NetworkManager provide them.
	private const int LocalPlayerId = 1;
	private static readonly int[] FakePlayerIds = { 2, 3 }; // test helper, remove later

	// All role text lives here, so the team (or the artist) edits it in one place.
	private static readonly Dictionary<Role, (string Name, string Blurb)> RoleInfo = new()
	{
		{ Role.Broadcaster,     ("Broadcaster", "Runs the show. Picks the callers.") },
		{ Role.RadioTechnician, ("Technician",  "Keeps the gear alive. Fixes what the raccoon breaks.") },
		{ Role.RadioOperator,   ("Operator",    "Sets the quota and keeps the schedule.") },
		// Role4 is left out until the team defines it.
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
		_cardsRow = GetNode<HBoxContainer>("Margin/Main/CardsRow");
		_randomizeButton = GetNode<Button>("Margin/Main/BottomBar/RandomizeButton");
		_readyButton = GetNode<Button>("Margin/Main/BottomBar/ReadyButton");
		_statusLabel = GetNode<Label>("Margin/Main/BottomBar/StatusLabel");
		_backButton = GetNode<Button>("Margin/Main/TopBar/BackButton");

		// Remove any preview cards left in the editor; the code builds the real ones.
		foreach (Node child in _cardsRow.GetChildren())
			child.QueueFree();

		// Backend. Roles come from RoleInfo so the screen and the logic always agree.
		_logic = new RoleSelectionLogic(RoleInfo.Keys);
		_logic.SelectionChanged += Refresh;
		_logic.AllReady += OnAllReady;

		BuildCards();

		_randomizeButton.Pressed += () => _logic.Randomize();
		_readyButton.Pressed += OnReadyPressed;
		_backButton.Pressed += () => GD.Print("TODO: Back goes to the lobby once the flow is decided.");

		// Join the room.
		_logic.AddPlayer(LocalPlayerId);
		foreach (int id in FakePlayerIds)
			_logic.AddPlayer(id);

		GD.Print("Test helper: press 2 or 3 to make a fake player pick a role and ready up.");
		Refresh();
	}

	private void BuildCards()
	{
		var cardScene = GD.Load<PackedScene>(CardScenePath);

		foreach (Role role in _logic.Roles)
		{
			var card = cardScene.Instantiate<RoleCard>();
			_cardsRow.AddChild(card); // add first so the card's nodes exist...

			var info = RoleInfo[role];
			card.Setup(info.Name, info.Blurb); // ...then set its text

			Role captured = role;
			card.SelectPressed += () => _logic.TrySelect(LocalPlayerId, captured);

			_cards[role] = card;
		}
	}

	// Redraws everything from the logic's current state.
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
		// TODO: tell GameSession to start the match.
		_statusLabel.Text = "Everyone is ready!";
	}

	private static string PlayerName(int playerId) =>
		playerId == LocalPlayerId ? "You" : $"Player {playerId}";

	// ---------- Test helper: remove once real players join ----------

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false } key)
		{
			if (key.Keycode == Key.Key2) FakePick(2);
			if (key.Keycode == Key.Key3) FakePick(3);
		}
	}

	private void FakePick(int playerId)
	{
		if (_logic.GetRole(playerId) != null) return; // already picked

		var free = _logic.Roles.Where(r => !_logic.IsTaken(r)).ToList();
		if (free.Count == 0) return;

		Role choice = free[(int)(GD.Randi() % (uint)free.Count)];
		_logic.TrySelect(playerId, choice);
		_logic.SetReady(playerId, true);
	}
}
