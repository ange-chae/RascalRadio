using System;

namespace RascalRadio.Core.Systems;

public class GameTimer
{
	public float Duration { get; }
	public float TimeRemaining { get; private set; }
	public bool IsRunning { get; private set; }

	public event Action? Started;
	public event Action? TimeUp;

	public GameTimer(float durationSeconds)
	{
		Duration = durationSeconds;
		TimeRemaining = durationSeconds;
	}

	public void Start()
	{
		TimeRemaining = Duration;
		IsRunning = true;
		Started?.Invoke();
	}

	public void Stop() => IsRunning = false;

	public void Tick(float deltaSeconds)
	{
		if (!IsRunning) return;

		TimeRemaining -= deltaSeconds;

		if (TimeRemaining <= 0f)
		{
			TimeRemaining = 0f;
			IsRunning = false;
			TimeUp?.Invoke();
		}
	}
}
