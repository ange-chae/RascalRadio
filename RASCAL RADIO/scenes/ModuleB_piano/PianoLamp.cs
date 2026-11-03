using Godot;

// A round indicator lamp in a recessed well: dark bezel, glass lens, soft glow. Draws itself.
public partial class PianoLamp : Control
{
    private Color _color = Colors.Gray;
    private bool _lit;
    private bool _blink;
    private float _kick;   // short shake when a wrong key is pressed

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(140, 140);
    }

    public void TurnOn(Color color, bool blink = false)
    {
        _color = color; _lit = true; _blink = blink;
        QueueRedraw();
    }

    public void TurnOff()
    {
        _lit = false; _blink = false;
        QueueRedraw();
    }

    public void Nudge()
    {
        _kick = 1f;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_kick > 0f) _kick = Mathf.Max(0f, _kick - (float)delta * 4f);
        if (_lit || _kick > 0f) QueueRedraw();   // keeps the glow pulsing
    }

    public override void _Draw()
    {
        float t = Time.GetTicksMsec() / 1000f;
        float outer = Mathf.Min(Size.X, Size.Y) / 2f;
        float r = outer - 20f;            // bezel radius (the glow lives in the ring between bezel and well)
        float lens = r - 10f;
        var still = Size / 2f;
        var center = still;
        center.X += Mathf.Sin(t * 70f) * 6f * _kick;

        float level = !_lit ? 0f
            : _blink ? (Mathf.Sin(t * 9f) > 0f ? 1f : 0.2f)      // blinking (fault)
            : 0.9f + 0.1f * Mathf.Sin(t * 2.5f);                  // gentle breathing

        // the recessed well the lamp sits in
        DrawCircle(still, outer, new Color("1E1A17"));
        DrawArc(still, outer - 1f, 0f, Mathf.Tau, 64, new Color("4A3D31"), 2f, true);

        // glow
        if (_lit)
            for (int i = 8; i >= 1; i--)
                DrawCircle(center, r + 20f * i / 8f, new Color(_color, 0.05f * level));

        // bezel
        DrawCircle(center, r, new Color("15110F"));
        DrawArc(center, r - 1.5f, 0f, Mathf.Tau, 64, new Color("7A6650"), 3f, true);

        // lens
        Color lensColor = _lit ? _color.Darkened(0.25f).Lerp(_color.Lightened(0.1f), level) : new Color("2A2420");
        DrawCircle(center, lens, lensColor);
        if (_lit) DrawCircle(center, lens * 0.6f, new Color(_color.Lightened(0.5f), 0.55f * level));
        DrawArc(center, lens, 0f, Mathf.Tau, 48, new Color("00000066"), 2f, true);

        // glass highlight
        DrawCircle(center + new Vector2(-lens * 0.35f, -lens * 0.38f), lens * 0.18f, new Color(1f, 1f, 1f, _lit ? 0.5f : 0.15f));
    }
}