using Godot;
using System.Collections.Generic;

public partial class Lobby : Control
{
	private NetworkManager _networkManager;

	private PopupPanel _hostPopup;
	private PopupPanel _joinPopup;

	private Control _hostContent;
	private Control _joinContent;
	private Control _roomResultControl;

	private StyleBoxFlat _foundRoomStyle;

	private Button _hostButton;
	private Button _joinButton;
	private Button _backButton;
	private Button _hostCancelButton;
	private Button _joinCancelButton;
	private Button _createRoomButton;
	private Button _joinRoomButton;

	// Track all name fields across Host and Join popups to keep them in sync.
	private readonly List<LineEdit> _nameInputs = new List<LineEdit>();
	private LineEdit _joinCodeInput;
	private LineEdit _inputRoomName;

	private Timer _searchDebounceTimer;

	private string _playerName = "Player 1";
	private string _lastSearchedCode = "";

	private string _foundHostIP = "";
	private int _foundRoomPort = NetworkManager.Port;

	private const double SearchDelay = 0.6;

	private static readonly Color Cream = new Color("#F8EED8");
	private static readonly Color DarkPurple = new Color("#24152D");
	private static readonly Color Gold = new Color("#E9C875");
	private static readonly Color MutedPurple = new Color("#76677B");
	private static readonly Color ReadOnlyCream = new Color("#F3E7CB");
	private int _foundPlayerCount = 0;
	private int _foundMaxPlayers = NetworkManager.MaxPlayers;
	private bool _isConnecting = false;

