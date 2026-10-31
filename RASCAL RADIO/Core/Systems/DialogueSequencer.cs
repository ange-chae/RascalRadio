using System;

namespace RascalRadio.Core.Systems;

public class DialogueSequencer
{
    private string[] _lines = Array.Empty<string>();
    private int _index;

    public bool Finished => _index >= _lines.Length;
    public string Current => Finished ? "" : _lines[_index];
    public bool IsLastLine => _index == _lines.Length - 1;

    public event Action<string> LineShown;
    public event Action Completed;

    public void Start(string[] lines)
    {
        _lines = lines;
        _index = 0;
        if (_lines.Length == 0) { Completed?.Invoke(); return; }
        LineShown?.Invoke(Current);
    }

    public void Advance()
    {
        if (Finished) return;
        _index++;
        if (Finished) Completed?.Invoke();
        else LineShown?.Invoke(Current);
    }
}