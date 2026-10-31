using Godot;

public partial class CassetteSelector : VBoxContainer
{
    private Label _lblLevelName;
    private TextureRect _texCassette;

    public override void _Ready()
    {
        _lblLevelName = FindChild("LblLevelName", true, false) as Label;
        _texCassette = FindChild("TexCassette", true, false) as TextureRect;
    }

    public void UpdateTapeVisual(string levelName, Texture2D tapeTexture)
    {
        if (_lblLevelName != null) _lblLevelName.Text = levelName;
        if (_texCassette != null && tapeTexture != null) _texCassette.Texture = tapeTexture;
    }
}