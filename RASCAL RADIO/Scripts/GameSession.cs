using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;
using System;
using System.Collections.Generic;
using System.Text;

// AUTOLOAD (Project Settings > Globals > Autoload, name: GameSession).
// The HOST owns the real state. Clients only send requests (RpcId(1, ...)),
// and the host replies with Authority RPCs that every peer applies locally.
public partial class GameSession : Node
{
	public static GameSession Instance { get; private set; }
    public const string PartyScenePath = "res://scenes/player_lists.tscn";

    public readonly List<LevelTask> Tasks = new();
    public bool LevelFinished { get; private set; }
    public bool Ended => GameOver || LevelFinished;
    public string FlippedModule { get; private set; } = "";
    public event Action TasksChanged;
    public event Action FlipChanged;
    private Node _endScreen;

    public int FaultCount => (Piano != null && Piano.InFault ? 1 : 0) + (KeypadStatic ? 1 : 0) + (FlippedModule != "" ? 1 : 0);
    public int Signal => Mathf.Clamp(100 - 35 * FaultCount, 0, 100);

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
    public static int MaxStrikes => Instance?._broadcaster?.MaxStrikes ?? Instance?.CurrentLevel?.MaxStrikes ?? BroadcasterLogic.DefaultMaxStrikes;
    public event Action StrikesChanged;
    private int _session;          // changes whenever a level starts or ends, so old timers do nothing
    private bool _callerOnLine;

    // Same on every peer: the piano runs in lockstep and the keypad state is synced.


    private double _qualityTick, _sabotageTimer = -1;

    private double _qualityLow, _signalLow;
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
    public SoundboardModule Soundboard { get; private set; }
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

    private const ulong ModuleStrikeCooldownMs = 1200;   // wrong presses closer together than this give ONE strike. 0 = every wrong press is a strike.
    private ulong _lastModuleStrikeMs;

    public override void _Ready()
    {
        Piano = new StudioPianoModule();
        AddChild(Piano);
        Piano.Completed += OnPianoCompleted;
        Piano.KeyResult += OnPianoKey;

        Soundboard = new SoundboardModule();
        AddChild(Soundboard);
        Soundboard.Completed += OnSoundboardCompleted;
    }

