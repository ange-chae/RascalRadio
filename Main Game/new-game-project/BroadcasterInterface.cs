using Godot;

public partial class BroadcasterInterface : Control
{
	private Label _strikesLabel;
	private Label _timerLabel;
	private ProgressBar _qualityBar;
	private Label _quotaLabel;
	private Label _nameLabel;
	private Label _blurbLabel;
	private Label _feedbackLabel;
	private ProgressBar _patienceBar;
	private Button _acceptButton;
	private Button _rejectButton;

	private string _quota = "Drama";
	private int _strikes = 0;
	private int _index = 0;
	private bool _over = false;

	// Percent per second. 12 is about 8 seconds per caller.
	private const float PatienceDrainPerSecond = 12f;
	private float _patience = 100f;

	private float _testTime = 300f; // TEMP: fake clock, delete later

	// Fake callers for testing. The genre is hidden from the player.
	private readonly string[] _names = { "Maria", "Jun", "Lola", "Ben" };
	private readonly string[] _blurbs =
	{
		"Wants to share a story about her family.",
		"Has a funny joke about his neighbor's rooster.",
		"Her love story ended in a dramatic breakup.",
        "Wants to request a song for his friend."
	};
	private readonly string[] _genres = { "Drama", "Comedy", "Drama", "Music" };

	public override void _Ready()
	{
		_strikesLabel = GetNode<Label>("Center/Layout/TopBar/StrikesLabel");
		_timerLabel = GetNode<Label>("Center/Layout/TopBar/TimerLabel");
		_qualityBar = GetNode<ProgressBar>("Center/Layout/TopBar/QualityBar");
		_quotaLabel = GetNode<Label>("Center/Layout/CallerCard/CardLayout/QuotaLabel");
		_nameLabel = GetNode<Label>("Center/Layout/CallerCard/CardLayout/CallerNameLabel");
		_blurbLabel = GetNode<Label>("Center/Layout/CallerCard/CardLayout/CallerBlurbLabel");
		_patienceBar = GetNode<ProgressBar>("Center/Layout/CallerCard/CardLayout/PatienceBar");
		_feedbackLabel = GetNode<Label>("Center/Layout/CallerCard/CardLayout/FeedbackLabel");
		_acceptButton = GetNode<Button>("Center/Layout/CallerCard/CardLayout/ButtonRow/AcceptButton");
		_rejectButton = GetNode<Button>("Center/Layout/CallerCard/CardLayout/ButtonRow/RejectButton");

		_qualityBar.MinValue = 0;
		_qualityBar.MaxValue = 100;
		_patienceBar.MinValue = 0;
		_patienceBar.MaxValue = 100;
		_patienceBar.ShowPercentage = false;

		_acceptButton.Pressed += OnAccept;
		_rejectButton.Pressed += OnReject;

		_quotaLabel.Text = "Quota: " + _quota;
		_feedbackLabel.Text = "Read the blurb, then decide!";
		ShowCaller();
	}

	public override void _Process(double delta)
	{
		// TEMP: pretend the game session is ticking. Delete these 3 lines later.
		_testTime -= (float)delta;
		SetTimeRemaining(_testTime);
		SetQuality(_testTime / 3f);

		if (_over) return;

		_patience -= PatienceDrainPerSecond * (float)delta;
		_patienceBar.Value = _patience;

		if (_patience <= 0)
		{
			_feedbackLabel.Text = "Caller hung up!";
			NextCaller();
		}
	}

	private void OnAccept()
	{
		if (_genres[_index] != _quota)
		{
			_strikes++;
			_strikesLabel.Text = "Strikes: " + _strikes + "/3";
			_feedbackLabel.Text = "Wrong genre! Strike.";
		}
		else
		{
			_feedbackLabel.Text = "Good pick!";
		}

		if (_strikes >= 3)
		{
			_over = true;
			_nameLabel.Text = "Struck out!";
			_blurbLabel.Text = "";
			_acceptButton.Disabled = true;
			_rejectButton.Disabled = true;
			return;
		}

		NextCaller();
	}

	private void OnReject()
	{
		_feedbackLabel.Text = "Caller rejected.";
		NextCaller();
	}

	private void NextCaller()
	{
		_index = (_index + 1) % _names.Length;
		ShowCaller();
	}

	private void ShowCaller()
	{
		_nameLabel.Text = "Caller: " + _names[_index];
		_blurbLabel.Text = _blurbs[_index];
		_patience = 100f;
		_patienceBar.Value = 100;
	}

	// Your teammates call these. The screen only displays what it's given.
	public void SetTimeRemaining(float seconds)
	{
		int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
		_timerLabel.Text = $"{total / 60:00}:{total % 60:00}";
	}

	public void SetQuality(float percent)
	{
		_qualityBar.Value = percent;
	}
}
