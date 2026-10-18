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

	public event Action? StruckOut;   // 3 wrong accepts

	// Returns true if the accepted caller fit the quota
	public bool Accept(Caller caller)
	{
		if (caller.Topic == CurrentQuota)
		{
			QuotaMet++;
			return true;
		}

		Strikes++;
		if (Strikes >= MaxStrikes) StruckOut?.Invoke();
		return false;
	}

	public void SetQuota(Genre genre, int target)
	{
		CurrentQuota = genre;
		QuotaTarget = target;
		QuotaMet = 0;
	}
}
