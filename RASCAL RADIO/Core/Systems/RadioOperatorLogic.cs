using System;
using System.Collections.Generic;
using System.Linq;
using RascalRadio.Core.Models;

namespace RascalRadio.Core.Systems
{
	public sealed class RadioOperatorLogic
	{
		public event Action Changed;
		public event Action<string> Notice;
		public event Action<bool> CallDecided;
		public event Action<CallLoss> CallLost;
		public event Action<bool> PowerChanged;
		public event Action<RadioChannel> ChannelChanged;
		public event Action ModuleRestored;
		public event Action<int, bool> ComponentReset;
		public event Action GeneratorDepleted;

		public bool RadioOn { get; private set; }
		public bool LeverOn { get; private set; }
		public PowerFault Fault { get; private set; }
		public float Voltage { get; private set; }
		public float LockLeft { get; private set; }

		public RadioChannel Channel { get; private set; } = RadioChannel.Zebra;
		public int Dial { get; private set; }
		public RadioChannel? CompromisedChannel { get; private set; }
		public bool SignalStable { get; private set; }
		public float AttackPenalty { get; private set; }

		public bool ModuleCorrupted { get; private set; }
		public int TargetCode { get; private set; }
		public float DipLockLeft { get; private set; }
		public float DipLightLeft { get; private set; }

		public bool MainPowerOut { get; private set; }
		public float GeneratorCharge { get; private set; } = 100f;
		public int FailedComponent { get; private set; } = -1;

		public bool HasIncomingCall { get; private set; }

		private readonly bool[] _powerSwitches = new bool[RadioRules.PowerSwitchCount];
		private readonly bool[] _dipSwitches = new bool[RadioRules.DipSwitchCount];
		private float _settleLeft = RadioRules.TuneSettleSeconds;
		private float _channelDelayLeft;
		private bool _awaitingConfirm;
		private int _failedTests;
		private bool _needsRestart;
		private bool _depletedRaised;

		public bool InSafeZone => Voltage >= RadioRules.SafeVoltageMin;
		public bool DipLightOn => DipLightLeft > 0f;
		public bool DialMatches => Dial == RadioChannels.Code(Channel);

		public bool GetPowerSwitch(int index) => _powerSwitches[index];
		public bool GetDipSwitch(int index) => _dipSwitches[index];
		public bool ComponentFaulted(int index) => MainPowerOut && index == FailedComponent;

		public int DipValue
		{
			get
			{
				int sum = 0;
				for (int i = 0; i < _dipSwitches.Length; i++)
					if (_dipSwitches[i]) sum += 128 >> i;
				return sum;
			}
		}

		public float Signal
		{
			get
			{
				if (!RadioOn) return 0f;

				float signal = 100f - AttackPenalty;
				if (CompromisedChannel == Channel) signal -= RadioRules.CompromisedPenalty;
				if (!DialMatches) signal -= RadioRules.MistunedPenalty;
				if (ModuleCorrupted) signal -= RadioRules.CorruptModulePenalty;
				return Math.Clamp(signal, 0f, 100f);
			}
		}

		public LinkStatus Link
		{
			get
			{
				if (!RadioOn) return LinkStatus.Offline;
				return Signal >= RadioRules.LinkThreshold && SignalStable ? LinkStatus.Linked : LinkStatus.Searching;
			}
		}

		public IReadOnlyList<WarningInfo> ActiveWarnings
		{
			get
			{
				var list = new List<WarningInfo>();
				if (Fault == PowerFault.FuseTripped) list.Add(WarningCatalog.FuseTripped);
				if (Fault == PowerFault.StartupFailed) list.Add(WarningCatalog.StartupFailed);
				if (LockLeft > 0f) list.Add(WarningCatalog.SystemLocked);
				if (RadioOn && CompromisedChannel == Channel) list.Add(WarningCatalog.Interference);
				if (RadioOn && !DialMatches) list.Add(WarningCatalog.NoConnection);
				if (RadioOn && ModuleCorrupted) list.Add(WarningCatalog.ModuleCorrupted);
				if (RadioOn && DipLockLeft > 0f) list.Add(WarningCatalog.ModuleLocked);
				if (MainPowerOut) list.Add(WarningCatalog.Outage);
				if (MainPowerOut && GeneratorCharge < RadioRules.GeneratorLowPercent) list.Add(WarningCatalog.GeneratorLow);
				if (RadioOn && AttackPenalty > 0f) list.Add(WarningCatalog.WeakSignal);
				return list;
			}
		}

