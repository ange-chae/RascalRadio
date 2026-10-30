using System;
using System.Collections.Generic;
using Godot;

public partial class SoundboardModule : Node
{
	public enum SoundboardStatus { Ready, Working, Check, Complete, Retry }

	public readonly record struct SoundboardState(SoundboardStatus Status, int Progress, float TimeLeft);

	public const string ModuleId = "soundboard";
	public const string ManualPageId = "C1";
	private const double RetryDisplayTime = 0.8;

	public event Action Configured;
	public event Action<SoundboardStatus> StatusChanged;
	public event Action<int, int> ProgressChanged;
	public event Action<string, bool, string> PadResult;
	public event Action<float> TimeChanged;
	public event Action Completed;
	public event Action TimedOut;

	public bool Active { get; private set; }
	public bool ShowSequence { get; private set; } = true;
	public SoundboardStatus Status { get; private set; } = SoundboardStatus.Ready;
	public List<string> Sequence { get; private set; } = new();
	public int Progress { get; private set; }
	public float TimeLimit { get; private set; }
	public float TimeLeft { get; private set; }

	private bool _running;

	private bool CanAcceptInput => Active && _running;

	public void Configure(IEnumerable<string> sequence, float timeLimit = 0f, bool showSequence = true)
	{
		Sequence = new List<string>();
		foreach (string padId in sequence)
		{
			if (SoundboardData.IsPad(padId))
			{
				Sequence.Add(padId);
			}
			else
			{
				GD.PushWarning($"[Soundboard] Unknown pad '{padId}' skipped.");
			}
		}
		TimeLimit = timeLimit;
		ShowSequence = showSequence;
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
		if (!Active || Sequence.Count == 0)
		{
			return;
		}
		_running = true;
		TimeLeft = TimeLimit;
		SetStatus(SoundboardStatus.Working);
		ProgressChanged?.Invoke(Progress, Sequence.Count);
		TimeChanged?.Invoke(TimeLeft);
	}

	public void Reset()
	{
		_running = false;
		Progress = 0;
		TimeLeft = TimeLimit;
		SetStatus(SoundboardStatus.Ready);
		ProgressChanged?.Invoke(Progress, Sequence.Count);
		TimeChanged?.Invoke(TimeLeft);
	}

	public void ResetSequence()
	{
		if (!CanAcceptInput)
		{
			return;
		}
		Progress = 0;
		SetStatus(SoundboardStatus.Working);
		ProgressChanged?.Invoke(Progress, Sequence.Count);
	}

	public void RequestPad(string padId)
	{
		if (!CanAcceptInput || !SoundboardData.IsPad(padId))
		{
			return;
		}

		string expected = Sequence[Progress];

		if (padId == expected)
		{
			Progress++;
			PadResult?.Invoke(padId, true, null);
			ProgressChanged?.Invoke(Progress, Sequence.Count);

			if (Progress >= Sequence.Count)
			{
				_running = false;
				SetStatus(SoundboardStatus.Complete);
				Completed?.Invoke();
			}
			else
			{
				SetStatus(SoundboardStatus.Working);
			}
		}
		else
		{
			PadResult?.Invoke(padId, false, ShowSequence ? expected : null);
			SetStatus(SoundboardStatus.Retry);
			GetTree().CreateTimer(RetryDisplayTime).Timeout += OnRetryTimeout;
		}
	}

	public SoundboardState GetState() => new(Status, Progress, TimeLeft);

	public override void _Process(double delta)
	{
		if (!_running || TimeLimit <= 0f)
		{
			return;
		}

		TimeLeft = Mathf.Max(TimeLeft - (float)delta, 0f);
		TimeChanged?.Invoke(TimeLeft);

		if (TimeLeft <= 0f)
		{
			_running = false;
			SetStatus(SoundboardStatus.Check);
			TimedOut?.Invoke();
		}
	}

	private void OnRetryTimeout()
	{
		if (_running && Status == SoundboardStatus.Retry)
		{
			SetStatus(SoundboardStatus.Working);
		}
	}

	private void SetStatus(SoundboardStatus newStatus)
	{
		Status = newStatus;
		StatusChanged?.Invoke(Status);
	}
}
