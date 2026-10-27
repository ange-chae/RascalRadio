using System;
using System.Collections.Generic;
using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;

// AUTOLOAD (Project Settings > Globals > Autoload, name: GameSession).
// The HOST owns the real state. Clients only send requests (RpcId(1, ...)),
// and the host replies with Authority RPCs that every peer applies locally.
public partial class GameSession : Node
{
	public static GameSession Instance { get; private set; }

	// Keypad feedback lines start with this tag so the keypad can pick them out of the Feedback event.
	public const string KeypadTag = "🔢 ";

	// ---- State every peer can read (copied from the host) ----
	public string LevelId { get; private set; } = "";
	public bool IsTutorial => LevelId == "tutorial";
	public TutorialStep CurrentStep { get; private set; } = TutorialStep.MeetStudio;
	public int QuotaMet { get; private set; }
	public int QuotaTarget { get; private set; } = 3;
	public string CallerName { get; private set; } = "";
	public string CallerStory { get; private set; } = "";
	public int CallerNumber { get; private set; }
	public int CallerTotal { get; private set; }



    // Line Override problem. The Operator UI shows ONLY KeypadLine (+ the static fault).
    // The Technician UI shows ONLY the level and the rule. Neither can solve it alone.
    public int KeypadLine { get; private set; }
	public int KeypadLevel { get; private set; }
	public bool KeypadStatic { get; private set; }
	public string RuleEvenSuffix { get; private set; } = "05";
	public string RuleOddSuffix { get; private set; } = "99";
	public bool HasKeypadProblem => KeypadLine > 0;
    public int AirtimeQuality { get; private set; } = 100;
    public event Action AirtimeChanged;

    // Plain C# events. Subscribers MUST unsubscribe in _ExitTree.
    public event Action<TutorialStep> StepChanged;
	public event Action<string, bool> Feedback;
	public event Action CallerChanged;
	public event Action QuotaChanged;
	public event Action KeypadChanged;

	// ---- Host-only objects ----
	private TutorialLogic _tutorial;
	private BroadcasterLogic _broadcaster;
	private readonly List<Caller> _callers = new();
	private int _callerIndex;
	private Random _rng;
	private CallerBank _callerBank;
	private LineOverrideSession _override;

	public override void _EnterTree() => Instance = this;

	// Called on EVERY peer from PlayerList.StartGameRpc.
	public void BeginLevel(string levelId)
	{
		LevelId = levelId;
		CurrentStep = TutorialStep.MeetStudio;
		QuotaMet = 0; QuotaTarget = 3;
		CallerName = ""; CallerStory = ""; CallerNumber = 0; CallerTotal = 0;
		KeypadLine = 0; KeypadLevel = 0; KeypadStatic = false;
		RuleEvenSuffix = "05"; RuleOddSuffix = "99";
		_tutorial = null; _broadcaster = null; _callers.Clear(); _override = null;
        AirtimeQuality = 100;

        if (!Multiplayer.IsServer() || !IsTutorial) return;

		int players = GetNode<NetworkManager>("/root/NetworkManager").Players.Count;
		_tutorial = new TutorialLogic(players);
		_tutorial.StepChanged += OnHostStepChanged;

		_broadcaster = new BroadcasterLogic { PenaltiesEnabled = false };
		_broadcaster.SetQuota(Genre.Drama, 3);

		// Every run picks different callers and a different keypad problem.
		_rng = new Random();
		_callerBank = new CallerBank(_rng);
		_callers.Add(_callerBank.Legit(Genre.Drama));   // fits the topic: ACCEPT
		_callers.Add(_callerBank.Raccoon(Genre.Drama)); // the odd one: REJECT

		// Tutorial keeps the manual's printed rule (05 / 99) and no static faults.
		// A real shift should use LineOverrideRule.NewRandom(_rng) and allowStatic: true.
		_override = new LineOverrideSession(_rng, LineOverrideRule.Default(), allowStatic: false);
	}

