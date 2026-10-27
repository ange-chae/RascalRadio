using System;
using System.Collections.Generic;
using RascalRadio.Core.Models;

namespace RascalRadio.Core.Systems;

/// <summary>Host-only tutorial state machine. No Godot types. Never applies penalties.</summary>
public class TutorialLogic
{
	public bool PenaltiesEnabled => false;
	public TutorialStep Step { get; private set; } = TutorialStep.MeetStudio;
	public int PlayersNeeded { get; }
	public int AckCount => _acked.Count;
	public event Action<TutorialStep> StepChanged;

	private readonly HashSet<long> _acked = new();

	public TutorialLogic(int playersNeeded) { PlayersNeeded = Math.Max(1, playersNeeded); }

	// Returns true only when this report moved the tutorial to the next step.
	public bool Report(TutorialAction action, long peerId)
	{
		if (!Matches(Step, action)) return false;

		if (Step == TutorialStep.MeetStudio)
		{
			_acked.Add(peerId);
			if (_acked.Count < PlayersNeeded) return false;
		}

		Step = (TutorialStep)((int)Step + 1);
		StepChanged?.Invoke(Step);
		return true;
	}

	private static bool Matches(TutorialStep s, TutorialAction a) => (s, a) switch
	{
		(TutorialStep.MeetStudio, TutorialAction.Acknowledged) => true,
		(TutorialStep.PracticeCaller, TutorialAction.CallerDecided) => true,
		(TutorialStep.GoLive, TutorialAction.PhraseMatched) => true,
		(TutorialStep.KeypadModule, TutorialAction.KeypadSolved) => true,
		(TutorialStep.SuspiciousCaller, TutorialAction.RaccoonRejected) => true,
		_ => false,
	};
}