	public override void _Ready()
	{
		_networkManager = GetNodeOrNull<NetworkManager>("/root/NetworkManager");

		_hostButton = GetNodeOrNull<Button>("HostPanel/btnHost");
		_joinButton = GetNodeOrNull<Button>("JoinPanel/btnJoin");
		_backButton = GetNodeOrNull<Button>("Back");

		_hostPopup = GetNodeOrNull<PopupPanel>("HostPopup");
		_joinPopup = GetNodeOrNull<PopupPanel>("JoinPopup");

		_hostContent = GetNodeOrNull<Control>("HostPopup/diskShape/DiskLabel/HostContent");
		_joinContent = GetNodeOrNull<Control>("JoinPopup/diskShape/DiskLabel/JoinContent");

		if (_networkManager == null ||
			_hostButton == null ||
			_joinButton == null ||
			_backButton == null ||
			_hostPopup == null ||
			_joinPopup == null ||
			_hostContent == null ||
			_joinContent == null)
		{
			GD.PrintErr("Lobby: A required node is missing. Check scene tree paths.");
			return;
		}

		// Room search result node
		_roomResultControl = _joinContent.FindChild("playerRoom", true, false) as Control;

		_foundRoomStyle = CreateInputStyle(Cream, Gold, 2, 6, 15, 10);

		// Recursively find and register all player name inputs (e.g. playerNameInput)
		_nameInputs.Clear();
		FindAndRegisterNameInputs(_hostContent);
		FindAndRegisterNameInputs(_joinContent);
		FindAndRegisterNameInputs(this);

		string initialName = "Player 1";
		foreach (var input in _nameInputs)
		{
			if (!string.IsNullOrWhiteSpace(input.Text) && input.Text != "Player 1")
			{
				initialName = input.Text.Trim();
				break;
			}
		}
		_playerName = initialName;

		StyleBoxFlat nameNormal = CreateInputStyle(Cream, Gold, 2, 6, 24, 16);
		StyleBoxFlat nameFocus = (StyleBoxFlat)nameNormal.Duplicate();
		nameFocus.BorderColor = DarkPurple;
		nameFocus.SetBorderWidthAll(3);

		foreach (var input in _nameInputs)
		{
			input.PlaceholderText = "";
			input.Text = _playerName;

			StyleLineEdit(input);
			input.AddThemeStyleboxOverride("normal", nameNormal);
			input.AddThemeStyleboxOverride("focus", nameFocus);
			input.TextChanged += OnPlayerNameChanged;
		}

		if (_nameInputs.Count == 0)
		{
			GD.PrintErr("Lobby: No player name LineEdit found.");
		}

		
		_inputRoomName = FindLineEdit(_hostContent, "inputRoomName");
		if (_inputRoomName != null)
		{
			if (string.IsNullOrWhiteSpace(_inputRoomName.Text))
				_inputRoomName.Text = "Studio 1";

			_inputRoomName.PlaceholderText = "";
			StyleLineEdit(_inputRoomName);
		}

		// Join room code input
		_joinCodeInput = FindLineEdit(_joinContent, "InputCode");
		if (_joinCodeInput != null)
		{
			_joinCodeInput.PlaceholderText = "";
			StyleLineEdit(_joinCodeInput);
			_joinCodeInput.FocusExited += OnRoomCodeFinished;
			_joinCodeInput.TextSubmitted += OnRoomCodeSubmitted;
			_joinCodeInput.TextChanged += OnRoomCodeChanged;
		}

		// Generated room code display (read-only)
		LineEdit generatedCode = FindLineEdit(_hostContent, "txtGeneratedRoomCode");
		if (generatedCode != null)
		{
			generatedCode.Editable = false;
			generatedCode.PlaceholderText = "";
			StyleLineEdit(generatedCode);
			generatedCode.AddThemeColorOverride("font_uneditable_color", DarkPurple);
		}

		// Style search result input if applicable
		if (_roomResultControl is LineEdit resultInput)
		{
			resultInput.Editable = false;
			resultInput.PlaceholderText = "";
			StyleLineEdit(resultInput);
			resultInput.AddThemeColorOverride("font_uneditable_color", DarkPurple);
		}

		// Search debounce timer
		_searchDebounceTimer = new Timer
		{
			WaitTime = SearchDelay,
			OneShot = true
		};
		AddChild(_searchDebounceTimer);
		_searchDebounceTimer.Timeout += SearchRoomFromInput;

		// Hide popups initially
		_hostPopup.Hide();
		_joinPopup.Hide();

		// Connect main navigation buttons
		_hostButton.Pressed += ShowHostPopup;
		_joinButton.Pressed += ShowJoinPopup;
		_backButton.Pressed += OnBackButtonPressed;

		_hostCancelButton = FindButton(_hostContent, "btnCancel");
		_joinCancelButton = FindButton(_joinContent, "btnCancel");
		_createRoomButton = FindButton(_hostContent, "btnRoom");

		_joinRoomButton = FindButton(_joinContent, "btnRoom") ??
						  FindButton(_joinContent, "btnJoin") ??
						  FindButton(_joinContent, "btnJoinRoom");

		if (_hostCancelButton != null)
			_hostCancelButton.Pressed += OnCancelPopup;

		if (_joinCancelButton != null)
			_joinCancelButton.Pressed += OnCancelPopup;

		if (_createRoomButton != null)
			_createRoomButton.Pressed += OnCreateRoomPressed;

		if (_joinRoomButton != null)
			_joinRoomButton.Pressed += OnJoinRoomPressed;

		// Network events
		_networkManager.RoomFound += OnRoomFound;
		_networkManager.RoomSearchFailed += OnRoomSearchFailed;

		if (_networkManager != null && !string.IsNullOrEmpty(_networkManager.DisconnectReason))
		{
            ShowPopupMessage("Studio Closed", _networkManager.DisconnectReason);
			_networkManager.DisconnectReason = "";
		}

		GD.Print("Lobby loaded successfully.");

		// --- FADE IN EFFECT ---
		CanvasLayer fadeLayer = new CanvasLayer { Layer = 100 };
		AddChild(fadeLayer);

		ColorRect fadeRect = new ColorRect();
		fadeRect.Color = new Color(0, 0, 0, 1);
		fadeLayer.AddChild(fadeRect);

		fadeRect.SetAnchorsPreset(LayoutPreset.FullRect);
		fadeRect.Size = GetViewportRect().Size;

		Tween tween = CreateTween();
		tween.TweenProperty(fadeRect, "color", new Color(0, 0, 0, 0), 0.8f);
		tween.TweenCallback(Callable.From(fadeLayer.QueueFree));
	}

    // ---------------------------------------------------------
    // HELPER FUNCTIONS
    // ---------------------------------------------------------
    private void ShowPopupMessage(string title, string message)
    {
        ConfirmPopup.Notice(this, title, message);
    }

    private void FindAndRegisterNameInputs(Control root)
	{
		if (root == null) return;

		List<LineEdit> allLineEdits = FindAllLineEdits(root);
		foreach (LineEdit input in allLineEdits)
		{
			string nodeName = input.Name.ToString().ToLowerInvariant();
			if ((nodeName.Contains("name") || nodeName.Contains("player")) && !nodeName.Contains("room"))
			{
				if (!_nameInputs.Contains(input))
				{
					_nameInputs.Add(input);
				}
			}
		}
	}