	// ---------- Requests (any peer -> host) ----------
	public void Report(TutorialAction action) => RpcId(1, MethodName.ReportRpc, (int)action);
	public void Decide(bool accept) => RpcId(1, MethodName.DecideRpc, accept);
	public void SubmitKeypad(string entry) => RpcId(1, MethodName.KeypadRpc, entry);

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReportRpc(int action)
	{
		if (!Multiplayer.IsServer() || _tutorial == null) return;
		Resolve((TutorialAction)action);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void DecideRpc(bool accept)
	{
		if (!Multiplayer.IsServer() || _tutorial == null) return;

		TutorialStep step = _tutorial.Step;
		if (step != TutorialStep.PracticeCaller && step != TutorialStep.SuspiciousCaller)
		{
			Say("There is no caller to decide on right now.");
			return;
		}

		Caller caller = _callers[_callerIndex];
		bool correct = accept ? _broadcaster.Accept(caller) : _broadcaster.Reject(caller);

		if (!correct)
		{
			if (step == TutorialStep.SuspiciousCaller)
				Say("↻ Something is fishy about this caller. Read the story again. No penalty.");
			else
				Say("↻ Not this time. Does the story fit the topic? Try again. No penalty.");
			return;
		}

		if (accept) Rpc(MethodName.SyncQuotaRpc, _broadcaster.QuotaMet, _broadcaster.QuotaTarget);
		Resolve(step == TutorialStep.PracticeCaller ? TutorialAction.CallerDecided : TutorialAction.RaccoonRejected);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void KeypadRpc(string entry)
	{
		if (!Multiplayer.IsServer() || _tutorial == null || _override == null) return;
		if (_tutorial.Step != TutorialStep.KeypadModule) return;

		switch (_override.Submit(entry))
		{
			case KeypadResult.Correct:
				Resolve(TutorialAction.KeypadSolved);
				break;
			case KeypadResult.StaticCleared:
				Rpc(MethodName.SyncKeypadRpc, _override.Problem.Line, _override.Problem.Level,
					_override.Rule.EvenSuffix, _override.Rule.OddSuffix, false);
				Say(KeypadTag + "Static cleared. Now enter the override code.", true);
				break;
			case KeypadResult.StaticBlocked:
				Say(KeypadTag + "⚡ Static on the line. Ask the Technician how to clear it.");
				break;
			default:
				Say(KeypadTag + "Wrong code. Read the line number to the Technician and try again. No penalty.");
				break;
		}
	}

	// ---------- Host logic ----------
	private long SenderId()
	{
		long id = Multiplayer.GetRemoteSenderId();
		return id == 0 ? 1 : id;   // 0 means "called locally on the host"
	}

	private void Resolve(TutorialAction action)
	{
		bool advanced = _tutorial.Report(action, SenderId());
		if (advanced) Say("✔ Nice! Step complete.", true);
		else if (action == TutorialAction.Acknowledged)
			Say($"✔ {_tutorial.AckCount}/{_tutorial.PlayersNeeded} ready. Waiting for the others...", true);
	}

	private void OnHostStepChanged(TutorialStep step)
	{
		if (step == TutorialStep.KeypadModule) SendKeypadProblem();   // data first, then the step
		Rpc(MethodName.SyncStepRpc, (int)step);
		if (step == TutorialStep.PracticeCaller) SendCaller(0);
		else if (step == TutorialStep.SuspiciousCaller) SendCaller(1);
	}

	private void SendKeypadProblem()
	{
		var p = _override.NewProblem();
		Rpc(MethodName.SyncKeypadRpc, p.Line, p.Level, _override.Rule.EvenSuffix, _override.Rule.OddSuffix, p.StaticFault);
	}

	private void SendCaller(int index)
	{
		_callerIndex = index;
		Caller c = _callers[index];
		Rpc(MethodName.SyncCallerRpc, c.Name, c.Story, index + 1, _callers.Count);
	}

	private void Say(string text, bool success = false) => Rpc(MethodName.FeedbackRpc, text, success);

	// ---------- Replies (host -> everyone, including the host itself) ----------
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncStepRpc(int step)
	{
		CurrentStep = (TutorialStep)step;
		StepChanged?.Invoke(CurrentStep);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncCallerRpc(string name, string story, int number, int total)
	{
		CallerName = name; CallerStory = story; CallerNumber = number; CallerTotal = total;
		CallerChanged?.Invoke();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncKeypadRpc(int line, int level, string evenSuffix, string oddSuffix, bool staticFault)
	{
		KeypadLine = line; KeypadLevel = level; KeypadStatic = staticFault;
		RuleEvenSuffix = evenSuffix; RuleOddSuffix = oddSuffix;
		KeypadChanged?.Invoke();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncQuotaRpc(int met, int target)
	{
		QuotaMet = met; QuotaTarget = target;
		QuotaChanged?.Invoke();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void FeedbackRpc(string text, bool success) => Feedback?.Invoke(text, success);

    public void ReportAirtime(int delta) => RpcId(1, MethodName.ReportAirtimeRpc, delta);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReportAirtimeRpc(int delta)
    {
        if (!Multiplayer.IsServer()) return;
        int newQuality = Mathf.Clamp(AirtimeQuality + delta, 0, 100);
        Rpc(MethodName.SyncAirtimeRpc, newQuality);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncAirtimeRpc(int currentQuality)
    {
        AirtimeQuality = currentQuality;
        AirtimeChanged?.Invoke();
    }
}
