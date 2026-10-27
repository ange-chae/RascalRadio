namespace RascalRadio.Core.Systems;

/// <summary>Operator-side keypad entry. The HOST decides if the number is correct.</summary>
public class KeypadLogic
{
	public const int MaxDigits = 4;   // biggest code is 27 + suffix = 2705
	public string Entry { get; private set; } = "";

	public void Press(int digit)
	{
		if (Entry.Length < MaxDigits) Entry += digit.ToString();
	}

	public void Clear() => Entry = "";

	public bool HasEntry => Entry.Length > 0;
}
