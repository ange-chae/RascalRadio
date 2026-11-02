using Godot;

namespace RascalRadio.Modules
{
    /// <summary>Technician side: the printed manual for the Memory module. Built in code from MemoryRules.</summary>
    public partial class MemoryManual : Control
    {
        public override void _Ready()
        {
            CustomMinimumSize = new Vector2(460, 560);

            var panel = new PanelContainer();
            panel.SetAnchorsPreset(LayoutPreset.FullRect);
            panel.AddThemeStyleboxOverride("panel", MemoryRules.Box(MemoryRules.Cream, MemoryRules.Ink, 4, 18));
            AddChild(panel);

            var margin = new MarginContainer();
            foreach (var side in new[] { "left", "right", "top", "bottom" })
                margin.AddThemeConstantOverride("margin_" + side, 16);
            panel.AddChild(margin);

            var scroll = new ScrollContainer();
            margin.AddChild(scroll);

            var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            col.AddThemeConstantOverride("separation", 8);
            scroll.AddChild(col);

            col.AddChild(Lbl("TECHNICIAN'S MANUAL: MEMORY", 22, MemoryRules.Ink, true));
            col.AddChild(Lbl("The button that BLINKS is the signal. Find the stage, then the blinking color, and tell the Operator what to press.", 15, MemoryRules.Ink, true));
            col.AddChild(Lbl("Positions: 1 top-left | 2 top-right | 3 bottom-left | 4 bottom-right. Wrong press = static, back to stage 1.", 15, MemoryRules.Ink, true));

            for (int s = 0; s < MemoryRules.Stages; s++)
            {
                col.AddChild(Lbl($"STAGE {s + 1}", 20, MemoryRules.Ink, false));
                for (int d = 0; d < 4; d++)
                {
                    var row = new HBoxContainer();
                    row.AddThemeConstantOverride("separation", 10);

                    var chip = Lbl($"{MemoryRules.Tag(d)} BLINKS", 15, Colors.White, false);
                    chip.CustomMinimumSize = new Vector2(160, 34);
                    chip.HorizontalAlignment = HorizontalAlignment.Center;
                    chip.VerticalAlignment = VerticalAlignment.Center;
                    chip.AddThemeStyleboxOverride("normal", MemoryRules.Box(MemoryRules.ColorValues[d], MemoryRules.Ink, 2, 12));
                    chip.AddThemeColorOverride("font_outline_color", MemoryRules.Ink);
                    chip.AddThemeConstantOverride("outline_size", 4);
                    row.AddChild(chip);

                    var desc = Lbl(MemoryRules.Describe(MemoryRules.Table[s][d]), 15, MemoryRules.Ink, true);
                    desc.VerticalAlignment = VerticalAlignment.Center;
                    row.AddChild(desc);
                    col.AddChild(row);
                }
            }
        }

        static Label Lbl(string text, int size, Color color, bool wrap)
        {
            var l = new Label { Text = text };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color);
            if (wrap)
            {
                l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            }
            return l;
        }
    }
}
