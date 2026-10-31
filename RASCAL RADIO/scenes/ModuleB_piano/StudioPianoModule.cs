using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Studio Piano module logic (Module B, manual page B1). No UI in here.
/// In a LAN game this node lives on the HOST: clients send RequestKey / RequestOctaveStep,
/// the host validates and broadcasts the results, and the host decides when TriggerFault() happens.
/// </summary>
public partial class StudioPianoModule : Node
{
	public enum PianoStatus { Ready, Working, Check, Complete, Retry }

	public readonly record struct PianoState(
		PianoStatus Status, int CueIndex, int Octave, float TimeLeft, bool InFault, int FaultIndex);

	public const string ModuleId = "studio_piano";
	public const string ManualPageId = "B1";
	private const double RetryDisplayTime = 0.8;

	// Manual: "If the piano keys flash red, play notes in descending order (B A G F E D C)."
	private static readonly StudioPianoData.Cue[] FaultSequence = StudioPianoData.GetRecalibration();

	// Plain C# events: type-safe, and the UI is C# too.
	public event Action Configured;
	public event Action<PianoStatus> StatusChanged;
	public event Action<int, int, StudioPianoData.Cue> CueChanged; // index, total, cue
	public event Action<int> OctaveChanged;
	public event Action<string, bool> KeyResult; // letter, correct
	public event Action<float> TimeChanged;
	public event Action<bool> FaultChanged; // true = keys flash red, false = cleared
	public event Action<int, int> FaultProgressChanged; // notes done, total
	public event Action Completed;
	public event Action TimedOut;

	public bool Active { get; private set; }
	public string SequenceName { get; private set; } = "";
	public bool ShowNotes { get; private set; } = true;
	public bool UseOctave { get; private set; }
	public PianoStatus Status { get; private set; } = PianoStatus.Ready;
	public List<StudioPianoData.Cue> Cues { get; private set; } = new();
	public int CueIndex { get; private set; }
	public int Octave { get; private set; } // -1, 0, 1
	public bool InFault { get; private set; }
	public int FaultIndex { get; private set; }
	public float TimeLimit { get; private set; } // 0 = no timer
	public float TimeLeft { get; private set; }

	private bool _running;

	private bool CanAcceptInput => Active && _running;

	/// <summary>
	/// Level config. showNotes = false means the Operator only sees the jingle NAME and the
	/// Technician must read the notes from the manual table. useOctave = true enforces the octave.
	/// </summary>
	public void Configure(
		string sequenceName,
		IEnumerable<StudioPianoData.Cue> cues,
		bool useOctave = true,
		float timeLimit = 0f,
		bool showNotes = true)
	{
		SequenceName = sequenceName;
		Cues = new List<StudioPianoData.Cue>(cues);
		UseOctave = useOctave;
		TimeLimit = timeLimit;
		ShowNotes = showNotes;
		Reset();
		Configured?.Invoke();
	}

	public void SetActive(bool value)
	{
		Active = value;
		if (!Active)
		{
			_running = false;
		}
	}

	public void Start()
	{
		if (!Active || Cues.Count == 0)
		{
			return;
		}
		_running = true;
		TimeLeft = TimeLimit;
		SetStatus(PianoStatus.Working);
		EmitCue();
		TimeChanged?.Invoke(TimeLeft);
	}

	public void Reset()
	{
		bool wasInFault = InFault;
		_running = false;
		InFault = false;
		FaultIndex = 0;
		CueIndex = 0;
		Octave = 0;
		TimeLeft = TimeLimit;
		SetStatus(PianoStatus.Ready);
		if (wasInFault)
		{
			FaultChanged?.Invoke(false);
		}
		OctaveChanged?.Invoke(Octave);
		EmitCue();
		TimeChanged?.Invoke(TimeLeft);
	}