	private List<LineEdit> FindAllLineEdits(Node root)
	{
		List<LineEdit> list = new List<LineEdit>();
		if (root == null) return list;

		foreach (Node child in root.GetChildren())
		{
			if (child is LineEdit lineEdit)
			{
				list.Add(lineEdit);
			}
			list.AddRange(FindAllLineEdits(child));
		}
		return list;
	}

	private string GetPlayerName()
	{
		if (!string.IsNullOrWhiteSpace(_playerName))
			return _playerName.Trim();

		foreach (var input in _nameInputs)
		{
			if (GodotObject.IsInstanceValid(input) && !string.IsNullOrWhiteSpace(input.Text))
			{
				return input.Text.Trim();
			}
		}
		return "Player 1";
	}

	// ---------------------------------------------------------
	// INPUT STYLING & AUTO-SELECT
	// ---------------------------------------------------------

	private StyleBoxFlat CreateInputStyle(
		Color background,
		Color border,
		int borderWidth,
		int radius,
		int leftPadding,
		int rightPadding)
	{
		StyleBoxFlat style = new StyleBoxFlat
		{
			BgColor = background,
			BorderColor = border
		};

		style.SetBorderWidthAll(borderWidth);
		style.SetCornerRadiusAll(radius);

		style.ContentMarginLeft = leftPadding;
		style.ContentMarginRight = rightPadding;
		style.ContentMarginTop = 7;
		style.ContentMarginBottom = 7;

		return style;
	}

	private void StyleLineEdit(LineEdit input)
	{
		if (input == null) return;

		// Auto-select all text whenever the field is focused/clicked
		input.FocusEntered += () => input.CallDeferred(LineEdit.MethodName.SelectAll);

		StyleBoxFlat normal = CreateInputStyle(Cream, Gold, 2, 6, 16, 10);
		StyleBoxFlat focus = (StyleBoxFlat)normal.Duplicate();
		focus.BorderColor = DarkPurple;
		focus.SetBorderWidthAll(3);

		StyleBoxFlat readOnly = (StyleBoxFlat)normal.Duplicate();
		readOnly.BgColor = ReadOnlyCream;

		input.AddThemeStyleboxOverride("normal", normal);
		input.AddThemeStyleboxOverride("focus", focus);
		input.AddThemeStyleboxOverride("read_only", readOnly);

		input.AddThemeColorOverride("font_color", DarkPurple);
		input.AddThemeColorOverride("font_focus_color", DarkPurple);
		input.AddThemeColorOverride("font_uneditable_color", DarkPurple);
		input.AddThemeColorOverride("font_placeholder_color", MutedPurple);
		input.AddThemeColorOverride("caret_color", DarkPurple);
		input.AddThemeColorOverride("selection_color", new Color("#D6C0E3"));
	}

	private LineEdit FindLineEdit(Control root, string nodeName)
	{
		if (root == null) return null;

		LineEdit result = root.FindChild(nodeName, true, false) as LineEdit;
		if (result != null) return result;

		result = root.FindChild(nodeName.ToLower(), true, false) as LineEdit;
		if (result != null) return result;

		string capitalized = char.ToUpper(nodeName[0]) + nodeName.Substring(1);
		return root.FindChild(capitalized, true, false) as LineEdit;
	}

	private Button FindButton(Control root, string nodeName)
	{
		if (root == null) return null;
		return root.FindChild(nodeName, true, false) as Button;
	}

	// ---------------------------------------------------------
	// POPUP NAVIGATION & PLAYER NAME
	// ---------------------------------------------------------

	private void TransitionToScene(string scenePath)
	{
		// 1. Slide down any visible popup immediately during transition
		if (_hostPopup != null && _hostPopup.Visible)
			SlideDownPopup(_hostPopup);

		if (_joinPopup != null && _joinPopup.Visible)
			SlideDownPopup(_joinPopup);

		// 2. Spawn black fade overlay
		CanvasLayer fadeLayer = new CanvasLayer { Layer = 100 };
		AddChild(fadeLayer);

		ColorRect fadeRect = new ColorRect();
		fadeRect.Color = new Color(0, 0, 0, 0);
		fadeLayer.AddChild(fadeRect);

		fadeRect.SetAnchorsPreset(LayoutPreset.FullRect);
		fadeRect.Size = GetViewportRect().Size;

		// 3. Extended fade duration (0.8s)
		Tween tween = CreateTween();
		tween.TweenProperty(fadeRect, "color", new Color(0, 0, 0, 1), 0.8f);
		tween.TweenCallback(Callable.From(() => GetTree().ChangeSceneToFile(scenePath)));
	}


