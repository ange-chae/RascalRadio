using Godot;
using System;
using System.Linq;

namespace RascalRadio.Modules
{
    /// <summary>
    /// Operator side of the Memory module. Simon-style: 4 colored buttons, one blinks.
    /// The Technician reads the manual (MemoryManual) and tells the Operator which button to press.
    /// All UI is built in code.
    /// </summary>
    public partial class MemoryModule : Control
    {
        [Signal] public delegate void SolvedEventHandler();
        [Signal] public delegate void StrikeEventHandler(int strikes);
        [Signal] public delegate void FailedEventHandler();

        public bool PenaltiesEnabled = true; // false = tutorial mode: wrong press just says "try again"
        public int MaxStrikes = 3;
        public bool ShowLog = false;         // true = show your past presses (easier, good for tutorial)
        public bool UseOwnStrikes = true;    // false = the game counts strikes, so the module never locks itself
        static readonly Color Gold = Color.FromHtml("#FFD36B");
        static readonly Color Good = Color.FromHtml("#3FA34D");
        static readonly Color Bad = Color.FromHtml("#E4572E");

        readonly Random _rng = new Random();
        readonly Button[] _btns = new Button[4];
        readonly Label[] _lamps = new Label[5];
        readonly int[] _layout = new int[4];   // position -> color index
        readonly int[] _histPos = new int[6];  // by stage number
        readonly int[] _histColor = new int[6];
        Label _strikeLabel, _msg, _log;
        Godot.Timer _blink;
        int _stage, _strikes, _display, _blinkPos;
        bool _locked, _lit;

        public override void _Ready()
        {
            CustomMinimumSize = new Vector2(420, 560);
            BuildUi();
            Restart();
        }

        public void Restart()
        {
            _stage = 1; _strikes = 0;
            Say("Watch which button blinks, then ask the Technician.", MemoryRules.Ink);
            NewRound();
        }

        // ---------- UI (hard-coded) ----------
        Label MakeLabel(string text, int size, HorizontalAlignment align)
        {
            var l = new Label { Text = text, HorizontalAlignment = align };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", MemoryRules.Ink);
            return l;
        }

        void BuildUi()
        {
            var panel = new PanelContainer();
            panel.SetAnchorsPreset(LayoutPreset.FullRect);
            panel.AddThemeStyleboxOverride("panel", MemoryRules.Box(MemoryRules.Cream, MemoryRules.Ink, 4, 18));
            AddChild(panel);

            var margin = new MarginContainer();
            foreach (var side in new[] { "left", "right", "top", "bottom" })
                margin.AddThemeConstantOverride("margin_" + side, 16);
            panel.AddChild(margin);

            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 12);
            margin.AddChild(col);

            col.AddChild(MakeLabel("MEMORY MODULE", 26, HorizontalAlignment.Center));

            var lampRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            lampRow.AddThemeConstantOverride("separation", 8);
            for (int i = 0; i < 5; i++)
            {
                var l = MakeLabel((i + 1).ToString(), 16, HorizontalAlignment.Center);
                l.VerticalAlignment = VerticalAlignment.Center;
                l.CustomMinimumSize = new Vector2(44, 44);
                _lamps[i] = l;
                lampRow.AddChild(l);
            }
            col.AddChild(lampRow);

            _strikeLabel = MakeLabel("", 16, HorizontalAlignment.Center);
            col.AddChild(_strikeLabel);

            var grid = new GridContainer { Columns = 2, SizeFlagsVertical = SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", 12);
            grid.AddThemeConstantOverride("v_separation", 12);
            for (int p = 0; p < 4; p++)
            {
                int pos = p;
                var b = new Button
                {
                    CustomMinimumSize = new Vector2(170, 120),
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    SizeFlagsVertical = SizeFlags.ExpandFill
                };
                b.AddThemeFontSizeOverride("font_size", 20);
                foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
                    b.AddThemeColorOverride(n, Colors.White);
                b.AddThemeColorOverride("font_outline_color", MemoryRules.Ink);
                b.AddThemeConstantOverride("outline_size", 6);
                b.Pressed += () => OnPress(pos);
                _btns[p] = b;
                grid.AddChild(b);
            }
            col.AddChild(grid);

            _msg = MakeLabel("", 18, HorizontalAlignment.Center);
            _msg.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _msg.CustomMinimumSize = new Vector2(0, 52);
            col.AddChild(_msg);

            _log = MakeLabel("", 14, HorizontalAlignment.Center);
            _log.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            col.AddChild(_log);

            _blink = new Godot.Timer { OneShot = true };
            _blink.Timeout += OnBlink;
            AddChild(_blink);
        }

