using System.Collections.Generic;
using Godot;
using RascalRadio.Core.Systems;

// Operator-side Line Override keypad. Built in code and dropped into Studio Desk slot 1.
public partial class KeypadModule : VBoxContainer
{
    [Signal] public delegate void SolvedEventHandler();
    private int _lineValue;
    public int Line
    {
        get => _lineValue;
        set
        {
            _lineValue = value;
            if (_line != null)
                _line.Text = _lineValue > 0 ? $"INCOMING LINE: {_lineValue}" : "No incoming line";
        }
    }

    private bool _staticFaultValue;
    public bool StaticFault
    {
        get => _staticFaultValue;
        set
        {
            _staticFaultValue = value;
            UpdateStatusText();
        }
    }

    private string _currentEntry = "";
    private readonly List<Button> _keys = new();
    private Label _line, _entry, _status;
    private bool _solved;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);

        var title = new Label { Text = "LINE OVERRIDE", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color", UiStyle.Ink);
        AddChild(title);

        var display = new PanelContainer();
        display.AddThemeStyleboxOverride("panel", UiStyle.Screen());
        AddChild(display);
        var displayBox = new VBoxContainer();
        display.AddChild(displayBox);

        _line = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _line.AddThemeFontSizeOverride("font_size", 14);
        _line.AddThemeColorOverride("font_color", UiStyle.Ink);
        displayBox.AddChild(_line);

        _entry = new Label { Text = "_", HorizontalAlignment = HorizontalAlignment.Center };
        _entry.AddThemeFontSizeOverride("font_size", 30);
        _entry.AddThemeColorOverride("font_color", UiStyle.Ink);
        displayBox.AddChild(_entry);

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        AddChild(grid);
        foreach (string key in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "CLEAR", "0", "ROUTE" })
        {
            string k = key;
            var b = new Button { Text = k, CustomMinimumSize = new Vector2(64, 44), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            b.AddThemeFontSizeOverride("font_size", k.Length > 1 ? 13 : 20);
            UiStyle.StyleButton(b, k == "ROUTE" ? UiStyle.Yellow : UiStyle.Cream);
            b.Pressed += () => OnKey(k);
            grid.AddChild(b);
            _keys.Add(b);
        }

        _status = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 48),
        };
        _status.AddThemeFontSizeOverride("font_size", 13);
        _status.AddThemeColorOverride("font_color", UiStyle.Ink);
        AddChild(_status);

        // Apply initial values now that UI nodes exist
        _line.Text = Line > 0 ? $"INCOMING LINE: {Line}" : "No incoming line";
        UpdateStatusText();

        if (GameSession.Instance != null) GameSession.Instance.Feedback += OnFeedback;
    }

    public override void _ExitTree()
    {
        if (GameSession.Instance != null) GameSession.Instance.Feedback -= OnFeedback;
    }

    public void Refresh(int line, bool staticFault)
    {
        Line = line;
        StaticFault = staticFault;
        _currentEntry = "";
        _entry.Text = "_";
    }

    private void UpdateStatusText()
    {
        if (_status == null) return;
        _status.Text = StaticFault
            ? "⚡ STATIC FAULT. Ask the Technician how to clear it."
            : "You don't have the rules. Read the line number to the Technician and ask for the override code.";
    }

    private void OnKey(string key)
    {
        if (_solved) return;

        if (key == "CLEAR")
        {
            _currentEntry = "";
        }
        else if (key == "ROUTE")
        {
            if (_currentEntry.Length > 0) GameSession.Instance.SubmitKeypad(_currentEntry);
            else _status.Text = "↻ Type a code first.";
            _currentEntry = "";
        }
        else
        {
            if (_currentEntry.Length < 6)
            {
                _currentEntry += key;
            }
        }

        _entry.Text = _currentEntry == "" ? "_" : _currentEntry;
    }

    private void OnFeedback(string text, bool success)
    {
        if (_solved || !text.StartsWith(GameSession.KeypadTag)) return;
        _status.Text = text.Substring(GameSession.KeypadTag.Length);
    }

    public void SetSolved()
    {
        if (_solved) return;
        _solved = true;
        foreach (var b in _keys) b.Disabled = true;
        _status.Text = "✔ SOLVED";
        EmitSignal(SignalName.Solved);
    }
}