		public IEnumerable<string> WarningTexts =>
			ActiveWarnings.Select(w =>
				w.Code == WarningCatalog.Interference.Code && CompromisedChannel.HasValue
					? $"{w.Code} {CompromisedChannel.Value}"
					: w.Code);

		public RadioSnapshot GetSnapshot() => new RadioSnapshot
		{
			RadioOn = RadioOn,
			LeverOn = LeverOn,
			PowerSwitches = (bool[])_powerSwitches.Clone(),
			Channel = Channel,
			Dial = Dial,
			SignalStable = SignalStable,
			Signal = Signal,
			Link = Link,
			ModuleCorrupted = ModuleCorrupted,
			DipSwitches = (bool[])_dipSwitches.Clone(),
			MainPowerOut = MainPowerOut,
			GeneratorCharge = GeneratorCharge,
			HasIncomingCall = HasIncomingCall,
		};

		public void SetPowerSwitch(int index, bool on)
		{
			if (index < 0 || index >= _powerSwitches.Length) return;
			if (LockLeft > 0f) { Say("System locked. Wait."); return; }

			if (on)
			{
				if (Fault != PowerFault.None) { Say("Panel is faulted."); return; }
				if (_powerSwitches[index]) { Changed?.Invoke(); return; }

				if (index != NextExpectedSwitch())
				{
					Fault = PowerFault.FuseTripped;
					Say("Wrong order! Master fuse tripped.");
					return;
				}

				if (index == 2 && !InSafeZone)
				{
					LockLeft = RadioRules.LockSeconds;
					Say("Voltage not stable. System locked.");
					return;
				}

				_powerSwitches[index] = true;
				Changed?.Invoke();
				return;
			}

			_powerSwitches[index] = false;

			if (RadioOn)
			{
				LeverOn = false;
				ShutDownRadio();
				Say("Radio shut down.");
				return;
			}

			ClearFaultIfReset();
			Changed?.Invoke();
		}

		public void SetLever(bool on)
		{
			if (LockLeft > 0f) { Say("System locked. Wait."); return; }

			if (on)
			{
				if (RadioOn || LeverOn) { Changed?.Invoke(); return; }
				if (Fault != PowerFault.None) { Say("Panel is faulted."); return; }

				LeverOn = true;

				if (_powerSwitches.All(s => s))
				{
					RadioOn = true;
					PowerChanged?.Invoke(true);
					Say("Radio is on.");
				}
				else
				{
					Fault = PowerFault.StartupFailed;
					Say("Lever pulled too early. Startup failed.");
				}
				return;
			}

			LeverOn = false;

			if (RadioOn)
			{
				ShutDownRadio();
				Say("Radio shut down.");
				return;
			}

			ClearFaultIfReset();
			Changed?.Invoke();
		}

		public void SelectChannel(RadioChannel channel)
		{
			if (!RequireOn()) return;
			if (channel == Channel) { Changed?.Invoke(); return; }

			if (_awaitingConfirm && HasIncomingCall) LoseCall(CallLoss.ChannelChange);

			Channel = channel;
			_awaitingConfirm = true;
			SignalStable = false;
			_settleLeft = RadioRules.TuneSettleSeconds;
			if (CompromisedChannel == channel) _channelDelayLeft = RadioRules.WrongChannelDelaySeconds;

			ChannelChanged?.Invoke(channel);
			Changed?.Invoke();
		}

		public void SetDial(int value)
		{
			if (!RequireOn()) return;

			Dial = Math.Clamp(value, RadioRules.DialMin, RadioRules.DialMax);
			Changed?.Invoke();
		}

