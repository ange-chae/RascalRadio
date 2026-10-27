using Godot;

public partial class QuitGuard : Node
{
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GetTree().AutoAcceptQuit = false;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest) return;

        var nm = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
        bool hosting = nm != null && nm.IsHosting;

        ConfirmPopup.Show(this, "Quit game?",
            hosting ? "You're the host. Quitting ends the studio for everyone."
                    : "You'll leave the studio.",
            "Quit", "Stay",
            () => { nm?.DisconnectFromRoom(); GetTree().Quit(); });
    }
}