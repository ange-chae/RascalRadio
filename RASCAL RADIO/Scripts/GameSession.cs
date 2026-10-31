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
    public string PendingLevelId { get; set; } = "";
    public LevelDefinition CurrentLevel { get; private set; }   // null during the tutorial
    public event Action LevelStarted;
    private readonly HashSet<long> _endingDone = new();
    public override void _EnterTree() => Instance = this;

	// Called on EVERY peer from PlayerList.StartGameRpc.
	public void BeginLevel(string levelId)
	{
        LevelId = levelId;
        CurrentLevel = LevelRegistry.Get(levelId);
        CurrentStep = TutorialStep.MeetStudio;
        QuotaMet = 0; QuotaTarget = CurrentLevel?.QuotaTarget ?? 3;
        CallerName = ""; CallerStory = ""; CallerNumber = 0; CallerTotal = 0;
        KeypadLine = 0; KeypadLevel = 0; KeypadStatic = false;
        RuleEvenSuffix = "05"; RuleOddSuffix = "99";
        _tutorial = null; _broadcaster = null; _callers.Clear(); _override = null;
        AirtimeQuality = 100;

        if (!Multiplayer.IsServer()) return;
        if (!IsTutorial)
        {
            if (CurrentLevel != null) SetupRealLevel(CurrentLevel);
            return;
        }

        int players = GetNode<NetworkManager>("/root/NetworkManager").Players.Count;
		_tutorial = new TutorialLogic(players);
		_tutorial.StepChanged += OnHostStepChanged;

		_broadcaster = new BroadcasterLogic { PenaltiesEnabled = false };
		_broadcaster.SetQuota(Genre.Drama, 3);

		// Every run picks different callers and a different keypad problem.
		_rng = new Random();
		_callerBank = new CallerBank(_rng);
        _callers.Add(_callerBank.Legit(Genre.Drama));    // caller 1: ACCEPT
        _callers.Add(_callerBank.Legit(Genre.Drama));    // caller 2: ACCEPT
        _callers.Add(_callerBank.Raccoon(Genre.Drama));  // caller 3: REJECT

        // Tutorial keeps the manual's printed rule (05 / 99) and no static faults.
        // A real shift should use LineOverrideRule.NewRandom(_rng) and allowStatic: true.
        _override = new LineOverrideSession(_rng, LineOverrideRule.Default(), allowStatic: false);
	}

    // Host only. Level coders fill this in: callers, quota, keypad for their level.
    private void SetupRealLevel(LevelDefinition def)
    {
        _rng = new Random();
        _callerBank = new CallerBank(_rng);
        _callerIndex = 0;

        _broadcaster = new BroadcasterLogic { PenaltiesEnabled = true };
        _broadcaster.SetQuota(def.QuotaGenre, def.QuotaTarget);
        _broadcaster.StruckOut += () => Say("💥 Three strikes! The station is off the air.");   // TODO: lose screen

        if (def.BuildCallers != null) _callers.AddRange(def.BuildCallers(_callerBank));
        if (def.HasKeypad)
            _override = new LineOverrideSession(_rng, LineOverrideRule.NewRandom(_rng), def.AllowStatic);

        if (_callers.Count > 0) SendCaller(0);
        if (_override != null) SendKeypadProblem();
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
        if (!Multiplayer.IsServer()) return;
        if (_tutorial == null) { RealDecide(accept); return; }   // real levels (teammates fill this in)

        TutorialStep step = _tutorial.Step;
        // only the two caller steps take decisions; ignore stray clicks at any other time
        if (step != TutorialStep.PracticeCaller && step != TutorialStep.SuspiciousCaller) return;

        // 1. judge the caller who is on the line right now, BEFORE anything changes
        Caller caller = _callers[_callerIndex];
        bool correct = accept ? _broadcaster.Accept(caller) : _broadcaster.Reject(caller);

        // 2. wrong answer: explain, no penalty, and the step does NOT advance
        if (!correct)
        {
            if (step == TutorialStep.SuspiciousCaller)
                Say("↻ Something is fishy about this caller. Read the story again. No penalty.");
            else
                Say("↻ Not this time. Does the story fit the topic? Try again. No penalty.");
            return;
        }

        // 3. right answer: the quota is synced AFTER Accept() has counted it
        if (accept) Rpc(MethodName.SyncQuotaRpc, _broadcaster.QuotaMet, _broadcaster.QuotaTarget);

        // 4. practice has two callers to accept: bring in the second one before the step completes
        if (step == TutorialStep.PracticeCaller && _callerIndex < 1)
        {
            SendCaller(_callerIndex + 1);
            return;
        }

        // 5. done: move the tutorial forward exactly once
        Resolve(step == TutorialStep.PracticeCaller ? TutorialAction.CallerDecided : TutorialAction.RaccoonRejected);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void KeypadRpc(string entry)
	{
        if (!Multiplayer.IsServer() || _override == null) return;
        if (_tutorial != null && _tutorial.Step != TutorialStep.KeypadModule) return;

        switch (_override.Submit(entry))
        {
            case KeypadResult.Correct:
                if (_tutorial != null) Resolve(TutorialAction.KeypadSolved);
                else { Say(KeypadTag + "Line routed!", true); SendKeypadProblem(); }   // a fresh problem
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

    private void RealDecide(bool accept)
    {
        if (_broadcaster == null || _callers.Count == 0) return;

        Caller caller = _callers[_callerIndex];
        bool correct = accept ? _broadcaster.Accept(caller) : _broadcaster.Reject(caller);

        if (accept && correct) Rpc(MethodName.SyncQuotaRpc, _broadcaster.QuotaMet, _broadcaster.QuotaTarget);
        if (!correct) Say("Wrong call. That's a strike.");

        if (_callerIndex + 1 < _callers.Count) SendCaller(_callerIndex + 1);
        else Say("That was the last caller.", true);   // TODO: level complete screen
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
        if (!advanced && action == TutorialAction.Acknowledged)
            Say($"✔ {_tutorial.AckCount}/{_tutorial.PlayersNeeded} ready. Waiting for the others...", true);
	}

	private void OnHostStepChanged(TutorialStep step)
	{
		if (step == TutorialStep.KeypadModule) SendKeypadProblem();   // data first, then the step
		Rpc(MethodName.SyncStepRpc, (int)step);
        if (step == TutorialStep.PracticeCaller) SendCaller(0);
        else if (step == TutorialStep.SuspiciousCaller) SendCaller(2);   // the raccoon is caller 3
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

    // Each peer calls this when its title card has finished showing.
    public void ReportEndingDone() => RpcId(1, MethodName.EndingDoneRpc);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void EndingDoneRpc()
    {
        if (!Multiplayer.IsServer()) return;
        _endingDone.Add(SenderId());
        int players = GetNode<NetworkManager>("/root/NetworkManager").Players.Count;
        if (_endingDone.Count < players) return;
        _endingDone.Clear();
        Rpc(MethodName.StartPendingLevelRpc);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StartPendingLevelRpc()
    {
        if (string.IsNullOrEmpty(PendingLevelId)) return;
        BeginLevel(PendingLevelId);          // IsTutorial is now false
        GetTree().ReloadCurrentScene();      // role screens restart in real-level mode
        LevelStarted?.Invoke();
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
