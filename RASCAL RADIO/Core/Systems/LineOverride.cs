using System;
using System.Collections.Generic;

namespace RascalRadio.Core.Systems;
public sealed class LineOverrideRule
{
	public string EvenSuffix { get; init; } = "05";
	public string OddSuffix { get; init; } = "99";

	private static readonly string[] SuffixPool = { "05", "99", "17", "42", "63", "28", "71", "36", "84", "50" };

	/// <summary>The rule printed in the original manual. Used by the tutorial.</summary>
	public static LineOverrideRule Default() => new();

	/// <summary>A fresh rule for a shift: two different suffixes.</summary>
	public static LineOverrideRule NewRandom(Random rng)
	{
		int a = rng.Next(SuffixPool.Length);
		int b;
		do { b = rng.Next(SuffixPool.Length); } while (b == a);
		return new LineOverrideRule { EvenSuffix = SuffixPool[a], OddSuffix = SuffixPool[b] };
	}

	public string Compute(int line, int level)
	{
        return line.ToString() + (line % 2 == 0 ? EvenSuffix : OddSuffix);
    }
}

public sealed class LineOverrideProblem
{
	public int Line { get; init; }        // 1-9, shown to the OPERATOR
	public int Level { get; init; }       // 1-3, shown to the TECHNICIAN
	public string Code { get; init; } = "";
	public bool StaticFault { get; set; } // must be cleared with 0000 first
}

public enum KeypadResult { Correct, Wrong, StaticBlocked, StaticCleared }

/// <summary>Host-side state of the current override problem.</summary>
public sealed class LineOverrideSession
{
	public const string StaticClearCode = "0000";

	public LineOverrideRule Rule { get; }
	public LineOverrideProblem Problem { get; private set; }
	public bool AllowStatic { get; }

	private readonly Random _rng;
	private readonly HashSet<(int, int)> _recent = new();

	public LineOverrideSession(Random rng, LineOverrideRule rule, bool allowStatic)
	{
		_rng = rng; Rule = rule; AllowStatic = allowStatic;
	}

	/// <param name="level">Studio level index (1-3). Null = random each problem.</param>
	public LineOverrideProblem NewProblem(int? level = null, double staticChance = 0.35)
	{
		int line, lvl, tries = 0;
		do
		{
			line = _rng.Next(1, 10);
			lvl = level ?? _rng.Next(1, 4);
			tries++;
		} while (_recent.Contains((line, lvl)) && tries < 20);

		if (_recent.Count >= 8) _recent.Clear();
		_recent.Add((line, lvl));

		Problem = new LineOverrideProblem
		{
			Line = line,
			Level = lvl,
			Code = Rule.Compute(line, lvl),
			StaticFault = AllowStatic && _rng.NextDouble() < staticChance,
		};
		return Problem;
	}

	/// <summary>The operator pressed ROUTE with this entry.</summary>
	public KeypadResult Submit(string entry)
	{
		if (Problem == null) return KeypadResult.Wrong;

		if (Problem.StaticFault)
		{
			if (entry == StaticClearCode) { Problem.StaticFault = false; return KeypadResult.StaticCleared; }
			return KeypadResult.StaticBlocked;
		}
		return entry == Problem.Code ? KeypadResult.Correct : KeypadResult.Wrong;
	}
}
