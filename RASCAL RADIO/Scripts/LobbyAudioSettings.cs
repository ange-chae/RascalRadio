using Godot;

/// <summary>
/// Opens the authored radio scene as a lobby overlay from the settings icon.
/// </summary>
public partial class LobbyAudioSettings : CanvasLayer
{
    private const string RadioScenePath = "res://scenes/settings/radio.tscn";

    private TextureButton _settingsButton;
    private Control _popupRoot;
    private RadioSettings _radio;

    public override void _Ready()
    {
        _settingsButton = GetNodeOrNull<TextureButton>("SettingsButton");
        if (_settingsButton == null)
        {
            GD.PushError("[LobbyAudioSettings] SettingsButton is missing from the lobby scene.");
            return;
        }

        _settingsButton.ZIndex = 100;
        _settingsButton.IgnoreTextureSize = true;
        _settingsButton.Pressed += TogglePopup;
        BuildPopup();
    }

    public override void _Input(InputEvent @event)
    {
        if (_popupRoot?.Visible == true
            && @event is InputEventKey key
            && key.Pressed
            && !key.Echo
            && key.Keycode == Key.Escape)
        {
            SetPopupVisible(false);
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildPopup()
    {
        _popupRoot = new Control
        {
            Name = "AudioSettingsPopup",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _popupRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _popupRoot.Visible = false;
        AddChild(_popupRoot);

        var dimmer = new ColorRect
        {
            Name = "Dimmer",
            Color = new Color(0.0f, 0.0f, 0.0f, 0.12f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        dimmer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dimmer.GuiInput += OnDimmerInput;
        _popupRoot.AddChild(dimmer);

        PackedScene radioScene = GD.Load<PackedScene>(RadioScenePath);
        if (radioScene == null)
        {
            GD.PushError($"[LobbyAudioSettings] Could not load the authored radio scene at {RadioScenePath}.");
            return;
        }

        _radio = radioScene.Instantiate<RadioSettings>();
        _popupRoot.AddChild(_radio);
        _radio.SetProcessInput(false);
    }

    private void TogglePopup()
    {
        SetPopupVisible(_popupRoot.Visible == false);
    }

    private void SetPopupVisible(bool visible)
    {
        if (_popupRoot == null)
        {
            return;
        }

        _popupRoot.Visible = visible;
        _radio?.SetProcessInput(visible);
    }

    private void OnDimmerInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse
            && mouse.Pressed
            && mouse.ButtonIndex == MouseButton.Left
            && _radio != null
            && !_radio.GetRadioBounds().HasPoint(mouse.Position))
        {
            SetPopupVisible(false);
            GetViewport().SetInputAsHandled();
        }
    }
}
