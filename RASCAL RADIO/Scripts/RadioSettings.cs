using Godot;

/// <summary>
/// Makes the controls in the authored radio.tscn scene interactive while leaving
/// its artwork and hand-authored positions intact.
/// </summary>
public partial class RadioSettings : Node2D
{
    private const string AssetRoot = "res://assets/images/audio_settings/";

    private TextureRect _radioBody;
    private TextureRect _bgmArtwork;
    private TextureRect _sfxArtwork;
    private TextureRect _dragger;
    private TextureRect _line;
    private Button _bgmHitbox;
    private Button _sfxHitbox;
    private Texture2D _normalBgmTexture;
    private Texture2D _mutedBgmTexture;
    private Texture2D _normalSfxTexture;
    private Texture2D _mutedSfxTexture;
    private Rect2 _sliderInputRect;
    private float _draggerMinX;
    private float _draggerMaxX;
    private float _dragOffset;
    private bool _isDragging;

    public override void _Ready()
    {
        SetProcessInput(false);
        _radioBody = GetNode<TextureRect>("radioBody_png");
        _bgmArtwork = GetNode<TextureRect>("BGMbutton");
        _sfxArtwork = GetNode<TextureRect>("BGMbutton2");
        _dragger = GetNode<TextureRect>("drag bar");
        _line = GetNode<TextureRect>("lineBar");

        // The transparent artwork should not intercept clicks intended for the
        // dimmer behind the radio. The hitboxes below handle the two mute buttons.
        _radioBody.MouseFilter = Control.MouseFilterEnum.Ignore;
        _bgmArtwork.MouseFilter = Control.MouseFilterEnum.Ignore;
        _sfxArtwork.MouseFilter = Control.MouseFilterEnum.Ignore;
        _dragger.MouseFilter = Control.MouseFilterEnum.Ignore;
        _line.MouseFilter = Control.MouseFilterEnum.Ignore;

        _normalBgmTexture = _bgmArtwork.Texture;
        _normalSfxTexture = _sfxArtwork.Texture;
        _mutedBgmTexture = GD.Load<Texture2D>(AssetRoot + "MutedBGM.png");
        _mutedSfxTexture = GD.Load<Texture2D>(AssetRoot + "MutedSound.png");

        if (_normalBgmTexture == null || _normalSfxTexture == null || _mutedBgmTexture == null || _mutedSfxTexture == null)
        {
            GD.PushError("[RadioSettings] A mute button texture could not be loaded.");
            return;
        }

        _bgmHitbox = CreateHitbox(_bgmArtwork, "ToggleBgm");
        _sfxHitbox = CreateHitbox(_sfxArtwork, "ToggleSfx");

        _draggerMinX = _line.Position.X + 4.0f;
        _draggerMaxX = _line.Position.X + _line.Size.X - _dragger.Size.X - 4.0f;
        _sliderInputRect = new Rect2(_line.Position.X, _dragger.Position.Y, _line.Size.X, _dragger.Size.Y);

        SyncMuteButtons();
        SyncVolumeHandle();
        SetProcessInput(false);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (!mouseButton.Pressed)
            {
                if (_isDragging)
                {
                    _isDragging = false;
                    GetViewport().SetInputAsHandled();
                }
                return;
            }

            Vector2 localPosition = ToLocal(mouseButton.Position);
            if (_sliderInputRect.HasPoint(localPosition))
            {
                Rect2 handleRect = new Rect2(_dragger.Position, _dragger.Size);
                _dragOffset = handleRect.HasPoint(localPosition)
                    ? localPosition.X - _dragger.Position.X
                    : _dragger.Size.X * 0.5f;
                _isDragging = true;
                UpdateVolumeFromPointer(localPosition.X - _dragOffset);
                GetViewport().SetInputAsHandled();
            }
        }
        else if (_isDragging && @event is InputEventMouseMotion mouseMotion)
        {
            Vector2 localPosition = ToLocal(mouseMotion.Position);
            UpdateVolumeFromPointer(localPosition.X - _dragOffset);
            GetViewport().SetInputAsHandled();
        }
    }

    public Rect2 GetRadioBounds()
    {
        return _radioBody?.GetGlobalRect() ?? new Rect2();
    }

    private Button CreateHitbox(TextureRect artwork, string handlerName)
    {
        var button = new Button
        {
            Name = artwork.Name + "Hitbox",
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
            ZIndex = 10,
            TooltipText = handlerName == "ToggleBgm"
                ? "Mute or restore background music"
                : "Mute or restore sound effects"
        };

        var emptyStyle = new StyleBoxEmpty();
        button.AddThemeStyleboxOverride("normal", emptyStyle);
        button.AddThemeStyleboxOverride("hover", emptyStyle);
        button.AddThemeStyleboxOverride("pressed", emptyStyle);
        button.AddThemeStyleboxOverride("focus", emptyStyle);

        AddChild(button);
        SetHitboxToArtwork(button, artwork);
        if (handlerName == "ToggleBgm")
        {
            button.Pressed += ToggleBgm;
        }
        else
        {
            button.Pressed += ToggleSfx;
        }
        return button;
    }

    private static void SetHitboxToArtwork(Button button, TextureRect artwork)
    {
        Texture2D texture = artwork.Texture;
        if (texture == null)
        {
            return;
        }

        Rect2I visiblePixels = texture.GetImage().GetUsedRect();
        if (visiblePixels.Size.X <= 0 || visiblePixels.Size.Y <= 0)
        {
            visiblePixels = new Rect2I(Vector2I.Zero, new Vector2I(texture.GetWidth(), texture.GetHeight()));
        }

        float scaleX = artwork.Size.X / texture.GetWidth();
        float scaleY = artwork.Size.Y / texture.GetHeight();
        button.Position = artwork.Position + new Vector2(visiblePixels.Position.X * scaleX, visiblePixels.Position.Y * scaleY);
        button.Size = new Vector2(visiblePixels.Size.X * scaleX, visiblePixels.Size.Y * scaleY);
    }

    private void UpdateVolumeFromPointer(float handleLeft)
    {
        float clampedLeft = Mathf.Clamp(handleLeft, _draggerMinX, _draggerMaxX);
        _dragger.Position = new Vector2(clampedLeft, _dragger.Position.Y);

        float range = _draggerMaxX - _draggerMinX;
        float progress = range <= 0.0f ? 0.0f : (clampedLeft - _draggerMinX) / range;
        // The authored handle starts at the left edge for full volume.
        AudioSettingsManager.Instance?.SetMasterVolumePercent((1.0f - progress) * 100.0f);
    }

    private void SyncVolumeHandle()
    {
        float volume = AudioSettingsManager.Instance?.MasterVolumePercent ?? 100.0f;
        float progress = 1.0f - volume / 100.0f;
        _dragger.Position = new Vector2(
            Mathf.Lerp(_draggerMinX, _draggerMaxX, progress),
            _dragger.Position.Y);
    }

    private void SyncMuteButtons()
    {
        AudioSettingsManager settings = AudioSettingsManager.Instance;
        if (settings == null)
        {
            return;
        }

        _bgmArtwork.Texture = settings.BgmMuted ? _mutedBgmTexture : _normalBgmTexture;
        _sfxArtwork.Texture = settings.SfxMuted ? _mutedSfxTexture : _normalSfxTexture;
        SetHitboxToArtwork(_bgmHitbox, _bgmArtwork);
        SetHitboxToArtwork(_sfxHitbox, _sfxArtwork);
    }

    private void ToggleBgm()
    {
        AudioSettingsManager settings = AudioSettingsManager.Instance;
        if (settings == null)
        {
            return;
        }

        settings.SetBgmMuted(!settings.BgmMuted);
        SyncMuteButtons();
    }

    private void ToggleSfx()
    {
        AudioSettingsManager settings = AudioSettingsManager.Instance;
        if (settings == null)
        {
            return;
        }

        settings.SetSfxMuted(!settings.SfxMuted);
        SyncMuteButtons();
    }
}
