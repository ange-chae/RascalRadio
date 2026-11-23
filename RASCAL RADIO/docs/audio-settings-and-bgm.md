# Audio settings and background music

This note describes the radio settings UI, the shared audio controls, and the background music added for the player list and game-over screens.

## Audio routing

`AudioSettingsManager` is registered as the `AudioSettings` autoload in `project.godot`. The autoload creates the `BGM` and `SFX` buses if they are missing, and sends both buses to `Master`.

```text
BGM tracks ──> BGM bus ──┐
                         ├──> Master bus ──> speakers
Sound effects ─> SFX bus ┘
```

- The radio's **BGM** button mutes or unmutes every player assigned to `BGM`.
- The **SFX** button mutes or unmutes every player assigned to `SFX`.
- The radio's volume slider changes the **Master** bus, so it changes the level of both music and effects together.
- Each audio player can still have its own `VolumeDb` starting level. That level is multiplied by the bus and master levels.

The settings values live in the autoload, so they remain in effect as the game changes scenes during the current run. `_Ready()` currently initializes Master to 100% and unmutes both buses when the application starts; settings are not saved between launches.

The bus helper reuses an existing bus or creates it, then routes it to Master:

```csharp
private static int GetOrCreateBus(string name)
{
    int busIndex = AudioServer.GetBusIndex(name);
    if (busIndex < 0)
    {
        AudioServer.AddBus();
        busIndex = AudioServer.BusCount - 1;
        AudioServer.SetBusName(busIndex, name);
    }

    AudioServer.SetBusSend(busIndex, "Master");
    return busIndex;
}
```

## The radio settings panel

### Authored layout and lobby entry point

The radio artwork and positions are authored in `scenes/settings/radio.tscn`. `RadioSettings.cs` is attached to that scene's `Radio` root and adds the slider and mute-button behavior without rebuilding the artwork layout.

In `scenes/lobby.tscn`, `AudioSettingsUI` uses `LobbyAudioSettings.cs` and contains the `SettingsButton`. Pressing it opens an overlay that instances `radio.tscn`. The overlay dims the lobby and closes on Escape or a click outside the radio body.

The settings icon is `assets/images/audio_settings/settingsIcon.png`; the supplied gear image replaced the image at that same path, which the lobby scene already references.

The radio panel is currently opened from the lobby. Its bus mute and volume state is global, so choices made there also apply to music on later scenes; the player-list and game-over screens do not add a second settings button.

### 0–100 volume slider

The slider uses the authored `lineBar` and `drag bar` controls. Its usable handle range leaves a 4-pixel inset at both ends:

```csharp
_draggerMinX = _line.Position.X + 4.0f;
_draggerMaxX = _line.Position.X + _line.Size.X - _dragger.Size.X - 4.0f;
```

When the user presses the line or handle, `RadioSettings._Input()` starts a drag. It remembers where inside the handle the pointer was pressed, so the handle does not jump under the cursor. Clicking the line instead centers the handle at the click. While the left mouse button is held, mouse motion updates the handle; releasing the button ends the drag.

The handle position is clamped to its allowed range, then converted to a percentage:

```csharp
float clampedLeft = Mathf.Clamp(handleLeft, _draggerMinX, _draggerMaxX);
_dragger.Position = new Vector2(clampedLeft, _dragger.Position.Y);

float range = _draggerMaxX - _draggerMinX;
float progress = range <= 0.0f ? 0.0f : (clampedLeft - _draggerMinX) / range;
AudioSettingsManager.Instance?.SetMasterVolumePercent((1.0f - progress) * 100.0f);
```

The subtraction reverses the usual slider direction to match this artwork's initial handle placement:

| Handle position | Master volume |
| --- | ---: |
| Far left | 100% |
| Middle | About 50% |
| Far right | 0% |

`SyncVolumeHandle()` performs the reverse calculation when the radio scene opens. It reads the current master percentage and places the handle at the matching location.

```csharp
private void SyncVolumeHandle()
{
    float volume = AudioSettingsManager.Instance?.MasterVolumePercent ?? 100.0f;
    float progress = 1.0f - volume / 100.0f;
    _dragger.Position = new Vector2(
        Mathf.Lerp(_draggerMinX, _draggerMaxX, progress),
        _dragger.Position.Y);
}
```

`AudioSettingsManager.SetMasterVolumePercent()` clamps input and sends a linear level to Godot's Master bus:

```csharp
public void SetMasterVolumePercent(double percent)
{
    MasterVolumePercent = Mathf.Clamp((float)percent, 0.0f, 100.0f);
    if (_masterBusIndex >= 0)
    {
        AudioServer.SetBusVolumeLinear(_masterBusIndex, MasterVolumePercent / 100.0f);
    }
}
```

For example, 100% becomes a linear bus level of `1.0`, 50% becomes `0.5`, and 0% becomes `0.0`. The percentage is a linear audio level; perceived loudness does not necessarily sound half as loud at 50%.

### BGM and SFX mute buttons

The button pictures remain the artwork in `radio.tscn`. `RadioSettings.cs` creates transparent `Button` hitboxes over the visible pixels of each icon, so clicks do not depend on the large transparent margins in the image files.

The toggle methods invert the current state and ask the autoload to mute the matching bus:

```csharp
private void ToggleBgm()
{
    AudioSettingsManager settings = AudioSettingsManager.Instance;
    if (settings == null) return;

    settings.SetBgmMuted(!settings.BgmMuted);
    SyncMuteButtons();
}

private void ToggleSfx()
{
    AudioSettingsManager settings = AudioSettingsManager.Instance;
    if (settings == null) return;

    settings.SetSfxMuted(!settings.SfxMuted);
    SyncMuteButtons();
}
```

