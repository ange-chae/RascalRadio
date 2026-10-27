using Godot;
using RascalRadio.Core.Models;

public partial class OperatorInterface : PopupFrame
{
	private KeypadModule _keypad;

	public override void _Ready()
	{
		base._Ready();

		var gs = GameSession.Instance;
		if (gs == null || !gs.IsTutorial) return;

		AddChild(new TutorialOverlay { RoleName = "Operator" });
		gs.StepChanged += OnStep;
		gs.CallerChanged += OnCaller;
		gs.QuotaChanged += OnQuota;
		gs.KeypadChanged += OnKeypadChanged;

		SetText("%StrikeCount", "none");
		OnQuota();
		OnCaller();
		OnStep(gs.CurrentStep);
	}

	public override void _ExitTree()
	{
		var gs = GameSession.Instance;
		if (gs == null) return;
		gs.StepChanged -= OnStep;
		gs.CallerChanged -= OnCaller;
		gs.QuotaChanged -= OnQuota;
		gs.KeypadChanged -= OnKeypadChanged;
	}

	public void SetCaller(string name, int number, int total, string quote)
	{
		GetNode<Label>("%NameLabel").Text = name;
		GetNode<Label>("%CallerNumLabel").Text = $"Caller {number} of {total}";
		GetNode<Label>("%QuoteLabel").Text = quote;
	}

	public void SetVerdict(bool accept)
	{
		GetNode<Label>("%VerdictLabel").Text = accept ? "ACCEPT" : "REJECT";
	}

	private void OnCaller()
	{
		var gs = GameSession.Instance;
		if (gs.CallerName == "") return;
		// The OPERATOR sees the story and reads it out loud.
		SetCaller(gs.CallerName, gs.CallerNumber, gs.CallerTotal, gs.CallerStory);
	}

	private void OnQuota() =>
		SetText("%QuotaCount", $"{GameSession.Instance.QuotaMet}/{GameSession.Instance.QuotaTarget}");

	private void OnStep(TutorialStep step)
	{
		if (step >= TutorialStep.KeypadModule && _keypad == null) BuildKeypad();
		if (step > TutorialStep.KeypadModule) _keypad?.SetSolved();
	}

	private void OnKeypadChanged() =>
		_keypad?.Refresh(GameSession.Instance.KeypadLine, GameSession.Instance.KeypadStatic);

	private void BuildKeypad()
	{
		var slot = GetNodeOrNull<VBoxContainer>("%Slot1Box");
		if (slot == null) { GD.PrintErr("Slot1Box not found. Rename slot1/slotbox1 to Slot1Box (unique)."); return; }
		foreach (Node c in slot.GetChildren()) c.QueueFree();

		_keypad = new KeypadModule { Line = GameSession.Instance.KeypadLine, StaticFault = GameSession.Instance.KeypadStatic };
		slot.AddChild(_keypad);
	}

	private void SetText(string path, string text)
	{
		var l = GetNodeOrNull<Label>(path);
		if (l != null) l.Text = text;
	}
}