		public void AdjustDial(int delta) => SetDial(Dial + delta);

		public void SetDipSwitch(int index, bool on)
		{
			if (index < 0 || index >= _dipSwitches.Length) return;
			if (!RequireOn()) return;
			if (DipLockLeft > 0f) { Say("Module locked out. Wait."); return; }

			_dipSwitches[index] = on;
			Changed?.Invoke();
		}

		public void PressDipTest()
		{
			if (!RequireOn()) return;
			if (DipLockLeft > 0f) { Say("Module locked out. Wait."); return; }

			if (!ModuleCorrupted)
			{
				Say("TEST OK. Module is fine.");
				return;
			}

			if (DipValue == TargetCode)
			{
				ModuleCorrupted = false;
				_failedTests = 0;
				DipLightLeft = RadioRules.DipLightSeconds;
				ModuleRestored?.Invoke();
				Say("TEST OK. Module restored.");
				return;
			}

			_failedTests++;
			if (_failedTests >= RadioRules.FailedTestsBeforeLockout)
			{
				_failedTests = 0;
				DipLockLeft = RadioRules.DipLockSeconds;
				Say("TEST FAILED. Module locked out.");
				return;
			}

			Say("TEST FAILED.");
		}

		public void ResetComponent(int index)
		{
			if (index < 0 || index >= RadioRules.ComponentCount) return;

			if (!MainPowerOut)
			{
				Say("Nothing to reset. Main power is fine.");
				return;
			}

			bool correct = index == FailedComponent;
			ComponentReset?.Invoke(index, correct);

			if (!correct)
			{
				Say($"{RadioRules.ComponentNames[index]} reset. No effect.");
				return;
			}

			RestoreMainPower();
		}

		public void AcceptCall() => DecideCall(true);
		public void RejectCall() => DecideCall(false);

		private void DecideCall(bool accepted)
		{
			if (!RequireOn()) return;
			if (!HasIncomingCall) { Say("No call on the line."); return; }
			if (accepted && Link != LinkStatus.Linked) { Say("No link. Fix the radio first."); return; }

			HasIncomingCall = false;
			CallDecided?.Invoke(accepted);
			Say(accepted ? "Call accepted." : "Call rejected.");
		}

		public void SetIncomingCall(bool ringing)
		{
			HasIncomingCall = ringing;
			Changed?.Invoke();
		}

		public void SetAntennaAttack(float penalty)
		{
			AttackPenalty = Math.Clamp(penalty, 0f, 100f);
			Changed?.Invoke();
		}

		public void SetCompromisedChannel(RadioChannel? channel)
		{
			CompromisedChannel = channel;
			Changed?.Invoke();
		}

		public void CorruptModule(int targetCode)
		{
			ModuleCorrupted = true;
			TargetCode = Math.Clamp(targetCode, 1, 255);
			_failedTests = 0;
			Array.Clear(_dipSwitches, 0, _dipSwitches.Length);
			Changed?.Invoke();
		}

		public void StartOutage(int failedComponent, bool equipmentNeedsRestart = true)
		{
			if (MainPowerOut) return;

			MainPowerOut = true;
			FailedComponent = Math.Clamp(failedComponent, 0, RadioRules.ComponentCount - 1);
			_needsRestart = equipmentNeedsRestart;
			_depletedRaised = false;
			Say("Main power lost! Running on backup.");
		}

		public void SetGeneratorCharge(float percent)
		{
			GeneratorCharge = Math.Clamp(percent, 0f, 100f);

			if (MainPowerOut && GeneratorCharge <= 0f && !_depletedRaised)
			{
				_depletedRaised = true;
				GeneratorDepleted?.Invoke();
				Say("Generator empty!");
				return;
			}

			Changed?.Invoke();
		}

		public void RestoreMainPower()
		{
			if (!MainPowerOut) return;

			MainPowerOut = false;
			FailedComponent = -1;

			if (_needsRestart && RadioOn)
			{
				ShutDownRadio();
				Say("Main power restored. Equipment is offline.");
				return;
			}

			Say("Main power restored.");
		}

