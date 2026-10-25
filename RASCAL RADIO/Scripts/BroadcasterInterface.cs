using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;

public partial class BroadcasterInterface : PopupFrame
{
	private static readonly string[] Sentence =
		{ "Welcome", "back", "listeners", "your", "stories", "are", "on", "air" };
	private static readonly string[] Decoys = { "tonight", "music" };
	[Export] public double SegmentTime = 20.0;

	private int _idx;
	private double _timeLeft;
	private bool _running;
	private RichTextLabel _target;
	private Label _timer, _status, _meterLabel;
	private HFlowContainer _bank;
	private Button _tryAgain;
	private HBoxContainer _meter;

	public override void _Ready()
	{
		base._Ready();
		_target = GetNode<RichTextLabel>("%TargetLabel");
		_timer = GetNode<Label>("%TimerLabel");
		_status = GetNode<Label>("%StatusLabel");
		_meterLabel = GetNode<Label>("%MeterLabel");
		_bank = GetNode<HFlowContainer>("%WordBank");
		_tryAgain = GetNode<Button>("%TryAgainBtn");
		_meter = GetNode<HBoxContainer>("%Meter");

		GetNode<Button>("%RetryBtn").Pressed += StartSegment;
		_tryAgain.Pressed += StartSegment;
		GetNode<Button>("%AcceptBtn").Pressed += () => GD.Print("ACCEPT pressed");
		GetNode<Button>("%RejectBtn").Pressed += () => GD.Print("REJECT pressed");
		StartSegment();
	}

	private void StartSegment()
	{
		_idx = 0; _timeLeft = SegmentTime; _running = true;
		_tryAgain.Disabled = true;
		_status.Text = "Click the words in order.";
		foreach (Node c in _bank.GetChildren())
			if (c != _tryAgain) c.QueueFree();

		foreach (var w in Sentence.Concat(Decoys).OrderBy(_ => GD.Randi()).ToList())
		{
			var word = w;
			var b = new Button { Text = word, ThemeTypeVariation = "ButtonWord" };
			b.Pressed += () => OnWord(b, word);
			_bank.AddChild(b);
		}
		_bank.MoveChild(_tryAgain, -1);
		Refresh();
	}

	private void OnWord(Button b, string w)
	{
		if (!_running) return;
		if (w == Sentence[_idx])
		{
			_idx++; b.Disabled = true; Refresh();
			if (_idx == Sentence.Length) { _running = false; _status.Text = "ON AIR! Segment complete."; }
		}
		else _timeLeft = Mathf.Max(0, _timeLeft - 2);
	}

	public override void _Process(double delta)
	{
		if (!_running) return;
		_timeLeft -= delta;
		_timer.Text = $"{Mathf.Max(0, Mathf.CeilToInt(_timeLeft))} s";
		if (_timeLeft > 0) return;
		_running = false;
		_status.Text = "Time is up. Press Try again.";
		_tryAgain.Disabled = false;
		foreach (Node c in _bank.GetChildren())
			if (c is Button bb && bb != _tryAgain) bb.Disabled = true;
	}

	private void Refresh()
	{
		var parts = Sentence.Select((w, i) => i < _idx ? $"[color=#0b5a2a]{w}[/color]" : w);
		_target.Text = "[center]" + string.Join(" ", parts) + "[/center]";
		_meterLabel.Text = $"ON AIR meter: {_idx} of {Sentence.Length} words";
		for (int i = 0; i < _meter.GetChildCount(); i++)
			((Control)_meter.GetChild(i)).ThemeTypeVariation = i < _idx ? "SegOn" : "SegOff";
	}
}
