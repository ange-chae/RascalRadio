using Godot;

public static class UiStyle
{
    public static readonly Color Cream = new("#FAEFD9");
    public static readonly Color Outline = new("#301E2E");
    public static readonly Color Ink = new("#1E1646");
    public static readonly Color Yellow = new("#FFD166");
    public static readonly Color Field = new("#F8E8CF");
    public static readonly Color FieldBorder = new("#E5A872");

    // Cream card with a thick dark outline and a heavier bottom edge, like your menu cards
    public static StyleBoxFlat Card()
    {
        var s = new StyleBoxFlat { BgColor = Cream, BorderColor = Outline };
        s.SetBorderWidthAll(4);
        s.BorderWidthBottom = 7;
        s.SetCornerRadiusAll(10);
        s.SetContentMarginAll(12);
        return s;
    }

    // Like your name input field
    public static StyleBoxFlat Screen()
    {
        var s = new StyleBoxFlat { BgColor = Field, BorderColor = FieldBorder };
        s.SetBorderWidthAll(3);
        s.SetCornerRadiusAll(8);
        s.SetContentMarginAll(10);
        return s;
    }

    private static StyleBoxFlat Key(Color fill, int bottom)
    {
        var s = new StyleBoxFlat { BgColor = fill, BorderColor = Ink };
        s.SetBorderWidthAll(3);
        s.BorderWidthBottom = bottom;
        s.SetCornerRadiusAll(8);
        s.SetContentMarginAll(8);
        return s;
    }

    // Chunky button like Join / Host / Back
    public static void StyleButton(Button b, Color fill)
    {
        b.AddThemeStyleboxOverride("normal", Key(fill, 6));
        b.AddThemeStyleboxOverride("hover", Key(fill.Lightened(0.12f), 6));
        b.AddThemeStyleboxOverride("pressed", Key(fill.Darkened(0.1f), 3));
        b.AddThemeStyleboxOverride("disabled", Key(new Color("#CDBFA8"), 6));
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_disabled_color" })
            b.AddThemeColorOverride(c, Ink);
    }
}