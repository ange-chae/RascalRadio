using Godot;

// Faceplate details drawn behind the piano controls: corner screws and faint side bars.
public partial class PianoFaceDecor : Control
{
    public bool ShowBars { get; set; } = true;   // false = just the screws

    // bar heights in pixels, drawn on both sides of the lamp
    private static readonly float[] LeftBars = { 52f, 34f, 24f };
    private static readonly float[] RightBars = { 30f, 50f, 64f, 78f };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        float w = Size.X, h = Size.Y;

        if (ShowBars)
        {
            float baseY = h * 0.60f;
            var bar = new Color("E3C28F", 0.22f);
            for (int i = 0; i < LeftBars.Length; i++)
                DrawRect(new Rect2(w * 0.07f + i * 14f, baseY - LeftBars[i], 5f, LeftBars[i]), bar);
            for (int i = 0; i < RightBars.Length; i++)
                DrawRect(new Rect2(w * 0.80f + i * 14f, baseY - RightBars[i], 5f, RightBars[i]), bar);
        }

        DrawScrew(new Vector2(15, 15));
        DrawScrew(new Vector2(w - 15, 15));
        DrawScrew(new Vector2(15, h - 15));
        DrawScrew(new Vector2(w - 15, h - 15));
    }

    private void DrawScrew(Vector2 p)
    {
        DrawCircle(p, 6f, new Color("2A1F16"));
        DrawCircle(p, 4.5f, new Color("B98F58"));
        DrawLine(p + new Vector2(-3f, 1.5f), p + new Vector2(3f, -1.5f), new Color("5A4128"), 1.5f, true);
    }
}