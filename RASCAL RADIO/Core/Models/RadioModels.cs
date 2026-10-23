using System.Collections.Generic;

namespace RascalRadio.Core.Models
{
	public enum RadioChannel { Zebra, Alpha, Delta }
	public enum LinkStatus { Offline, Searching, Linked }
	public enum PowerFault { None, FuseTripped, StartupFailed }
	public enum WarningLight { None, Power, Channel, Module, Generator, Signal }
	public enum CallLoss { RadioOff, SignalLost, ChannelChange }

	public static class RadioChannels
	{
		public static int Code(RadioChannel channel) => channel switch
		{
			RadioChannel.Zebra => 173,
			RadioChannel.Alpha => 246,
			_ => 315,
		};
	}

	public static class RadioRules
	{
		public const int PowerSwitchCount = 4;
		public const int DipSwitchCount = 8;
		public const int ComponentCount = 3;
		public const int DialMin = 0;
		public const int DialMax = 400;

		public const float RunningVoltage = 75f;
		public const float SafeVoltageMin = 60f;
		public const float VoltageRiseSeconds = 4f;
		public const float LockSeconds = 5f;

		public const float TuneSettleSeconds = 1.5f;
		public const float WrongChannelDelaySeconds = 3f;
		public const float LinkThreshold = 50f;
		public const float CompromisedPenalty = 45f;
		public const float MistunedPenalty = 60f;
		public const float CorruptModulePenalty = 30f;

		public const int FailedTestsBeforeLockout = 3;
		public const float DipLockSeconds = 5f;
		public const float DipLightSeconds = 2f;

		public const float GeneratorLowPercent = 25f;

		public static readonly string[] PowerSwitchLabels = { "Main Isolation", "Filament Heater", "Plate Voltage", "Antenna Load" };
		public static readonly string[] ComponentNames = { "Breaker A", "Breaker B", "Power Relay" };
	}

	public sealed class WarningInfo
	{
		public string Code { get; init; }
		public WarningLight Light { get; init; }
		public string Panel { get; init; }
		public string Meaning { get; init; }
	}

	public static class WarningCatalog
	{
		public static readonly WarningInfo FuseTripped = new() { Code = "E-10", Light = WarningLight.Power, Panel = "A", Meaning = "Master fuse tripped" };
		public static readonly WarningInfo StartupFailed = new() { Code = "E-11", Light = WarningLight.Power, Panel = "A", Meaning = "Startup failed" };
		public static readonly WarningInfo SystemLocked = new() { Code = "E-12", Light = WarningLight.Power, Panel = "A", Meaning = "System temporarily locked" };
		public static readonly WarningInfo Interference = new() { Code = "E-20", Light = WarningLight.Channel, Panel = "B", Meaning = "Interference on the active channel" };
		public static readonly WarningInfo NoConnection = new() { Code = "E-21", Light = WarningLight.Channel, Panel = "B", Meaning = "Dial does not match the channel code" };
		public static readonly WarningInfo ModuleCorrupted = new() { Code = "E-30", Light = WarningLight.Module, Panel = "C", Meaning = "Module configuration corrupted" };
		public static readonly WarningInfo ModuleLocked = new() { Code = "E-31", Light = WarningLight.Module, Panel = "C", Meaning = "Module locked out after failed tests" };
		public static readonly WarningInfo Outage = new() { Code = "E-40", Light = WarningLight.Generator, Panel = "D", Meaning = "Main power outage, running on backup" };
		public static readonly WarningInfo GeneratorLow = new() { Code = "E-41", Light = WarningLight.Generator, Panel = "D", Meaning = "Generator charge is low" };
		public static readonly WarningInfo WeakSignal = new() { Code = "E-50", Light = WarningLight.Signal, Panel = "E", Meaning = "Signal strength falling" };

		public static readonly IReadOnlyList<WarningInfo> All = new List<WarningInfo>
		{
			FuseTripped, StartupFailed, SystemLocked, Interference, NoConnection,
			ModuleCorrupted, ModuleLocked, Outage, GeneratorLow, WeakSignal,
		};
	}

	public sealed class RadioSnapshot
	{
		public bool RadioOn { get; init; }
		public bool LeverOn { get; init; }
		public bool[] PowerSwitches { get; init; }
		public RadioChannel Channel { get; init; }
		public int Dial { get; init; }
		public bool SignalStable { get; init; }
		public float Signal { get; init; }
		public LinkStatus Link { get; init; }
		public bool ModuleCorrupted { get; init; }
		public bool[] DipSwitches { get; init; }
		public bool MainPowerOut { get; init; }
		public float GeneratorCharge { get; init; }
		public bool HasIncomingCall { get; init; }
	}
}
