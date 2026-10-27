using System;
using RascalRadio.Core.Models;

namespace RascalRadio.Core.Systems;

public class BroadcasterLogic
{
	public const int MaxStrikes = 3;

	public int Strikes { get; private set; }
	public int QuotaMet { get; private set; }
	public int QuotaTarget { get; set; } = 3;
	public Genre CurrentQuota { get; set; } = Genre.Drama;

	// Tutorial sets this to false: wrong choices just return false, no strikes.
	public bool PenaltiesEnabled { get; set; } = true;

	public event Action StruckOut;   // 3 wrong decisions

	// Returns true if accepting this caller was correct
	public bool Accept(Caller caller)
	{
		if (caller.Topic == CurrentQuota && !caller.IsRaccoon)
		{
			QuotaMet++;
			return true;
		}

		AddStrike();
		return false;
	}

	// Returns true if rejecting this caller was correct
	public bool Reject(Caller caller)
	{
		if (caller.Topic != CurrentQuota || caller.IsRaccoon) return true;

		AddStrike();
		return false;
	}

	private void AddStrike()
	{
		if (!PenaltiesEnabled) return;
		Strikes++;
		if (Strikes >= MaxStrikes) StruckOut?.Invoke();
	}

	public void SetQuota(Genre genre, int target)
	{
		CurrentQuota = genre;
		QuotaTarget = target;
		QuotaMet = 0;
	}
}
