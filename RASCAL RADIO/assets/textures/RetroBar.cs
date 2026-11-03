using Godot;

// Segmented retro bar. Replaces the look of a ProgressBar; same Value / MaxValue properties.
[GlobalClass]
public partial class RetroBar : ProgressBar
{
    [Export] public int Segments = 20;       // number of blocks
    [Export] public int DangerBelow = 30;    // blocks turn RED (and flash) at or under this %
    [Export] public int WarnBelow = 60;      // blocks turn YELLOW under this %

    private static readonly Color Frame = new("1FA06A");   // outline
    private static readonly Color Back = new("12111A");    // inside of the screen
    private static readonly Color Off = new("143B30");     // unlit blocks
    private static readonly Color Good = new("2FD6A0");    // green
    private static readonly Color Warn = new("FFD25A");    // yellow
    private static readonly Color Danger = new("E5484D");  // red

    public override void _Ready()
    {
        ShowPercentage = false;   // we draw our own
        if (Segments <= 0) Segments = 20;
        if (DangerBelow <= 0) DangerBelow = 30;
        if (WarnBelow <= 0) WarnBelow = 60;
        if (CustomMinimumSize.Y < 24f)
            CustomMinimumSize = new Vector2(CustomMinimumSize.X, 24f);
        var empty = new StyleBoxEmpty();
        AddThemeStyleboxOverride("background", empty);
        AddThemeStyleboxOverride("fill", empty);
        ValueChanged += _ => QueueRedraw();
        Resized += QueueRedraw;
    }

    private double Percent => MaxValue > MinValue ? (Value - MinValue) / (MaxValue - MinValue) * 100.0 : 0;

    public override void _Process(double delta)
    {
        if (Percent <= DangerBelow) QueueRedraw();   // keep flashing while in danger
    }

    public override void _Draw()
    {
        var size = Size;

        var back = new StyleBoxFlat { BgColor = Back, BorderColor = Frame };
        back.SetBorderWidthAll(2);
        back.SetCornerRadiusAll(4);
        DrawStyleBox(back, new Rect2(Vector2.Zero, size));

        double pct = Percent;
        bool danger = pct <= DangerBelow;
        Color c = danger ? Danger : pct < WarnBelow ? Warn : Good;
        if (danger) c.A = 0.65f + 0.35f * Mathf.Sin(Time.GetTicksMsec() * 0.012f);

        const float pad = 5f, gap = 3f;
        float segW = (size.X - pad * 2 - gap * (Segments - 1)) / Segments;
        float segH = size.Y - pad * 2;

        int lit = Mathf.RoundToInt((float)(pct / 100.0) * Segments);
        if (pct > 0 && lit == 0) lit = 1;   // never look empty while there is some left

        for (int i = 0; i < Segments; i++)
        {
            float x = pad + i * (segW + gap);
            DrawRect(new Rect2(x, pad, segW, segH), i < lit ? c : Off);
        }

        //if (ShowValue)
        //{
        //    var font = GetThemeFont("font");
        //    int fs = GetThemeFontSize("font_size");
        //    if (fs <= 0) fs = 22;
        //    string text = $"{Mathf.RoundToInt((float)pct)}%";
        //    var pos = new Vector2(0, size.Y / 2 + fs * 0.35f);
        //    DrawString(font, pos, text, HorizontalAlignment.Center, size.X, fs, Colors.White);
        //}
    }
}