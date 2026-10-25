using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RascalRadio.Core.Models;
using RascalRadio.Core.Systems;

using Godot;

public partial class OperatorInterface : PopupFrame
{
	public override void _Ready()
	{
		base._Ready();
		// Call SetCaller(...) from your NetworkManager when a new caller arrives.
	}

	public void SetCaller(string name, int number, int total, string quote)
	{
		GetNode<Label>("%NameLabel").Text = name;
		GetNode<Label>("%CallerNumLabel").Text = $"Caller {number} of {total}";
		GetNode<Label>("%QuoteLabel").Text = quote;
	}

	public void SetVerdict(bool accept)
	{
		GetNode<Label>("%VerdictLabel").Text = accept ? "ACCEPT" : "REJECT";
	}
}
