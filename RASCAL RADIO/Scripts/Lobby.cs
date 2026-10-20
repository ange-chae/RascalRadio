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

    private const double SearchDelay = 0.6;

    private static readonly Color Cream = new Color("#F8EED8");
    private static readonly Color DarkPurple = new Color("#24152D");
    private static readonly Color Gold = new Color("#E9C875");
    private static readonly Color MutedPurple = new Color("#76677B");
    private static readonly Color ReadOnlyCream = new Color("#F3E7CB");

    public override void _Ready()
    {
        _networkManager =
            GetNodeOrNull<NetworkManager>("/root/NetworkManager");

        _hostButton = GetNodeOrNull<Button>("HostPanel/btnHost");
        _joinButton = GetNodeOrNull<Button>("JoinPanel/btnJoin");
        _backButton = GetNodeOrNull<Button>("Back");

        _hostPopup = GetNodeOrNull<PopupPanel>("HostPopup");
        _joinPopup = GetNodeOrNull<PopupPanel>("JoinPopup");

        _hostContent = GetNodeOrNull<Control>(
            "HostPopup/diskShape/DiskLabel/HostContent");

        _joinContent = GetNodeOrNull<Control>(
            "JoinPopup/diskShape/DiskLabel/JoinContent");

        if (_networkManager == null ||
            _hostButton == null ||
            _joinButton == null ||
            _backButton == null ||
            _hostPopup == null ||
            _joinPopup == null ||
            _hostContent == null ||
            _joinContent == null)
        {
            GD.PrintErr(
                "Lobby: A required node is missing. Check scene tree paths.");
            return;
        }

        // Find the room search result node.
        _roomResultControl =
            _joinContent.FindChild("playerRoom", true, false) as Control;

        if (_roomResultControl != null)
        {
            _roomResultControl.GuiInput += OnRoomResultGuiInput;
        }

        _foundRoomStyle =
            CreateInputStyle(Cream, Gold, 2, 6, 15, 10);

        // Find and register all player name input fields across Host, Join, and main scene.
        _nameInputs.Clear();
        FindAndRegisterNameInputs(_hostContent);
        FindAndRegisterNameInputs(_joinContent);
        FindAndRegisterNameInputs(this);

        string initialName = "Player 1";
        foreach (var input in _nameInputs)
        {
            if (!string.IsNullOrWhiteSpace(input.Text))
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

        // Host room name input (defaults to "Room 1").
        _inputRoomName = FindLineEdit(_hostContent, "inputRoomName");
        if (_inputRoomName != null)
        {
            if (string.IsNullOrWhiteSpace(_inputRoomName.Text))
                _inputRoomName.Text = "Room 1";

            StyleLineEdit(_inputRoomName);
        }

        // Join room code input.
        _joinCodeInput = FindLineEdit(_joinContent, "InputCode");
        if (_joinCodeInput != null)
        {
            StyleLineEdit(_joinCodeInput);
            _joinCodeInput.FocusExited += OnRoomCodeFinished;
            _joinCodeInput.TextSubmitted += OnRoomCodeSubmitted;
            _joinCodeInput.TextChanged += OnRoomCodeChanged;
        }

        // Generated room code display (read-only).
        LineEdit generatedCode = FindLineEdit(_hostContent, "txtGeneratedRoomCode");
        if (generatedCode != null)
        {
            generatedCode.Editable = false;
            StyleLineEdit(generatedCode);
            generatedCode.AddThemeColorOverride("font_uneditable_color", DarkPurple);
        }

        // Style search result input if applicable.
        if (_roomResultControl is LineEdit resultInput)
        {
            resultInput.Editable = false;
            StyleLineEdit(resultInput);
            resultInput.AddThemeColorOverride("font_uneditable_color", DarkPurple);
        }

        // Search debounce timer.
        _searchDebounceTimer = new Timer
        {
            WaitTime = SearchDelay,
            OneShot = true
        };
        AddChild(_searchDebounceTimer);
        _searchDebounceTimer.Timeout += SearchRoomFromInput;

        // Hide popups initially.
        _hostPopup.Hide();
        _joinPopup.Hide();

        // Connect main navigation buttons.
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

        // Network events.
        _networkManager.RoomFound += OnRoomFound;
        _networkManager.RoomSearchFailed += OnRoomSearchFailed;

        GD.Print("Lobby loaded successfully.");
    }

    // ---------------------------------------------------------
    // HELPER FUNCTIONS
    // ---------------------------------------------------------

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

        // Auto-select all text whenever the field is clicked or focused
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
        _hostPopup.PopupCentered();
    }

    private void ShowJoinPopup()
    {
        _hostPopup.Hide();
        _joinPopup.PopupCentered();
    }

    private void OnBackButtonPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/menu.tscn");
    }

    // ---------------------------------------------------------
    // CANCEL ACTION
    // ---------------------------------------------------------

    private void OnCancelPopup()
    {
        if (_searchDebounceTimer != null)
            _searchDebounceTimer.Stop();

        // Reset host room name input back to default "Room 1".
        if (_inputRoomName != null)
            _inputRoomName.Text = "Room 1";

        LineEdit generatedCode = FindLineEdit(_hostContent, "txtGeneratedRoomCode");
        if (generatedCode != null)
        {
            if (!string.IsNullOrWhiteSpace(generatedCode.Text))
            {
                var cancelMethod = _networkManager?.GetType().GetMethod("CloseRoom")
                                ?? _networkManager?.GetType().GetMethod("LeaveRoom")
                                ?? _networkManager?.GetType().GetMethod("CancelRoom");
                cancelMethod?.Invoke(_networkManager, null);
            }
            generatedCode.Clear();
        }

        // Reset join search state.
        if (_joinCodeInput != null)
            _joinCodeInput.Clear();

        SetRoomDisplay("");
        _lastSearchedCode = "";
        SetRoomFoundOutline(false);

        // Close popups immediately without confirmation dialog.
        _hostPopup.Hide();
        _joinPopup.Hide();

        GD.Print("Popup cancelled.");
    }

    // ---------------------------------------------------------
    // CREATE ROOM
    // ---------------------------------------------------------

    private void OnCreateRoomPressed()
    {
        if (_networkManager == null || _hostContent == null) return;

        LineEdit generatedCode = FindLineEdit(_hostContent, "txtGeneratedRoomCode");
        string playerName = GetPlayerName();
        string roomName = _inputRoomName?.Text.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(roomName))
        {
            roomName = "Room 1";
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
            if (generatedCode != null) generatedCode.Clear();
            GD.PrintErr($"Room creation failed: {result}");
            return;
        }

        if (generatedCode != null)
        {
            generatedCode.Editable = false;
            generatedCode.Text = _networkManager.RoomCode;
        }

        GD.Print($"Room created: {_networkManager.RoomName}, code: {_networkManager.RoomCode}");
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

    private void OnRoomFound(string roomName, string hostName, string hostIP, int roomPort)
    {
        SetRoomDisplay($"{roomName} | Host: {hostName}");
        SetRoomFoundOutline(true);

        GD.Print($"Room found! Name: {roomName}, Host: {hostName}, IP: {hostIP}, Port: {roomPort}");
    }

    private void OnRoomSearchFailed(string message)
    {
        SetRoomDisplay(message);
        SetRoomFoundOutline(false);

        GD.PrintErr(message);
    }

    private void OnJoinRoomPressed()
    {
        ExecuteJoinRoom();
    }

    private void OnRoomResultGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
        {
            ExecuteJoinRoom();
        }
    }

    private void ExecuteJoinRoom()
    {
        if (_networkManager == null) return;

        string playerName = GetPlayerName();
        string code = _joinCodeInput?.Text.Trim().ToUpperInvariant() ?? "";

        if (string.IsNullOrWhiteSpace(code))
        {
            SetRoomDisplay("Enter a room code before joining.");
            return;
        }

        _playerName = playerName;

        var method = _networkManager.GetType().GetMethod("JoinRoom");
        if (method != null)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 2)
            {
                method.Invoke(_networkManager, new object[] { playerName, code });
            }
            else if (parameters.Length == 1)
            {
                method.Invoke(_networkManager, new object[] { code });
            }
            else
            {
                method.Invoke(_networkManager, null);
            }
            GD.Print($"Joining room {code} as {playerName}...");
        }
        else
        {
            GD.PrintErr("NetworkManager does not contain a JoinRoom method.");
        }
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

        if (_roomResultControl != null && GodotObject.IsInstanceValid(_roomResultControl))
        {
            _roomResultControl.GuiInput -= OnRoomResultGuiInput;
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