The manager applies the mute at the bus, rather than stopping individual tracks:

```csharp
public void SetBgmMuted(bool muted)
{
    BgmMuted = muted;
    if (_bgmBusIndex >= 0)
        AudioServer.SetBusMute(_bgmBusIndex, muted);
}

public void SetSfxMuted(bool muted)
{
    SfxMuted = muted;
    if (_sfxBusIndex >= 0)
        AudioServer.SetBusMute(_sfxBusIndex, muted);
}
```

After the state changes, `SyncMuteButtons()` swaps the picture shown by each `TextureRect` and updates that icon's transparent hitbox:

```csharp
_bgmArtwork.Texture = settings.BgmMuted ? _mutedBgmTexture : _normalBgmTexture;
_sfxArtwork.Texture = settings.SfxMuted ? _mutedSfxTexture : _normalSfxTexture;
SetHitboxToArtwork(_bgmHitbox, _bgmArtwork);
SetHitboxToArtwork(_sfxHitbox, _sfxArtwork);
```

`SyncMuteButtons()` displays `MutedBGM.png` or `MutedSound.png` when its bus is muted, and the normal BGM/SFX icon when it is unmuted. The icon art and mute state therefore stay in sync. Muting one bus does not mute the other; the master slider affects both.

## Background music

All background music covered here uses `AudioSettingsManager.BgmBusName` (`BGM`). That shared route is what lets the radio's BGM mute button and master slider affect the tracks.

### Existing menu, lobby, and gameplay music

- `scenes/menu.tscn` routes `MenuBGM` to `BGM`.
- `scenes/lobby.tscn` routes `LobbyBgm` (the existing `LOBBY.mp3`) to `BGM` at `-16 dB`.
- `GameMusic.cs` chooses and loops the gameplay music by level: morning for the tutorial and morning broadcast, afternoon for the afternoon broadcast, and night for the emergency broadcast. It routes the player to `BGM`, uses `-14 dB`, and fades between tracks over one second.

### Player list music

`assets/sfx/BGM/playerLobby.mp3` is referenced by the `PlayerListBgm` `AudioStreamPlayer` node in `scenes/player_lists.tscn`. It uses the `BGM` bus and starts at `-14 dB`. `PlayerList._Ready()` turns on MP3 looping before starting playback:

```csharp
var playerListBgm = GetNodeOrNull<AudioStreamPlayer>("PlayerListBgm");
if (playerListBgm?.Stream is AudioStreamMP3 lobbyTrack)
{
    lobbyTrack.Loop = true;
    playerListBgm.Play();
}
```

This runs before the network-manager check in `_Ready()`, so the music starts when the scene opens even if that check returns early.

### Game-over music

`GameSession.GameOverRpc()` creates a `GameOverScreen` when the players lose. In `GameOverScreen._Ready()`, the gameplay music is stopped before `assets/sfx/BGM/gameover.mp3` starts. The active gameplay track is stopped and any level-music fade tween is cancelled, preventing a pending tween from restarting that track over the game-over music. The game-over MP3 has looping disabled, so it plays once.

`GameMusic` registers itself in the `game_music` group. `StopActiveBgm()` asks those nodes to cancel their level transition and stop their player, then stops any other player on the BGM bus. The guard in `CheckLevel()` prevents that music node from starting another level track after defeat:

```csharp
public void StopForGameOver()
{
    _stoppedForGameOver = true;
    _fade?.Kill();
    _fade = null;
    _player?.Stop();
}

if (_stoppedForGameOver || GameSession.Instance == null || GameSession.Instance.GameOver)
{
    return;
}
```

```csharp
private void StartGameOverBgm()
{
    const string musicPath = "res://assets/sfx/BGM/gameover.mp3";
    AudioStream stream = GD.Load<AudioStream>(musicPath);
    if (stream == null)
    {
        GD.PushError($"[GameOverScreen] Could not load game-over music at {musicPath}.");
        return;
    }

    if (stream is AudioStreamMP3 mp3)
    {
        mp3.Loop = false;
    }

    var player = new AudioStreamPlayer
    {
        Name = "GameOverBgm",
        Stream = stream,
        VolumeDb = -14.0f,
        Bus = AudioSettingsManager.BgmBusName,
    };
    AddChild(player);
    player.Play();
}
```

The actual method also reports a Godot error if the file cannot be loaded. The player is a child of the game-over screen, so it stops when that screen is removed for a restart or return to the player list.

## Sound effects

The SFX bus is used by the shared `Sfx.Play()` helper, click sounds, tutorial voice playback, and the piano and soundboard audio players. The radio's SFX button mutes all of those together by muting the `SFX` bus.

## Main files

| File | Purpose |
| --- | --- |
| `Scripts/AudioSettingsManager.cs` | Creates the audio buses and controls master volume and bus mute states. |
| `Scripts/RadioSettings.cs` | Makes the authored slider and BGM/SFX icons interactive. |
| `Scripts/LobbyAudioSettings.cs` | Opens and closes the authored radio scene as a lobby overlay. |
| `scenes/settings/radio.tscn` | Authored radio artwork and control positions. |
| `Scripts/PlayerList.cs` and `scenes/player_lists.tscn` | Start and loop player-list music on `BGM`. |
| `Scripts/GameOverScreen.cs` | Stops the gameplay BGM and starts game-over music on `BGM`. |
| `assets/sfx/BGM/playerLobby.mp3` | Player-list background music. |
| `assets/sfx/BGM/gameover.mp3` | Game-over background music. |
| `assets/images/audio_settings/settingsIcon.png` | Current lobby settings icon. |
