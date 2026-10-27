using System;
using Godot;

public static class ConfirmPopup
{
    public static void Show(Node parent, string title, string text, string okText, string cancelText, Action onOk)
    {
        if (parent.HasNode("ConfirmPopup")) return;   // never stack two

        var layer = new CanvasLayer { Layer = 90, Name = "ConfirmPopup" };
        parent.AddChild(layer);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f) };   // also blocks clicks behind it
        layer.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        dim.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
        panel.AddThemeStyleboxOverride("panel", UiStyle.Card());
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);

        var t = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center };
        t.AddThemeFontSizeOverride("font_size", 28);
        t.AddThemeColorOverride("font_color", UiStyle.Ink);
        box.AddChild(t);

        var msg = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        msg.AddThemeFontSizeOverride("font_size", 16);
        msg.AddThemeColorOverride("font_color", UiStyle.Ink);
        box.AddChild(msg);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 16);
        box.AddChild(row);

        var cancel = new Button { Text = cancelText, CustomMinimumSize = new Vector2(130, 44) };
        UiStyle.StyleButton(cancel, UiStyle.Field);
        cancel.Pressed += layer.QueueFree;
        row.AddChild(cancel);

        var ok = new Button { Text = okText, CustomMinimumSize = new Vector2(130, 44) };
        UiStyle.StyleButton(ok, UiStyle.Yellow);
        ok.Pressed += () => { layer.QueueFree(); onOk(); };
        row.AddChild(ok);
    }
}