	private void OnPlayerNameChanged(string newText)
	{
		_playerName = newText.Trim();

		foreach (var input in _nameInputs)
		{
			if (GodotObject.IsInstanceValid(input) && input.Text != newText)
			{
				input.Text = newText;
			}
		}

		if (_joinPopup != null && _joinPopup.Visible &&
			_joinCodeInput != null && !string.IsNullOrWhiteSpace(_joinCodeInput.Text))
		{
			SetRoomDisplay($"Searching for room as {_playerName}...");
		}
	}

   private void ShowHostPopup()
	{
		_joinPopup.Hide();
		SlideUpPopup(_hostPopup);
	}

	private void ShowJoinPopup()
	{
		_hostPopup.Hide();
		SlideUpPopup(_joinPopup);
	}

	private void SlideUpPopup(PopupPanel popup)
	{
		if (popup == null) return;

		// 1. Trigger the popup so it calculates its internal layout and size
		popup.PopupCentered();

		// 2. Get viewport and popup dimensions
		Vector2I viewportSize = (Vector2I)GetViewportRect().Size;
		Vector2I popupSize = popup.Size;

		// 3. Calculate target center position and starting bottom position
		Vector2I targetPos = new Vector2I(
			(viewportSize.X - popupSize.X) / 2,
			(viewportSize.Y - popupSize.Y) / 2
		);
		Vector2I startPos = new Vector2I(targetPos.X, viewportSize.Y);

		// 4. Instantly snap to the bottom off-screen
		popup.Position = startPos;

		// 5. Tween the popup up to the center smoothly
		Tween tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Quint); // Smooth deceleration
		tween.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(popup, "position", targetPos, 0.4f);
	}

	private void OnBackButtonPressed()
	{
		GetTree().ChangeSceneToFile("res://scenes/menu.tscn");
	}

	// ---------------------------------------------------------
	// CANCEL ACTION (NO CONFIRMATION POPUP)
	// ---------------------------------------------------------

	private void OnCancelPopup()
	{
		if (_searchDebounceTimer != null)
			_searchDebounceTimer.Stop();

		// Reset host room name back to default "Studio 1"
		if (_inputRoomName != null)
			_inputRoomName.Text = "Studio 1";

		LineEdit generatedCode = FindLineEdit(_hostContent, "txtGeneratedRoomCode");
		if (generatedCode != null)
			generatedCode.Clear();

		// Disconnect host session on network manager if active
		_networkManager?.DisconnectFromRoom();

		// Reset join search state
		if (_joinCodeInput != null)
			_joinCodeInput.Clear();

		_foundHostIP = "";
		_foundRoomPort = NetworkManager.Port;
		SetRoomDisplay("");
		_lastSearchedCode = "";
		SetRoomFoundOutline(false);

		// Slide down whichever popup is currently visible instead of hiding instantly
		if (_hostPopup != null && _hostPopup.Visible) SlideDownPopup(_hostPopup);
		if (_joinPopup != null && _joinPopup.Visible) SlideDownPopup(_joinPopup);

		GD.Print("Popup cancelled.");
	}

	private void SlideDownPopup(PopupPanel popup)
	{
		if (popup == null || !popup.Visible) return;

		// Calculate the bottom of the screen
		Vector2I viewportSize = (Vector2I)GetViewportRect().Size;
		Vector2I targetPos = new Vector2I(popup.Position.X, viewportSize.Y);

		Tween tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Quint);
		tween.SetEase(Tween.EaseType.In); // Ease in accelerates as it falls

		// Slide the popup down
		tween.TweenProperty(popup, "position", targetPos, 0.4f);

		// Automatically call Hide() exactly when the animation finishes
		tween.TweenCallback(Callable.From(popup.Hide));
	}

	// ---------------------------------------------------------
	// CREATE ROOM
	// ---------------------------------------------------------

	private void OnCreateRoomPressed()
	{
		if (_networkManager == null || _hostContent == null) return;

		string playerName = GetPlayerName();
		string roomName = _inputRoomName?.Text.Trim() ?? "";

		if (string.IsNullOrWhiteSpace(roomName))
		{
			roomName = "Studio 1";
			if (_inputRoomName != null)
				_inputRoomName.Text = roomName;
		}

		if (string.IsNullOrWhiteSpace(playerName))
		{
			playerName = "Player 1";
		}

		_playerName = playerName;

		Error result = _networkManager.CreateRoom(playerName, roomName);
		if (result != Error.Ok)
		{
			LineEdit generatedCode = FindLineEdit(_hostContent, "txtGeneratedRoomCode");
			if (generatedCode != null) generatedCode.Clear();
			GD.PrintErr($"Room creation failed: {result}");
			return;
		}

		GD.Print($"Room created: {_networkManager.RoomName}, code: {_networkManager.RoomCode}");

		// Transition directly to the player list scene
		TransitionToScene("res://scenes/player_lists.tscn");
	}

	// ---------------------------------------------------------
	// ROOM SEARCH & JOIN
	// ---------------------------------------------------------

	private void SearchRoomFromInput()
	{
		if (_networkManager == null || _joinCodeInput == null) return;

		string code = _joinCodeInput.Text.Trim().ToUpperInvariant();
		string enteredName = GetPlayerName();

		if (string.IsNullOrWhiteSpace(code))
		{
			SetRoomDisplay("");
			return;
		}

		if (code == _lastSearchedCode) return;

		_lastSearchedCode = code;
		SetRoomDisplay($"Searching for room as {enteredName}...");

		_networkManager.SearchForRoom(code);
	}

	private void OnRoomCodeFinished() => SearchRoomFromInput();

	private void OnRoomCodeSubmitted(string text) => SearchRoomFromInput();

	private void OnRoomCodeChanged(string newText)
	{
		SetRoomFoundOutline(false);
		_lastSearchedCode = "";

		if (_searchDebounceTimer == null) return;

		_searchDebounceTimer.Stop();

		string code = newText.Trim().ToUpperInvariant();
		if (string.IsNullOrWhiteSpace(code))
		{
			SetRoomDisplay("");
			return;
		}

		string enteredName = GetPlayerName();
		SetRoomDisplay($"Searching for room as {enteredName}...");

		_searchDebounceTimer.Start();
	}

	private void OnRoomFound(string roomName, string hostName, string hostIP, int roomPort, int playerCount, int maxPlayers)
	{
		_foundHostIP = hostIP;
		_foundRoomPort = roomPort;
		_foundPlayerCount = playerCount;
		_foundMaxPlayers = maxPlayers;

		string fullTag = playerCount >= maxPlayers ? " (FULL)" : "";
		SetRoomDisplay($"{roomName} | Host: {hostName} | {playerCount}/{maxPlayers}{fullTag}");
		SetRoomFoundOutline(true);

		GD.Print($"Room found! Name: {roomName}, Host: {hostName}, IP: {hostIP}, Port: {roomPort}, Capacity: {playerCount}/{maxPlayers}");
	}
	private void OnRoomSearchFailed(string message)
	{
		_foundHostIP = "";
		SetRoomDisplay(message);
		SetRoomFoundOutline(false);

		GD.PrintErr(message);
	}

	private void OnJoinRoomPressed()
	{
		ExecuteJoinRoom();
	}

	private void ExecuteJoinRoom()
	{
		if (_networkManager == null || _isConnecting) return;

		string playerName = GetPlayerName();
		string code = _joinCodeInput?.Text.Trim().ToUpperInvariant() ?? "";

		if (string.IsNullOrWhiteSpace(code))
		{
			SetRoomDisplay("Enter a room code before joining.");
			return;
		}

		if (string.IsNullOrWhiteSpace(_foundHostIP))
		{
			SetRoomDisplay("No valid room found to join.");
			return;
		}

		// Early check: stop joining if room is already full
		if (_foundPlayerCount >= _foundMaxPlayers)
		{
			SetRoomDisplay("Can't connect: Room is full.");
			return;
		}

		_isConnecting = true;
		_playerName = playerName;
		SetRoomDisplay("Connecting to host...");

		_networkManager.JoinSucceeded += OnJoinSucceeded;
		_networkManager.JoinFailed += OnJoinFailed;

		Error result = _networkManager.JoinRoom(_foundHostIP, _foundRoomPort, playerName);
		if (result != Error.Ok)
		{
			_isConnecting = false;
			_networkManager.JoinSucceeded -= OnJoinSucceeded;
			_networkManager.JoinFailed -= OnJoinFailed;
			SetRoomDisplay("Failed to initiate connection.");
		}
	}

	private void OnJoinSucceeded()
	{
		_isConnecting = false;
		_networkManager.JoinSucceeded -= OnJoinSucceeded;
		_networkManager.JoinFailed -= OnJoinFailed;

		TransitionToScene("res://scenes/player_lists.tscn");
	}

	private void OnJoinFailed(string reason)
	{
		_isConnecting = false;
		_networkManager.JoinSucceeded -= OnJoinSucceeded;
		_networkManager.JoinFailed -= OnJoinFailed;

		SetRoomDisplay(reason);
	}

	// ---------------------------------------------------------
	// DISPLAY MANAGEMENT
	// ---------------------------------------------------------

	private void SetRoomDisplay(string message)
	{
		if (_joinContent == null) return;

		Node display = _joinContent.FindChild("playerRoom", true, false);
		SetNodeText(display, message);
	}

	private void SetNodeText(Node node, string message)
	{
		switch (node)
		{
			case Label label:
				label.Text = message;
				break;
			case LineEdit lineEdit:
				lineEdit.Text = message;
				break;
			case RichTextLabel richTextLabel:
				richTextLabel.Text = message;
				break;
			case TextEdit textEdit:
				textEdit.Text = message;
				break;
			case null:
				GD.PrintErr("Could not find the requested text node.");
				break;
			default:
				GD.PrintErr($"Unsupported text node type: {node.GetType().Name}");
				break;
		}
	}

	private void SetRoomFoundOutline(bool found)
	{
		if (_roomResultControl is not LineEdit lineEdit || _foundRoomStyle == null)
			return;

		if (found)
		{
			StyleBoxFlat normal = (StyleBoxFlat)_foundRoomStyle.Duplicate();
			StyleBoxFlat focus = (StyleBoxFlat)_foundRoomStyle.Duplicate();

			focus.BorderColor = DarkPurple;
			focus.SetBorderWidthAll(3);

			lineEdit.AddThemeStyleboxOverride("normal", normal);
			lineEdit.AddThemeStyleboxOverride("focus", focus);
			lineEdit.AddThemeStyleboxOverride("read_only", normal);
		}
		else
		{
			StyleLineEdit(lineEdit);
		}

		lineEdit.QueueRedraw();
	}

	// ---------------------------------------------------------
	// CLEANUP
	// ---------------------------------------------------------

	public override void _ExitTree()
	{
		foreach (var input in _nameInputs)
		{
			if (GodotObject.IsInstanceValid(input))
				input.TextChanged -= OnPlayerNameChanged;
		}

		if (_joinCodeInput != null && GodotObject.IsInstanceValid(_joinCodeInput))
		{
			_joinCodeInput.FocusExited -= OnRoomCodeFinished;
			_joinCodeInput.TextSubmitted -= OnRoomCodeSubmitted;
			_joinCodeInput.TextChanged -= OnRoomCodeChanged;
		}

		if (_searchDebounceTimer != null && GodotObject.IsInstanceValid(_searchDebounceTimer))
		{
			_searchDebounceTimer.Timeout -= SearchRoomFromInput;
		}

		if (_networkManager != null && GodotObject.IsInstanceValid(_networkManager))
		{
			_networkManager.RoomFound -= OnRoomFound;
			_networkManager.RoomSearchFailed -= OnRoomSearchFailed;
		}

		if (_hostButton != null && GodotObject.IsInstanceValid(_hostButton))
			_hostButton.Pressed -= ShowHostPopup;

		if (_joinButton != null && GodotObject.IsInstanceValid(_joinButton))
			_joinButton.Pressed -= ShowJoinPopup;

		if (_backButton != null && GodotObject.IsInstanceValid(_backButton))
			_backButton.Pressed -= OnBackButtonPressed;

		if (_hostCancelButton != null && GodotObject.IsInstanceValid(_hostCancelButton))
			_hostCancelButton.Pressed -= OnCancelPopup;

		if (_joinCancelButton != null && GodotObject.IsInstanceValid(_joinCancelButton))
			_joinCancelButton.Pressed -= OnCancelPopup;

		if (_createRoomButton != null && GodotObject.IsInstanceValid(_createRoomButton))
			_createRoomButton.Pressed -= OnCreateRoomPressed;

		if (_joinRoomButton != null && GodotObject.IsInstanceValid(_joinRoomButton))
			_joinRoomButton.Pressed -= OnJoinRoomPressed;
	}
}
