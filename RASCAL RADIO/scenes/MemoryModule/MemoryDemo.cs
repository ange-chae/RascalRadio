using Godot;

namespace RascalRadio.Modules
{
    /// <summary>Test scene: Operator module on the left, Technician manual on the right.</summary>
    public partial class MemoryDemo : Control
    {
        public override void _Ready()
        {
            var bg = new ColorRect { Color = Color.FromHtml("#6B4A38") };
            bg.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(bg);

            var margin = new MarginContainer();
            margin.SetAnchorsPreset(LayoutPreset.FullRect);
            foreach (var side in new[] { "left", "right", "top", "bottom" })
                margin.AddThemeConstantOverride("margin_" + side, 24);
            AddChild(margin);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 24);
            margin.AddChild(row);

            var module = new MemoryModule { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            module.Solved += () => GD.Print("Memory module solved");
            module.Strike += n => GD.Print($"Memory strike {n}");
            module.Failed += () => GD.Print("Memory module failed");
            row.AddChild(module);

            row.AddChild(new MemoryManual { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        }
    }
}
