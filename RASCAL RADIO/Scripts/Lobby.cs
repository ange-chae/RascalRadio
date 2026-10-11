using Godot;

public partial class Lobby : Control
{
    private Control _hostContent;
    private Control _joinContent;
    private NetworkManager _networkManager;

    public override void _Ready()
    {
        _networkManager = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
        _hostContent = GetNodeOrNull<Control>("rightPanel/HostContent");
        _joinContent = GetNodeOrNull<Control>("rightPanel/JoinContent");

        Button hostButton = GetNodeOrNull<Button>("HostPanel/btnHost");
        Button joinButton = GetNodeOrNull<Button>("JoinPanel/btnJoin");
        Button createRoomButton =
            GetNodeOrNull<Button>("rightPanel/HostContent/btnRoom");
        Button searchRoomButton =
            GetNodeOrNull<Button>("rightPanel/JoinContent/btnJoin");
        Button backButton = GetNodeOrNull<Button>("Back");

        if (_networkManager == null ||
            _hostContent == null ||
            _joinContent == null ||
            hostButton == null ||
            joinButton == null ||
            createRoomButton == null ||
            searchRoomButton == null ||
            backButton == null)
        {
            GD.PrintErr(
                "Lobby: A node path is incorrect. Check the Scene tree names."
            );
            return;
        }

        createRoomButton.Pressed += OnCreateRoomPressed;
        searchRoomButton.Pressed += OnSearchRoomPressed;
        backButton.Pressed += OnBackButtonPressed;
        hostButton.Pressed += ShowHostContent;
        joinButton.Pressed += ShowJoinContent;

        _networkManager.RoomFound += OnRoomFound;
        _networkManager.RoomSearchFailed += OnRoomSearchFailed;

        ShowHostContent();
        GD.Print("Lobby loaded successfully.");
    }

    private void ShowHostContent()
    {
        _hostContent.Visible = true;
        _joinContent.Visible = false;
    }

    private void ShowJoinContent()
    {
        _hostContent.Visible = false;
        _joinContent.Visible = true;
    }

    private void OnBackButtonPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/menu.tscn");
    }

    private void OnCreateRoomPressed()
    {
        LineEdit playerNameInput =
            GetNode<LineEdit>("rightPanel/HostContent/inputName");

        LineEdit roomNameInput =
            GetNode<LineEdit>("rightPanel/HostContent/inputIP");

        LineEdit roomCodeInput =
            GetNode<LineEdit>(
                "rightPanel/HostContent/txtGeneratedRoomCode"
            );

        Error result = _networkManager.CreateRoom(
            playerNameInput.Text,
            roomNameInput.Text
        );

        if (result != Error.Ok)
        {
            roomCodeInput.Text = "";
            GD.PrintErr(
                "Room creation failed. Check the player name, room name, and Output panel."
            );
            return;
        }

        roomCodeInput.Editable = false;
        roomCodeInput.Text = _networkManager.RoomCode;
    }

    private void OnSearchRoomPressed()
    {
        LineEdit codeInput =
            GetNode<LineEdit>("rightPanel/JoinContent/inputCode");

        Control roomDisplay =
            GetNodeOrNull<Control>("rightPanel/JoinContent/playerRoom");

        string code = codeInput.Text.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(code))
        {
            SetRoomDisplay(roomDisplay, "Enter a room code first.");
            return;
        }

        SetRoomDisplay(roomDisplay, "Searching for room...");
        _networkManager.SearchForRoom(code);
    }

    private void OnRoomFound(
        string roomName,
        string hostName,
        string hostIP,
        int roomPort)
    {
        Control roomDisplay =
            GetNodeOrNull<Control>("rightPanel/JoinContent/playerRoom");

        SetRoomDisplay(
            roomDisplay,
            $"Room found: {roomName} | Host: {hostName}"
        );

        GD.Print(
            $"Room found! Name: {roomName}, Host: {hostName}, IP: {hostIP}, Port: {roomPort}"
        );
    }

    private void OnRoomSearchFailed(string message)
    {
        Control roomDisplay =
            GetNodeOrNull<Control>("rightPanel/JoinContent/playerRoom");

        SetRoomDisplay(roomDisplay, message);
        GD.PrintErr(message);
    }

    private void SetRoomDisplay(Control display, string message)
    {
        if (display is Label label)
        {
            label.Text = message;
        }
        else if (display is LineEdit lineEdit)
        {
            lineEdit.Text = message;
        }
        else if (display is RichTextLabel richTextLabel)
        {
            richTextLabel.Text = message;
        }
        else if (display is TextEdit textEdit)
        {
            textEdit.Text = message;
        }
        else
        {
            GD.PrintErr(
                "Lobby: 'playerRoom' must be a Label, LineEdit, RichTextLabel, or TextEdit."
            );
        }
    }

    public override void _ExitTree()
    {
        if (_networkManager != null)
        {
            _networkManager.RoomFound -= OnRoomFound;
            _networkManager.RoomSearchFailed -= OnRoomSearchFailed;
        }
    }
}