using Godot;

namespace RascalRadio.Modules
{
    public enum RuleKind { Position, Label, SamePosition, SameColor }

    public readonly struct Rule
    {
        public readonly RuleKind Kind;
        public readonly int Value; // Position: 0-3 | Label: color index | Same*: stage number 1-5
        public Rule(RuleKind kind, int value) { Kind = kind; Value = value; }
    }

    /// <summary>Shared data for the Operator module and the Technician manual (edit rules here).</summary>
    public static class MemoryRules
    {
        public const int Stages = 5;
        const int Green = 3;

        // Color index: 0 Red, 1 Blue, 2 Yellow, 3 Green. Shapes keep it readable without color.
        public static readonly string[] ColorNames = { "Red", "Blue", "Yellow", "Green" };
        public static readonly string[] Shapes = { "■", "●", "▲", "★" };
        public static readonly Color[] ColorValues =
        {
            Color.FromHtml("#E4572E"), Color.FromHtml("#3A7BD5"),
            Color.FromHtml("#F2B632"), Color.FromHtml("#3FA34D")
        };
        public static readonly string[] PositionNames = { "top-left", "top-right", "bottom-left", "bottom-right" };
        public static readonly Color Cream = Color.FromHtml("#FFF1D6");
        public static readonly Color Ink = Color.FromHtml("#3B2A20");

        static Rule Pos(int p) => new Rule(RuleKind.Position, p - 1); // written 1-based
        static Rule Lab(int c) => new Rule(RuleKind.Label, c);
        static Rule SamePos(int s) => new Rule(RuleKind.SamePosition, s);
        static Rule SameCol(int s) => new Rule(RuleKind.SameColor, s);

        // Table[stage - 1][blinkingColor] -> what to press. Order: Red, Blue, Yellow, Green blinks.
        public static readonly Rule[][] Table =
        {
            new[] { Pos(2),       Pos(2),       Pos(3),       Pos(4)       },
            new[] { Lab(Green),   SamePos(1),   Pos(1),       SamePos(1)   },
            new[] { SameCol(2),   SameCol(1),   Pos(3),       Lab(Green)   },
            new[] { SamePos(1),   Pos(1),       SamePos(2),   SamePos(2)   },
            new[] { SameCol(1),   SameCol(2),   SameCol(4),   SameCol(3)   },
        };

        public static string Tag(int color) => Shapes[color] + " " + ColorNames[color].ToUpper();

        public static string Describe(Rule r)
        {
            switch (r.Kind)
            {
                case RuleKind.Position: return $"Press position {r.Value + 1} ({PositionNames[r.Value]})";
                case RuleKind.Label: return $"Press the button labeled {Tag(r.Value)}";
                case RuleKind.SamePosition: return $"Press the SAME POSITION you pressed in stage {r.Value}";
                default: return $"Press the SAME COLOR you pressed in stage {r.Value}";
            }
        }

        public static StyleBoxFlat Box(Color bg, Color border, int borderWidth, int radius)
        {
            var s = new StyleBoxFlat { BgColor = bg, BorderColor = border };
            s.SetBorderWidthAll(borderWidth);
            s.SetCornerRadiusAll(radius);
            return s;
        }
    }
}