    public override void _ExitTree()
    {
        if (Piano != null) Piano.Completed -= OnPianoCompleted;
        if (Piano != null) Piano.KeyResult -= OnPianoKey;
        if (Soundboard != null) Soundboard.Completed -= OnSoundboardCompleted;
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
        LevelFinished = false; FlippedModule = ""; BuildTasks();
        Topic = Genre.Drama;
        _qualityLow = 0; _signalLow = 0;
        _lastModuleStrikeMs = 0;


        Piano.SetActive(false);
        Piano.Reset();
        Soundboard?.SetActive(false);
        Soundboard?.Reset();

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

        _broadcaster = new BroadcasterLogic { PenaltiesEnabled = true, MaxStrikes = def.MaxStrikes };
        _broadcaster.SetQuota(topic, def.QuotaTarget);
        ScheduleSabotage();

        if (def.BuildCallers != null) _callers.AddRange(def.BuildCallers(_callerBank, topic));
        if (def.HasKeypad)
            _override = new LineOverrideSession(_rng, LineOverrideRule.NewRandom(_rng), def.AllowStatic);

        if (def.HasPiano) StartRandomPiano();
        if (def.HasSoundboard) StartRandomSoundboard();
        if (_callers.Count > 0) ScheduleNextCaller(0, 5.0);   // first caller rings after 5 seconds
        if (_override != null) SendKeypadProblem(0);   // first problem is always clean
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
                else OnKeypadRouted();
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
                if (_tutorial != null) Say(KeypadTag + "Wrong code. Read the line number to the Technician and try again. No penalty.");
                else ModuleStrike(TaskKind.Keypad, KeypadTag + "Wrong code. That's a strike.");
                break;
        }
	}

    private void RealDecide(bool accept)
    {
        if (Ended || !_callerOnLine || _broadcaster == null || _callers.Count == 0) return;
        _callerOnLine = false;

        Caller caller = _callers[_callerIndex];
        int strikesBefore = _broadcaster.Strikes;
        bool correct = accept ? _broadcaster.Accept(caller) : _broadcaster.Reject(caller);

        var callersTask = TaskOf(TaskKind.Callers);
        if (accept && correct) Rpc(MethodName.SyncQuotaRpc, _broadcaster.QuotaMet, _broadcaster.QuotaTarget);
        if (!correct && callersTask != null) callersTask.Mistakes++;
        if (callersTask != null && _broadcaster.QuotaMet >= callersTask.Target) callersTask.Done = true;
        if (_broadcaster.Strikes != strikesBefore) Rpc(MethodName.SyncStrikesRpc, _broadcaster.Strikes);
        if (!correct) Say("Wrong call. That's a strike.");

        if (_broadcaster.Strikes >= _broadcaster.MaxStrikes) { Rpc(MethodName.GameOverRpc); return; }
        if (accept && caller.IsRaccoon) TriggerSabotage();

        SyncTasks();
        Rpc(MethodName.SyncCallerRpc, "", "", 0, _callers.Count);   // line goes quiet

        if (callersTask?.Done == true) { Say("Quota reached!", true); CheckLevelDone(); return; }
        if (_callerIndex + 1 < _callers.Count)
            ScheduleNextCaller(_callerIndex + 1, (CurrentLevel?.CallerGap ?? 6) * (0.75 + _rng.NextDouble() * 0.5));
        else
            FinishLevel();   // out of callers
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

    private void SendKeypadProblem(double staticChance = 0.2)
    {
        var p = _override.NewProblem(null, staticChance);
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
        if (!Multiplayer.IsServer() || CurrentLevel == null || Ended) return;

        var t = TaskOf(TaskKind.Piano);
        if (t != null)
        {
            t.Progress++;
            if (t.Progress >= t.Target)
            {
                t.Done = true;
                Piano.SetActive(false);
                if (FlippedModule == "Piano") Rpc(MethodName.SyncFlipRpc, "");
                Say("🎹 Piano finished!", true);
                SyncTasks(); CheckLevelDone();
                return;
            }
            SyncTasks();
        }
        Say("🎹 Jingle played!", true);
        StartRandomPiano();
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

    // Soundboard Host & RPCs
    public void StartRandomSoundboard(float timeLimit = 0f, bool showSequence = false)
    {
        if (!Multiplayer.IsServer()) return;
        _rng ??= new Random();
        var seqs = SoundboardData.GetSequences();
        var list = new List<string>(seqs.Keys);
        string name = list[_rng.Next(list.Count)];
        Rpc(MethodName.SoundboardConfigureRpc, name, timeLimit, showSequence);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SoundboardConfigureRpc(string sequenceName, float timeLimit, bool showSequence)
    {
        if (SoundboardData.GetSequences().TryGetValue(sequenceName, out var seq))
        {
            Soundboard.Configure(seq, timeLimit, showSequence);
            Soundboard.SetActive(true);
            Soundboard.Start();
        }
    }

    public void SoundboardPad(string padId)
    {
        if (!Multiplayer.HasMultiplayerPeer()) { Soundboard.RequestPad(padId); return; }
        Rpc(MethodName.SoundboardApplyPadRpc, padId);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SoundboardApplyPadRpc(string padId) => Soundboard.RequestPad(padId);

    public void SoundboardReset()
    {
        if (!Multiplayer.HasMultiplayerPeer()) { Soundboard.ResetSequence(); return; }
        Rpc(MethodName.SoundboardApplyResetRpc);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SoundboardApplyResetRpc() => Soundboard.ResetSequence();

    private void OnSoundboardCompleted()
    {
        if (!Multiplayer.IsServer() || CurrentLevel == null || Ended) return;
        var t = TaskOf(TaskKind.Soundboard);
        if (t != null && !t.Done)
        {
            t.Progress++;
            if (t.Progress >= t.Target) t.Done = true;
            SyncTasks();
        }
        Say("📻 Sound cue played!", true);
        StartRandomSoundboard();
    }

    // ---------- Memory (Module D) ----------
    public void MemoryStrike() => RpcId(1, MethodName.MemoryStrikeRpc);
    public void MemorySolved() => RpcId(1, MethodName.MemorySolvedRpc);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void MemoryStrikeRpc()
    {
        if (!Multiplayer.IsServer() || CurrentLevel == null || Ended || IsDone(TaskKind.Memory)) return;
        ModuleStrike(TaskKind.Memory, "Wrong memory button. That's a strike.");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void MemorySolvedRpc()
    {
        if (!Multiplayer.IsServer() || CurrentLevel == null || Ended) return;
        var t = TaskOf(TaskKind.Memory);
        if (t == null || t.Done) return;

        t.Progress++;
        if (t.Progress >= t.Target) t.Done = true;
        Say("Memory module solved!", true);
        SyncTasks();
        CheckLevelDone();
    }

    // ---------- Quality, sabotage, strikes (host) ----------
    public override void _Process(double delta)
    {
        if (CurrentLevel == null || IsTutorial || Ended || _broadcaster == null) return;
        if (!Multiplayer.HasMultiplayerPeer() || !Multiplayer.IsServer()) return;

        _qualityTick += delta;
        if (_qualityTick >= 1.0)
        {
            _qualityTick -= 1.0;
            int faults = FaultCount;
            ApplyAirtime(faults > 0 ? -CurrentLevel.FaultDrain * faults : CurrentLevel.CalmRecovery);
        }

        CheckHealthTasks(delta);

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
        if (!Multiplayer.IsServer() || Ended) return;

        var options = new List<Action>();
        bool pianoLive = CurrentLevel?.HasPiano == true && !IsDone(TaskKind.Piano);
        bool keypadLive = _override?.Problem != null && !IsDone(TaskKind.Keypad);

        if (pianoLive && Piano.Active && !Piano.InFault && Piano.Status == StudioPianoModule.PianoStatus.Working)
            options.Add(() => { Say("🦝 The raccoon is chewing the piano wires! Keys flashing red."); TriggerPianoFault(); });

        if (keypadLive && !_override.Problem.StaticFault)
            options.Add(() =>
            {
                var p = _override.Problem;
                p.StaticFault = true;
                Rpc(MethodName.SyncKeypadRpc, p.Line, p.Level, _override.Rule.EvenSuffix, _override.Rule.OddSuffix, true);
                Say(KeypadTag + "⚡ The raccoon is on the line! Static. Ask the Technician how to clear it.");
            });

        if (CurrentLevel?.FlipSabotage == true && FlippedModule == "")
        {
            if (keypadLive) options.Add(() => FlipModule("Keypad"));
            if (pianoLive) options.Add(() => FlipModule("Piano"));
        }

        if (options.Count == 0) return;
        options[_rng.Next(options.Count)]();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncStrikesRpc(int strikes)
    {
        bool gotStrike = strikes > Strikes;
        Strikes = strikes;
        StrikesChanged?.Invoke();
        if (gotStrike) ShowStrikeFlash();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void GameOverRpc()
    {
        GameOver = true;
        Piano.SetActive(false);
        _endScreen = new GameOverScreen();
        GetTree().Root.AddChild(_endScreen);
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
        if (IsInstanceValid(_endScreen)) _endScreen.QueueFree();
        _endScreen = null;
        Tasks.Clear(); FlippedModule = ""; LevelFinished = false;

        CurrentLevel = null; LevelId = ""; GameOver = false;
        _tutorial = null; _broadcaster = null; _override = null; _callers.Clear(); _endingDone.Clear();
        Strikes = 0; AirtimeQuality = 100; Topic = Genre.Drama;
        _sabotageTimer = -1; _qualityTick = 0;
        Piano.SetActive(false);
        Piano.Reset();
        Soundboard?.SetActive(false);
        Soundboard?.Reset();

        _session++; _callerOnLine = false;
    }

    // ---------- Tasks and score ----------
    public LevelTask TaskOf(TaskKind k) => Tasks.Find(t => t.Kind == k);
    public bool IsDone(TaskKind k) => TaskOf(k)?.Done == true;

    private void BuildTasks()
    {
        Tasks.Clear();
        var d = CurrentLevel;
        if (d == null) return;
        Tasks.Add(new LevelTask { Kind = TaskKind.Callers, Target = d.QuotaTarget });
        if (d.HasKeypad) Tasks.Add(new LevelTask { Kind = TaskKind.Keypad, Target = d.KeypadTarget });
        if (d.HasPiano) Tasks.Add(new LevelTask { Kind = TaskKind.Piano, Target = d.PianoTarget });
        if (d.HasSoundboard) Tasks.Add(new LevelTask { Kind = TaskKind.Soundboard, Target = d.SoundboardTarget });
        if (d.HasMemory) Tasks.Add(new LevelTask { Kind = TaskKind.Memory, Target = d.MemoryTarget });
        Tasks.Add(new LevelTask { Kind = TaskKind.Quality });
        Tasks.Add(new LevelTask { Kind = TaskKind.Signal });
    }

    public string TaskText(LevelTask t) => t.Kind switch
    {
        TaskKind.Callers => $"Accept {t.Target} {Topic} callers ({Math.Min(QuotaMet, t.Target)}/{t.Target})",
        TaskKind.Keypad => $"Route {t.Target} lines on the keypad ({t.Progress}/{t.Target})",
        TaskKind.Piano => $"Play {t.Target} jingles ({t.Progress}/{t.Target})",
        TaskKind.Soundboard => $"Play {t.Target} sound cues ({t.Progress}/{t.Target})",
        TaskKind.Memory => $"Solve {t.Target} memory puzzle{(t.Target == 1 ? "" : "s")} ({t.Progress}/{t.Target})",
        TaskKind.Quality => $"Keep Quality above {CurrentLevel?.QualityFloor}%",
        TaskKind.Signal => $"Keep Signal above {CurrentLevel?.SignalFloor}%",
        _ => t.Kind.ToString()   // safety net so a new TaskKind never crashes the game
    };

    // The same text on every role's Task box.
    public string TaskBbcode()
    {
        var sb = new StringBuilder();
        foreach (var t in Tasks)
        {
            string text = TaskText(t);
            if (t.Failed) sb.Append($"[color=#B3261E]- {text} (FAILED)[/color]\n");
            else if (t.Done) sb.Append($"[color=#7A7A7A][s]- {text}[/s][/color]\n");
            else sb.Append($"- {text}\n");
        }
        return sb.ToString().TrimEnd('\n');
    }

    public static (string Rank, string Title) Rating(int score) =>
        score >= 40 ? ("S", "Perfect Broadcast") :
        score >= 31 ? ("A", "Golden Broadcast") :
        score >= 25 ? ("B", "Stable Broadcast") :
                      ("F", "Broadcast Failure");

    private void AddMistake(TaskKind k)
    {
        var t = TaskOf(k);
        if (t == null) return;
        t.Mistakes++;
        SyncTasks();
    }

    private void OnPianoKey(string letter, bool correct)
    {
        if (!Multiplayer.IsServer() || CurrentLevel == null || Ended || correct) return;
        if (!IsDone(TaskKind.Piano)) ModuleStrike(TaskKind.Piano, "Wrong piano key. That's a strike.");
    }

    // Host only: a wrong answer inside a module.
    private void ModuleStrike(TaskKind kind, string message)
    {
        if (!Multiplayer.IsServer() || CurrentLevel == null || _broadcaster == null || Ended) return;

        var task = TaskOf(kind);
        if (task != null) task.Mistakes++;   // always hurts that task's accuracy score
        SyncTasks();

        ulong now = Time.GetTicksMsec();
        if (_lastModuleStrikeMs != 0 && now - _lastModuleStrikeMs < ModuleStrikeCooldownMs) return;   // button mashing counts once
        _lastModuleStrikeMs = now;

        _broadcaster.ExternalStrike();
        Rpc(MethodName.SyncStrikesRpc, _broadcaster.Strikes);
        Say(message);
        if (_broadcaster.Strikes >= _broadcaster.MaxStrikes) Rpc(MethodName.GameOverRpc);
    }

    // Runs on every peer: the whole screen flashes red for about a second.
    private void ShowStrikeFlash()
    {
        var layer = new CanvasLayer { Layer = 90 };   // above gameplay, below the end screens
        GetTree().Root.AddChild(layer);

        var rect = new ColorRect { Color = new Color(0.9f, 0.1f, 0.1f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(rect);
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var tween = layer.CreateTween();
        tween.TweenProperty(rect, "color:a", 0.5f, 0.08);   // snap to red
        tween.TweenProperty(rect, "color:a", 0.0f, 0.92);   // fade out
        tween.TweenCallback(Callable.From(layer.QueueFree));
    }

    private void OnKeypadRouted()
    {
        var t = TaskOf(TaskKind.Keypad);
        if (t == null) { Say(KeypadTag + "Line routed!", true); SendKeypadProblem(); return; }

        t.Progress++;
        Say(KeypadTag + "Line routed!", true);
        if (t.Progress >= t.Target)
        {
            t.Done = true;
            Rpc(MethodName.SyncKeypadRpc, 0, 0, _override.Rule.EvenSuffix, _override.Rule.OddSuffix, false);   // line 0 = nothing incoming
            if (FlippedModule == "Keypad") Rpc(MethodName.SyncFlipRpc, "");
            Say(KeypadTag + "✔ All lines routed.", true);
        }
        else SendKeypadProblem();

        SyncTasks();
        CheckLevelDone();
    }

    private void CheckHealthTasks(double delta)
    {
        var d = CurrentLevel;
        bool changed = false;

        var q = TaskOf(TaskKind.Quality);
        if (q != null && !q.Failed)
        {
            _qualityLow = AirtimeQuality <= d.QualityFloor ? _qualityLow + delta : 0;   // resets when it recovers
            if (_qualityLow >= d.HealthGrace) { q.Failed = true; changed = true; }
        }

        var s = TaskOf(TaskKind.Signal);
        if (s != null && !s.Failed)
        {
            _signalLow = Signal <= d.SignalFloor ? _signalLow + delta : 0;
            if (_signalLow >= d.HealthGrace) { s.Failed = true; changed = true; }
        }

        if (changed) SyncTasks();
    }
    private void CheckLevelDone()
    {
        foreach (var t in Tasks)
            if (!t.IsHealth && !t.Done) return;
        FinishLevel();
    }

    private void FinishLevel()
    {
        if (!Multiplayer.IsServer() || Ended || Tasks.Count == 0) return;

        int total = Tasks.Count, completed = 0;
        double share = 25.0 / total, accuracy = 0;
        foreach (var t in Tasks)
        {
            bool ok = t.IsHealth ? !t.Failed : t.Done;
            if (!ok) continue;
            completed++;
            accuracy += share * Math.Max(0.0, 1.0 - 0.25 * t.Mistakes);   // each mistake costs 25% of this task's share
        }
        int completionPts = Mathf.RoundToInt(25f * completed / total);
        int accuracyPts = Mathf.RoundToInt((float)accuracy);
        Rpc(MethodName.LevelCompleteRpc, completionPts, accuracyPts, completed, total);
    }

    private void SyncTasks()
    {
        var data = new int[Tasks.Count * 4];
        for (int i = 0; i < Tasks.Count; i++)
        {
            var t = Tasks[i];
            data[i * 4] = t.Progress; data[i * 4 + 1] = t.Done ? 1 : 0;
            data[i * 4 + 2] = t.Failed ? 1 : 0; data[i * 4 + 3] = t.Mistakes;
        }
        Rpc(MethodName.SyncTasksRpc, data);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncTasksRpc(int[] data)
    {
        for (int i = 0; i < Tasks.Count && i * 4 + 3 < data.Length; i++)
        {
            var t = Tasks[i];
            t.Progress = data[i * 4]; t.Done = data[i * 4 + 1] == 1;
            t.Failed = data[i * 4 + 2] == 1; t.Mistakes = data[i * 4 + 3];
        }
        TasksChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void LevelCompleteRpc(int completionPts, int accuracyPts, int completed, int total)
    {
        LevelFinished = true;
        Piano.SetActive(false);
        _endScreen = new LevelResultScreen { CompletionPts = completionPts, AccuracyPts = accuracyPts, Completed = completed, Total = total };
        GetTree().Root.AddChild(_endScreen);
    }

    // ---------- Upside-down sabotage ----------
    private void FlipModule(string module)
    {
        Say($"🦝 The raccoon flipped the {module} upside down! Press FLIP BACK on it.");
        Rpc(MethodName.SyncFlipRpc, module);
    }

    public void RequestUnflip() => RpcId(1, MethodName.UnflipRpc);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void UnflipRpc()
    {
        if (!Multiplayer.IsServer() || FlippedModule == "") return;
        Rpc(MethodName.SyncFlipRpc, "");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncFlipRpc(string module)
    {
        FlippedModule = module;
        FlipChanged?.Invoke();
    }
}
