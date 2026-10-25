using Godot;

public partial class TechnicianInterface : PopupFrame
{
	private static readonly (string Title, string Body)[] Pages =
	{
		("STATION BASICS",
		 "1. Read the topic and the quota at the top.\n2. Operator reads the caller story out loud.\n3. Operator says ACCEPT or REJECT.\n4. Broadcaster presses the matching button.\n5. Everyone checks that quota and strikes updated."),
		("STATUS WORDS", "TODO: list your status words here."),
		("STRIKES AND QUOTA", "TODO: explain quota goals and the strike limit."),
		("RACCOON WATCH", "TODO: explain the raccoon event."),
	};

	public override void _Ready()
	{
		base._Ready();
		string[] tabs = { "%TabBasics", "%TabStatus", "%TabStrikes", "%TabRaccoon" };
		for (int i = 0; i < tabs.Length; i++)
		{
			int idx = i;
			GetNode<Button>(tabs[i]).Pressed += () => ShowPage(idx);
		}
		ShowPage(0);
	}

	private void ShowPage(int i)
	{
		GetNode<Label>("%PageTitle").Text = Pages[i].Title;
		GetNode<Label>("%PageTag").Text = $"Page {i + 1} of {Pages.Length}";
		GetNode<RichTextLabel>("%PageText").Text = Pages[i].Body;
	}

	public void SetHealth(int signal, int quality)
	{
		GetNode<ProgressBar>("%SignalBar").Value = signal;
		GetNode<Label>("%SignalLabel").Text = $"Signal {signal}%";
		GetNode<ProgressBar>("%QualityBar").Value = quality;
		GetNode<Label>("%QualityLabel").Text = $"Quality {quality}%";
	}
}