        void Paint(int pos, bool lit)
        {
            var c = MemoryRules.ColorValues[_layout[pos]];
            var fill = lit ? c : c.Darkened(0.55f);
            var border = lit ? Colors.White : MemoryRules.Ink;
            int bw = lit ? 8 : 4;
            var b = _btns[pos];
            var normal = MemoryRules.Box(fill, border, bw, 16);
            b.AddThemeStyleboxOverride("normal", normal);
            b.AddThemeStyleboxOverride("focus", normal);
            b.AddThemeStyleboxOverride("hover", MemoryRules.Box(fill.Lightened(0.15f), border, bw, 16));
            b.AddThemeStyleboxOverride("pressed", MemoryRules.Box(c, Colors.White, 8, 16));
        }

        void Refresh()
        {
            for (int i = 0; i < 5; i++)
            {
                int n = i + 1;
                var fill = n < _stage ? Good : n == _stage ? Gold : MemoryRules.Cream;
                _lamps[i].Text = n < _stage ? "OK" : n.ToString();
                _lamps[i].AddThemeStyleboxOverride("normal", MemoryRules.Box(fill, MemoryRules.Ink, 3, 22));
                _lamps[i].AddThemeColorOverride("font_color", n < _stage ? Colors.White : MemoryRules.Ink);
            }
            _strikeLabel.Text = "STATIC  " + new string('X', _strikes) + new string('-', Math.Max(0, MaxStrikes - _strikes));
            _strikeLabel.Visible = PenaltiesEnabled && UseOwnStrikes;
            var parts = Enumerable.Range(1, Math.Min(_stage, 6) - 1)
                .Select(s => $"S{s}: POS {_histPos[s] + 1} {MemoryRules.ColorNames[_histColor[s]]}");
            _log.Text = ShowLog && _stage > 1 ? "LOG  " + string.Join("  |  ", parts) : "";
        }

        void Say(string text, Color c)
        {
            if (_msg == null) return;
            _msg.Text = text;
            _msg.AddThemeColorOverride("font_color", c);
        }

        // ---------- Game logic ----------
        void NewRound()
        {
            _display = _rng.Next(4); // the color that will blink
            Enumerable.Range(0, 4).OrderBy(_ => _rng.Next()).ToArray().CopyTo(_layout, 0);
            _blinkPos = Array.IndexOf(_layout, _display);
            for (int p = 0; p < 4; p++)
            {
                int c = _layout[p];
                _btns[p].Text = $"{MemoryRules.Shapes[c]}\n{MemoryRules.ColorNames[c]}\nPOS {p + 1}";
                Paint(p, false);
            }
            _locked = false; _lit = false;
            Refresh();
            _blink.Start(0.6);
        }

        void OnBlink()
        {
            if (_locked) return;
            _lit = !_lit;
            Paint(_blinkPos, _lit);
            _blink.Start(_lit ? 0.55 : 0.85); // short flash, longer pause
        }

        int Target()
        {
            var r = MemoryRules.Table[_stage - 1][_display];
            switch (r.Kind)
            {
                case RuleKind.Position: return r.Value;
                case RuleKind.Label: return Array.IndexOf(_layout, r.Value);
                case RuleKind.SamePosition: return _histPos[r.Value];
                default: return Array.IndexOf(_layout, _histColor[r.Value]);
            }
        }

        SignalAwaiter Delay(double seconds) =>
            ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

        async void OnPress(int pos)
        {
            if (_locked) return;
            _locked = true;
            _blink.Stop();
            Paint(_blinkPos, false);
            Paint(pos, true); // Simon-style light-up feedback

            if (pos == Target())
            {
                _histPos[_stage] = pos;
                _histColor[_stage] = _layout[pos];
                if (_stage == MemoryRules.Stages)
                {
                    _stage = 6; Refresh();
                    Say("ON AIR! Memory module solved.", Good);
                    EmitSignal(SignalName.Solved);
                    return;
                }
                Say($"Locked in! Stage {_stage + 1} next.", Good);
                await Delay(0.8);
                if (!IsInstanceValid(this)) return;
                _stage++;
                NewRound();
                return;
            }

            if (!PenaltiesEnabled)
            {
                Say("Wrong button. Check the manual and try again.", Bad);
                await Delay(0.8);
                if (!IsInstanceValid(this)) return;
                Paint(pos, false);
                _locked = false; _lit = false;
                _blink.Start(0.4);
                return;
            }

            _strikes++;
            EmitSignal(SignalName.Strike, _strikes);
            Refresh();
            if (UseOwnStrikes && _strikes >= MaxStrikes)
            {
                Say("Signal lost! Too much static.", Bad);
                EmitSignal(SignalName.Failed);
                return; // stays locked until Restart()
            }
            Say("Static! Back to stage 1 with a new signal.", Bad);
            await Delay(1.0);
            if (!IsInstanceValid(this)) return;
            _stage = 1;
            NewRound();
        }
    }
}
