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
    public const string PartyScenePath = "res://scenes/player_lists.tscn"; 
    private GameOverScreen _gameOverScreen;

    // Keypad feedback lines start with this tag so the keypad can pick them out of the Feedback event.
    public const string KeypadTag = "🔢 ";

	// ---- State every peer can read (copied from the host) ----
	public string LevelId { get; private set; } = "";
	public bool IsTutorial => LevelId == "tutorial";
	public TutorialStep CurrentStep { get; private set; } = TutorialStep.MeetStudio;
	public int QuotaMet { get; private set; }
	public int QuotaTarget { get; private set; } = 3;
	public string CallerName { get; private set; } = "";
    public Genre Topic { get; private set; } = Genre.Drama;
    public event Action TopicChanged;
    private static readonly Genre[] TutorialTopics = { Genre.Drama, Genre.Comedy, Genre.Music };
    public string CallerStory { get; private set; } = "";
	public int CallerNumber { get; private set; }
	public int CallerTotal { get; private set; }
    public int Strikes { get; private set; }
    public bool GameOver { get; private set; }
    public static int MaxStrikes => BroadcasterLogic.MaxStrikes;
    public event Action StrikesChanged;
    private int _session;          // changes whenever a level starts or ends, so old timers do nothing
    private bool _callerOnLine;

    // Same on every peer: the piano runs in lockstep and the keypad state is synced.
    public int FaultCount => (Piano != null && Piano.InFault ? 1 : 0) + (KeypadStatic ? 1 : 0);
    public int Signal => Mathf.Clamp(100 - 35 * FaultCount, 0, 100);

    private double _qualityTick, _sabotageTimer = -1;



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
    public StudioPianoModule Piano { get; private set; }
    private static readonly string[] PianoSequences = { "Morning Jingle", "Night Jingle", "Breaking News Alert" };

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

    public override void _Ready()
    {
        Piano = new StudioPianoModule();
        AddChild(Piano);
        Piano.Completed += OnPianoCompleted;
    }

    public override void _ExitTree()
    {
        if (Piano != null) Piano.Completed -= OnPianoCompleted;
    }

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
        Strikes = 0; GameOver = false; _qualityTick = 0; _sabotageTimer = -1;
        Topic = Genre.Drama;

        Piano.SetActive(false);
        Piano.Reset();

        _session++; _callerOnLine = false;

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
        // Every run picks a different topic.
        _rng = new Random();
        Genre topic = TutorialTopics[_rng.Next(TutorialTopics.Length)];
        _broadcaster.SetQuota(topic, 3);
        SetTopic(topic);

        _callerBank = new CallerBank(_rng);
        _callers.Add(_callerBank.Legit(topic));    // caller 1: ACCEPT
        _callers.Add(_callerBank.Legit(topic));    // caller 2: ACCEPT
        _callers.Add(_callerBank.Raccoon(topic));  // caller 3: REJECT

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

        Genre topic = def.Genres[_rng.Next(def.Genres.Count)];
        SetTopic(topic);

        _broadcaster = new BroadcasterLogic { PenaltiesEnabled = true };
        _broadcaster.SetQuota(topic, def.QuotaTarget);
        ScheduleSabotage();

        if (def.BuildCallers != null) _callers.AddRange(def.BuildCallers(_callerBank, topic));
        if (def.HasKeypad)
            _override = new LineOverrideSession(_rng, LineOverrideRule.NewRandom(_rng), def.AllowStatic);

        if (def.HasPiano) StartRandomPiano();
        if (_callers.Count > 0) ScheduleNextCaller(0, 5.0);   // first caller rings after 5 seconds
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
        if (GameOver || !_callerOnLine || _broadcaster == null || _callers.Count == 0) return;
        _callerOnLine = false;   // ignores double-clicks and presses while nobody is on the line

        Caller caller = _callers[_callerIndex];
        int strikesBefore = _broadcaster.Strikes;
        bool correct = accept ? _broadcaster.Accept(caller) : _broadcaster.Reject(caller);

        if (accept && correct) Rpc(MethodName.SyncQuotaRpc, _broadcaster.QuotaMet, _broadcaster.QuotaTarget);
        if (_broadcaster.Strikes != strikesBefore) Rpc(MethodName.SyncStrikesRpc, _broadcaster.Strikes);
        if (!correct) Say("Wrong call. That's a strike.");

        if (_broadcaster.Strikes >= BroadcasterLogic.MaxStrikes) { Rpc(MethodName.GameOverRpc); return; }

        if (accept && caller.IsRaccoon) TriggerSabotage();

        Rpc(MethodName.SyncCallerRpc, "", "", 0, _callers.Count);   // line goes quiet: both screens hide the caller
        if (_callerIndex + 1 < _callers.Count)
            ScheduleNextCaller(_callerIndex + 1, (CurrentLevel?.CallerGap ?? 6) * (0.75 + _rng.NextDouble() * 0.5));
        else
            Say("That was the last caller.", true);   // TODO: level complete screen
    }

    private void ScheduleNextCaller(int index, double delay)
    {
        int session = _session;
        GetTree().CreateTimer(delay).Timeout += () =>
        {
            // do nothing if the level ended or restarted while we waited
            if (session != _session || GameOver || _broadcaster == null || index >= _callers.Count) return;
            SendCaller(index);
        };
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
        _callerOnLine = true;
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
        ApplyAirtime(delta);
    }

    private void ApplyAirtime(int delta)   // host only
    {
        int q = Mathf.Clamp(AirtimeQuality + delta, 0, 100);
        if (q != AirtimeQuality) Rpc(MethodName.SyncAirtimeRpc, q);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncAirtimeRpc(int currentQuality)
    {
        AirtimeQuality = currentQuality;
        AirtimeChanged?.Invoke();
    }

    // ---------- Studio Piano (Module B) ----------
    // Requests (any peer -> host)
    public void PianoKey(string letter) => RpcId(1, MethodName.PianoKeyRpc, letter);
    public void PianoOctave(int direction) => RpcId(1, MethodName.PianoOctaveRpc, direction);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PianoKeyRpc(string letter)
    {
        if (!Multiplayer.IsServer()) return;
        Rpc(MethodName.PianoApplyKeyRpc, letter);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PianoOctaveRpc(int direction)
    {
        if (!Multiplayer.IsServer()) return;
        Rpc(MethodName.PianoApplyOctaveRpc, direction);
    }

    // Host logic
    public void StartRandomPiano(bool showNotes = false, float timeLimit = 0f)
    {
        if (!Multiplayer.IsServer()) return;
        _rng ??= new Random();
        string name = PianoSequences[_rng.Next(PianoSequences.Length)];
        // showNotes=false: Operator sees only the jingle NAME, the Technician reads the notes from the manual.
        Rpc(MethodName.PianoConfigureRpc, name, true, timeLimit, showNotes);
    }

    // Call this from your sabotage logic when you want "keys flash red".
    public void TriggerPianoFault()
    {
        if (!Multiplayer.IsServer()) return;
        Rpc(MethodName.PianoFaultRpc);
    }

    private void OnPianoCompleted()
    {
        if (!Multiplayer.IsServer()) return;
        Say("🎹 Jingle played!", true);
        StartRandomPiano();   // a fresh jingle; remove this line if the piano should only run once
    }

    private void SetTopic(Genre genre) => Rpc(MethodName.SyncTopicRpc, (int)genre);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncTopicRpc(int genre)
    {
        Topic = (Genre)genre;
        TopicChanged?.Invoke();
    }

    // Replies (host -> everyone, including the host)
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PianoConfigureRpc(string sequence, bool useOctave, float timeLimit, bool showNotes)
    {
        var cues = sequence == StudioPianoData.RecalibrationName
            ? StudioPianoData.GetRecalibration()
            : StudioPianoData.GetJingles()[sequence];
        Piano.Configure(sequence, cues, useOctave, timeLimit, showNotes);
        Piano.SetActive(true);
        Piano.Start();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PianoApplyKeyRpc(string letter) => Piano.RequestKey(letter);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PianoApplyOctaveRpc(int direction) => Piano.RequestOctaveStep(direction);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PianoFaultRpc() => Piano.TriggerFault();

    // ---------- Quality, sabotage, strikes (host) ----------
    public override void _Process(double delta)
    {
        if (CurrentLevel == null || IsTutorial || GameOver || _broadcaster == null) return;
        if (!Multiplayer.HasMultiplayerPeer() || !Multiplayer.IsServer()) return;
        if (!Multiplayer.IsServer() || IsTutorial || GameOver || CurrentLevel == null || _broadcaster == null) return;

        // Quality: faults drain it, calm seconds heal it.
        _qualityTick += delta;
        if (_qualityTick >= 1.0)
        {
            _qualityTick -= 1.0;
            int faults = FaultCount;
            ApplyAirtime(faults > 0 ? -2 * faults : 1);
        }

        // Raccoon sabotage timer
        if (_sabotageTimer > 0)
        {
            _sabotageTimer -= delta;
            if (_sabotageTimer <= 0) { TriggerSabotage(); ScheduleSabotage(); }
        }
    }

    private void ScheduleSabotage()
    {
        double interval = CurrentLevel?.SabotageInterval ?? 0;
        _sabotageTimer = interval <= 0 ? -1 : interval * (0.7 + _rng.NextDouble() * 0.6);   // a little random
    }

    public void TriggerSabotage()
    {
        if (!Multiplayer.IsServer() || GameOver) return;

        var options = new List<Action>();

        // Piano: keys flash red (only while a jingle is being played)
        if (CurrentLevel?.HasPiano == true && Piano.Active && !Piano.InFault
            && Piano.Status == StudioPianoModule.PianoStatus.Working)
        {
            options.Add(() =>
            {
                Say("The raccoon is chewing the piano wires! Keys flashing red.");
                TriggerPianoFault();
            });
        }

        // Keypad: static on the current line problem
        if (_override?.Problem != null && !_override.Problem.StaticFault)
        {
            options.Add(() =>
            {
                var p = _override.Problem;
                p.StaticFault = true;
                Rpc(MethodName.SyncKeypadRpc, p.Line, p.Level, _override.Rule.EvenSuffix, _override.Rule.OddSuffix, true);
                Say(KeypadTag + "The raccoon is on the line! Static. Ask the Technician how to clear it.");
            });
        }

        if (options.Count == 0) return;   // nothing to sabotage right now
        options[_rng.Next(options.Count)]();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncStrikesRpc(int strikes)
    {
        Strikes = strikes;
        StrikesChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void GameOverRpc()
    {
        GameOver = true;
        Piano.SetActive(false);
        _gameOverScreen = new GameOverScreen();
        GetTree().Root.AddChild(_gameOverScreen);
    }

    // ---------- Back to the players list ----------
    // Anyone can ask. The host tells EVERYONE to go back together.
    public void ReturnToParty()
    {
        if (!Multiplayer.HasMultiplayerPeer()) { ResetSession(); GetTree().ChangeSceneToFile(PartyScenePath); return; }
        RpcId(1, MethodName.ReturnRequestRpc);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReturnRequestRpc()
    {
        if (!Multiplayer.IsServer()) return;
        Rpc(MethodName.ReturnToPartyRpc);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReturnToPartyRpc()
    {
        ResetSession();
        GetTree().ChangeSceneToFile(PartyScenePath);
    }

    private void ResetSession()
    {
        if (IsInstanceValid(_gameOverScreen)) _gameOverScreen.QueueFree();   // it lives on the root, so free it by hand
        _gameOverScreen = null;

        CurrentLevel = null; LevelId = ""; GameOver = false;
        _tutorial = null; _broadcaster = null; _override = null; _callers.Clear(); _endingDone.Clear();
        Strikes = 0; AirtimeQuality = 100; Topic = Genre.Drama;
        _sabotageTimer = -1; _qualityTick = 0;
        Piano.SetActive(false);
        Piano.Reset();

        _session++; _callerOnLine = false;
    }
}
