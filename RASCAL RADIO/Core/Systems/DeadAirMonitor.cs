using System;

namespace RascalRadio.Core.Systems;

/// <summary>
/// Dead Air Prevention. Pure C# (no Godot types).
/// Any registered word/trigger resets the idle clock. After IdleLimitSeconds of silence the
/// DEAD AIR state starts and the listener quota drains quickly until the Broadcaster talks again.
/// </summary>
public sealed class DeadAirMonitor
{
	public const float IdleLimitSeconds = 3f;
	public const float DrainPerSecond = 15f;
	public const float RecoverPerSecond = 3f;

	public float IdleTime { get; private set; }
	public float Listeners { get; private set; } = 100f;   // 0-100
	public bool IsDeadAir => IdleTime >= IdleLimitSeconds;

	public event Action DeadAirStarted;
	public event Action ListenersGone;

	/// <summary>Call whenever the Broadcaster registers a word or trigger.</summary>
	public void Register() => IdleTime = 0f;

	public void Reset() { IdleTime = 0f; Listeners = 100f; }

	public void Tick(float delta)
	{
		bool wasDead = IsDeadAir;
		IdleTime += delta;

		if (IsDeadAir)
		{
			if (!wasDead) DeadAirStarted?.Invoke();
			float before = Listeners;
			Listeners = Math.Max(0f, Listeners - DrainPerSecond * delta);
			if (before > 0f && Listeners <= 0f) ListenersGone?.Invoke();
		}
		else
		{
			Listeners = Math.Min(100f, Listeners + RecoverPerSecond * delta);
		}
	}
}