	/// <summary>
	/// Host calls this for the "keys flash red" sabotage. The Operator must play B A G F E D C
	/// (descending) to clear it, then the interrupted sequence continues where it left off.
	/// The sequence timer is paused during a fault so the fault is a puzzle, not a surprise loss.
	/// </summary>
	public void TriggerFault()
	{
		if (!CanAcceptInput || InFault)
		{
			return;
		}
		InFault = true;
		FaultIndex = 0;
		SetStatus(PianoStatus.Check);
		FaultChanged?.Invoke(true);
		FaultProgressChanged?.Invoke(FaultIndex, FaultSequence.Length);
	}

	/// <summary>Called when a player presses a key. Wrong or late presses never end the round.</summary>
	public void RequestKey(string letter)
	{
		if (!CanAcceptInput)
		{
			return;
		}

		if (InFault)
		{
			HandleFaultKey(letter);
			return;
		}

		var cue = Cues[CueIndex];
		bool letterOk = letter == cue.Letter;
		bool octaveOk = !UseOctave || Octave == cue.Octave;

		if (letterOk && octaveOk)
		{
			KeyResult?.Invoke(letter, true);
			CueIndex++;
			if (CueIndex >= Cues.Count)
			{
				_running = false;
				SetStatus(PianoStatus.Complete);
				Completed?.Invoke();
			}
			else
			{
				SetStatus(PianoStatus.Working);
				EmitCue();
			}
		}
		else
		{
			KeyResult?.Invoke(letter, false);
			SetStatus(PianoStatus.Retry);
			GetTree().CreateTimer(RetryDisplayTime).Timeout += OnRetryTimeout;
		}
	}

	/// <summary>direction is -1 or +1. Ignored unless this sequence uses octave controls.</summary>
	public void RequestOctaveStep(int direction)
	{
		if (!UseOctave || !CanAcceptInput)
		{
			return;
		}
		Octave = Mathf.Clamp(Octave + direction, -1, 1);
		OctaveChanged?.Invoke(Octave);
	}

	/// <summary>Snapshot for syncing clients later (host sends it, clients display it).</summary>
	public PianoState GetState() => new(Status, CueIndex, Octave, TimeLeft, InFault, FaultIndex);

	public override void _Process(double delta)
	{
		// Timer is paused while a fault is being recalibrated.
		if (!_running || TimeLimit <= 0f || InFault)
		{
			return;
		}

		TimeLeft = Mathf.Max(TimeLeft - (float)delta, 0f);
		TimeChanged?.Invoke(TimeLeft);

		if (TimeLeft <= 0f)
		{
			_running = false;
			SetStatus(PianoStatus.Check); // CHECK = time ran out, reread the cue and reset
			TimedOut?.Invoke();
		}
	}

	private void HandleFaultKey(string letter)
	{
		if (letter == FaultSequence[FaultIndex].Letter)
		{
			KeyResult?.Invoke(letter, true);
			FaultIndex++;
			FaultProgressChanged?.Invoke(FaultIndex, FaultSequence.Length);

			if (FaultIndex >= FaultSequence.Length)
			{
				InFault = false;
				FaultIndex = 0;
				SetStatus(PianoStatus.Working);
				FaultChanged?.Invoke(false);
				EmitCue(); // restore the interrupted cue
			}
		}
		else
		{
			// Gentle retry: keep progress, no penalty.
			KeyResult?.Invoke(letter, false);
			SetStatus(PianoStatus.Retry);
			GetTree().CreateTimer(RetryDisplayTime).Timeout += OnRetryTimeout;
		}
	}

	private void OnRetryTimeout()
	{
		if (_running && Status == PianoStatus.Retry)
		{
			SetStatus(InFault ? PianoStatus.Check : PianoStatus.Working);
		}
	}

	private void EmitCue()
	{
		if (CueIndex < Cues.Count)
		{
			CueChanged?.Invoke(CueIndex, Cues.Count, Cues[CueIndex]);
		}
	}

	private void SetStatus(PianoStatus newStatus)
	{
		Status = newStatus;
		StatusChanged?.Invoke(Status);
	}
}
