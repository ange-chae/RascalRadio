using System;
using System.Collections.Generic;
using System.Linq;
using RascalRadio.Core.Models;

namespace RascalRadio.Core.Systems;

/// <summary>
/// Backend rules for the role selection screen. No Godot types in here:
/// the screen only displays this state and forwards button presses to it.
/// </summary>
public class RoleSelectionLogic
{
	private readonly List<Role> _roles;
	private readonly List<int> _players = new();
	private readonly Dictionary<int, Role> _assignments = new(); // playerId -> role
	private readonly HashSet<int> _ready = new();
	private readonly Random _rng;

	/// <summary>Roles that can be picked. Role4 is left out until the team defines it.</summary>
	public IReadOnlyList<Role> Roles => _roles;

	/// <summary>Players currently in the room, in join order.</summary>
	public IReadOnlyList<int> Players => _players;

	/// <summary>How many players are needed before the game may start. Placeholder until the team decides.</summary>
	public int MinPlayers { get; set; } = 1;

	/// <summary>Raised whenever players, roles or ready states change.</summary>
	public event Action SelectionChanged;

	/// <summary>Raised when every player has a role and is ready.</summary>
	public event Action AllReady;

	public RoleSelectionLogic(IEnumerable<Role> availableRoles, Random rng = null)
	{
		_roles = availableRoles.Distinct().ToList();
		_rng = rng ?? new Random();
	}

	// ---------- Players ----------

	public bool AddPlayer(int playerId)
	{
		if (_players.Contains(playerId)) return false;
		// Can't have more players than roles, since every role is one player.
		if (_players.Count >= _roles.Count) return false;

		_players.Add(playerId);
		Changed();
		return true;
	}

	public void RemovePlayer(int playerId)
	{
		if (!_players.Remove(playerId)) return;
		_assignments.Remove(playerId);
		_ready.Remove(playerId);
		Changed();
	}

	// ---------- Picking roles ----------

	/// <summary>Returns false if the player is unknown, the role is not available, or someone else has it.</summary>
	public bool TrySelect(int playerId, Role role)
	{
		if (!_players.Contains(playerId)) return false;
		if (!_roles.Contains(role)) return false;

		if (_assignments.TryGetValue(playerId, out var current) && current == role)
			return true; // already yours, nothing to do

		if (IsTakenByOther(role, playerId)) return false;

		_ready.Remove(playerId); // changing role cancels ready
		_assignments[playerId] = role;
		Changed();
		return true;
	}

	/// <summary>Lets a player give up their role.</summary>
	public void Deselect(int playerId)
	{
		if (!_assignments.Remove(playerId)) return;
		_ready.Remove(playerId);
		Changed();
	}

	// ---------- Randomizer ----------

	/// <summary>
	/// Assigns roles at random.
	/// onlyUnassigned = false: everyone is reshuffled and un-readied.
	/// onlyUnassigned = true: players who already chose keep their role, the rest get leftovers.
	/// </summary>
	public bool Randomize(bool onlyUnassigned = false)
	{
		if (_players.Count == 0) return false;

		List<int> targets;
		List<Role> pool;

		if (onlyUnassigned)
		{
			targets = _players.Where(p => !_assignments.ContainsKey(p)).ToList();
			pool = _roles.Where(r => !_assignments.ContainsValue(r)).ToList();
		}
		else
		{
			targets = _players.ToList();
			pool = _roles.ToList();
			_assignments.Clear();
			_ready.Clear();
		}

		if (targets.Count == 0 || pool.Count < targets.Count) return false;

		Shuffle(pool);
		for (int i = 0; i < targets.Count; i++)
		{
			_assignments[targets[i]] = pool[i];
			_ready.Remove(targets[i]);
		}

		Changed();
		return true;
	}

	// ---------- Ready ----------

	/// <summary>A player can only be ready once they hold a role.</summary>
	public bool SetReady(int playerId, bool ready)
	{
		if (!_assignments.ContainsKey(playerId)) return false;

		if (ready) _ready.Add(playerId);
		else _ready.Remove(playerId);

		Changed();
		return true;
	}

	// ---------- Queries (used by the screen to draw itself) ----------

	public Role? GetRole(int playerId) =>
		_assignments.TryGetValue(playerId, out var role) ? role : (Role?)null;

	public int? GetPlayerWithRole(Role role)
	{
		foreach (var pair in _assignments)
			if (pair.Value == role) return pair.Key;
		return null;
	}

	public bool IsTaken(Role role) => _assignments.ContainsValue(role);

	public bool IsReady(int playerId) => _ready.Contains(playerId);

	public int ReadyCount => _ready.Count;

	public bool CanStart =>
		_players.Count >= MinPlayers &&
		_players.All(p => _assignments.ContainsKey(p) && _ready.Contains(p));

	// ---------- Helpers ----------

	private bool IsTakenByOther(Role role, int playerId) =>
		_assignments.Any(a => a.Value == role && a.Key != playerId);

	private void Shuffle<T>(IList<T> list)
	{
		// Fisher-Yates
		for (int i = list.Count - 1; i > 0; i--)
		{
			int j = _rng.Next(i + 1);
			(list[i], list[j]) = (list[j], list[i]);
		}
	}

	private void Changed()
	{
		SelectionChanged?.Invoke();
		if (CanStart) AllReady?.Invoke();
	}
}