		public void DemoQuickStart()
		{
			if (RadioOn) return;

			Fault = PowerFault.None;
			LockLeft = 0f;
			for (int i = 0; i < _powerSwitches.Length; i++) _powerSwitches[i] = true;
			LeverOn = true;
			Voltage = RadioRules.RunningVoltage;
			Channel = RadioChannel.Zebra;
			Dial = RadioChannels.Code(Channel);
			RadioOn = true;
			PowerChanged?.Invoke(true);
			Say("Radio is on.");
		}

		public void Tick(double delta)
		{
			float dt = (float)delta;
			bool dirty = false;

			bool heating = _powerSwitches[0] && _powerSwitches[1] && Fault == PowerFault.None;
			float targetVoltage = heating ? RadioRules.RunningVoltage : 0f;
			float step = RadioRules.RunningVoltage / RadioRules.VoltageRiseSeconds * dt;
			float before = Voltage;
			if (Voltage < targetVoltage) Voltage = Math.Min(targetVoltage, Voltage + step);
			else if (Voltage > targetVoltage) Voltage = Math.Max(targetVoltage, Voltage - step);
			if (Voltage != before) dirty = true;

			if (LockLeft > 0f)
			{
				LockLeft = Math.Max(0f, LockLeft - dt);
				dirty = true;
			}

			if (DipLockLeft > 0f)
			{
				DipLockLeft = Math.Max(0f, DipLockLeft - dt);
				dirty = true;
			}

			if (DipLightLeft > 0f)
			{
				DipLightLeft = Math.Max(0f, DipLightLeft - dt);
				dirty = true;
			}

			if (_channelDelayLeft > 0f)
			{
				_channelDelayLeft = Math.Max(0f, _channelDelayLeft - dt);
				dirty = true;
			}

			bool ready = RadioOn && DialMatches && CompromisedChannel != Channel && _channelDelayLeft <= 0f;
			if (ready)
			{
				if (!SignalStable)
				{
					_settleLeft -= dt;
					if (_settleLeft <= 0f)
					{
						SignalStable = true;
						_awaitingConfirm = false;
						Say("Signal stable.");
					}
				}
			}
			else
			{
				if (SignalStable)
				{
					SignalStable = false;
					dirty = true;
				}
				_settleLeft = RadioRules.TuneSettleSeconds;
			}

			if (RadioOn && HasIncomingCall && Signal <= 0f)
				LoseCall(CallLoss.SignalLost);

			if (dirty) Changed?.Invoke();
		}

		private int NextExpectedSwitch()
		{
			for (int i = 0; i < _powerSwitches.Length; i++)
				if (!_powerSwitches[i]) return i;
			return -1;
		}

		private void ClearFaultIfReset()
		{
			if (Fault == PowerFault.None || LeverOn || _powerSwitches.Any(s => s)) return;

			Fault = PowerFault.None;
			Say("Panel reset.");
		}

		private void ShutDownRadio()
		{
			if (!RadioOn) return;

			RadioOn = false;
			SignalStable = false;
			_awaitingConfirm = false;
			_settleLeft = RadioRules.TuneSettleSeconds;
			_channelDelayLeft = 0f;

			if (HasIncomingCall) LoseCall(CallLoss.RadioOff);
			PowerChanged?.Invoke(false);
		}

		private void LoseCall(CallLoss reason)
		{
			HasIncomingCall = false;
			CallLost?.Invoke(reason);

			Say(reason switch
			{
				CallLoss.RadioOff => "The radio died. Caller lost.",
				CallLoss.SignalLost => "Signal lost. Caller disconnected.",
				_ => "Channel changed too fast. Caller interrupted.",
			});
		}

		private bool RequireOn()
		{
			if (RadioOn) return true;

			Say("Radio is off. Start it up first.");
			return false;
		}

		private void Say(string message)
		{
			Notice?.Invoke(message);
			Changed?.Invoke();
		}
	}
}
