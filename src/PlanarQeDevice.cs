using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;
using PepperDash.Core.Logging;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using PepperDash.Essentials.Core.DeviceTypeInterfaces;
using PepperDash.Essentials.Core.Queues;
using PepperDash.Essentials.Devices.Common.Displays;

namespace Pepperdash.Essentials.Plugins.Display.Planar.Qe
{
	public class PlanarQeController : TwoWayDisplayBase, ICommunicationMonitor, IHasInputs<string>,
		IBridgeAdvanced, IRoutingSinkWithSwitchingWithInputPort
	{
		private PlanarQePropertiesConfig props;

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="key"></param>
		/// <param name="name"></param>
		/// <param name="config"></param>
		/// <param name="comms"></param>
		public PlanarQeController(string key, string name, PlanarQePropertiesConfig config, IBasicCommunication comms)
			: base(key, name)
		{
			props = config;

			if (props == null)
			{
				this.LogError("configuration must be included");
				return;
			}

			Communication = comms;

			_receiveQueue = new GenericQueue(key + "-queue");

			PortGather = new CommunicationGather(Communication, GatherDelimiter);
			PortGather.LineReceived += PortGather_LineReceived;

			var pollIntervalMs = props.PollIntervalMs > 45000 ? props.PollIntervalMs : 45000;
			CommunicationMonitor = new GenericCommunicationMonitor(this, Communication, pollIntervalMs, 180000, 300000,
				StatusGet);

			CommunicationMonitor.StatusChange += CommunicationMonitor_StatusChange;

			WarmupTime = props.WarmingTimeMs < 15000 ? props.WarmingTimeMs : 15000;
			CooldownTime = props.CoolingTimeMs < 15000 ? props.CoolingTimeMs : 15000;

			InitializeInputs();
		}


		#region IBridgeAdvanced Members

		/// <summary>
		/// LinkToApi
		/// </summary>
		/// <param name="trilist"></param>
		/// <param name="joinStart"></param>
		/// <param name="joinMapKey"></param>
		/// <param name="bridge"></param>
		public void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApiAdvanced bridge)
		{
			var joinMap = new PlanarQeBridgeJoinMap(joinStart);

			// This adds the join map to the collection on the bridge
			bridge?.AddJoinMap(Key, joinMap);

			var customJoins = JoinMapHelper.TryGetJoinMapAdvancedForDevice(joinMapKey);
			if (customJoins != null)
			{
				joinMap.SetCustomJoinData(customJoins);
			}

			this.LogInformation("Linking to Trilist '{0}'", trilist.ID.ToString("X"));
			this.LogInformation("Linking to Bridge Type {0}", GetType().Name);

			// links to bridge
			// device name
			trilist.SetString(joinMap.Name.JoinNumber, Name);

			//var twoWayDisplay = this as TwoWayDisplayBase;
			//trilist.SetBool(joinMap.IsTwoWayDisplay.JoinNumber, twoWayDisplay != null);

			CommunicationMonitor?.IsOnlineFeedback.LinkInputSig(trilist.BooleanInput[joinMap.IsOnline.JoinNumber]);

			// power off
			trilist.SetSigTrueAction(joinMap.PowerOff.JoinNumber, PowerOff);
			PowerIsOnFeedback.LinkComplementInputSig(trilist.BooleanInput[joinMap.PowerOff.JoinNumber]);

			// power on 
			trilist.SetSigTrueAction(joinMap.PowerOn.JoinNumber, PowerOn);
			PowerIsOnFeedback.LinkInputSig(trilist.BooleanInput[joinMap.PowerOn.JoinNumber]);

			// input (digital select, digital feedback, names)
			for (var i = 0; i < InputPorts.Count; i++)
			{
				var inputIndex = i;
				var input = InputPorts.ElementAt(inputIndex);

				if (input == null) continue;

				trilist.SetSigTrueAction((ushort)(joinMap.InputSelectOffset.JoinNumber + inputIndex), () =>
				{
					this.LogVerbose("InputSelect Digital-'{0}'", inputIndex + 1);
					SetInput = inputIndex + 1;
				});
				this.LogVerbose("Setting Input Select Action on Digital Join {0} to Input: {1}", joinMap.InputSelectOffset.JoinNumber + inputIndex, this.InputPorts[input.Key.ToString()].Key.ToString());

				trilist.StringInput[(ushort)(joinMap.InputNamesOffset.JoinNumber + inputIndex)].StringValue = string.IsNullOrEmpty(input.Key) ? string.Empty : input.Key;

				InputFeedback[inputIndex].LinkInputSig(trilist.BooleanInput[joinMap.InputSelectOffset.JoinNumber + (uint)inputIndex]);
			}

			// input (analog select)
			trilist.SetUShortSigAction(joinMap.InputSelect.JoinNumber, analogValue =>
			{
				this.LogVerbose("InputSelect Analog-'{0}'", analogValue);
				SetInput = analogValue;
			});

			// input (analog feedback)
			CurrentInputNumberFeedback?.LinkInputSig(trilist.UShortInput[joinMap.InputSelect.JoinNumber]);

			if (CurrentInputFeedback != null)
				CurrentInputFeedback.OutputChange += (sender, args) => this.LogInformation("CurrentInputFeedback: {0}", args.StringValue);

			// bridge online change
			trilist.OnlineStatusChange += (sender, args) =>
			{
				if (!args.DeviceOnLine) return;

				// device name
				trilist.SetString(joinMap.Name.JoinNumber, Name);

				PowerIsOnFeedback.FireUpdate();

				CurrentInputFeedback?.FireUpdate();

				CurrentInputNumberFeedback?.FireUpdate();

				for (var i = 0; i < InputPorts.Count; i++)
				{
					var inputIndex = i;
					InputFeedback?[inputIndex].FireUpdate();
				}
			};
		}


		#endregion



		#region ICommunicationMonitor Members

		/// <summary>
		/// IBasicComminication object
		/// </summary>
		public IBasicCommunication Communication { get; private set; }

		/// <summary>
		/// Port gather object
		/// </summary>
		public CommunicationGather PortGather { get; private set; }

		/// <summary>
		/// Communication status monitor object
		/// </summary>
		public StatusMonitorBase CommunicationMonitor { get; private set; }

		private const string CmdDelimiter = "\x0D";
		private const string GatherDelimiter = "\x0D";

		private readonly GenericQueue _receiveQueue;

		#endregion



		/// <summary>
		/// Initialize (override from PepperDash Essentials)
		/// </summary>
		public override void Initialize()
		{
			Communication.Connect();
			CommunicationMonitor.Start();
		}


		private void CommunicationMonitor_StatusChange(object sender, MonitorStatusChangeEventArgs args)
		{
			CommunicationMonitor.IsOnlineFeedback.FireUpdate();
		}

		private void PortGather_LineReceived(object sender, GenericCommMethodReceiveTextArgs args)
		{
			if (args == null)
			{
				this.LogWarning("PortGather_LineReceived: args are null");
				return;
			}

			if (string.IsNullOrEmpty(args.Text))
			{
				this.LogWarning("PortGather_LineReceived: args.Text is null or empty");
				return;
			}

			try
			{
				this.LogVerbose("PortGather_LineReceived: args.Text-'{0}'", args.Text);
				_receiveQueue.Enqueue(new ProcessStringMessage(args.Text, ProcessResponse));
			}
			catch (Exception ex)
			{
				this.LogError("HandleLineReceived Exception Message: {message}", ex.Message);
				this.LogVerbose(ex, "HandleLineReceived Exception");
			}
		}

		private void ProcessResponse(string response)
		{
			if (string.IsNullOrEmpty(response)) return;

			this.LogDebug("ProcessResponse: {0}", response);

			if (!response.Contains(":") || response.Contains("ERR"))
			{
				this.LogVerbose("ProcessResponse: '{response}' is not tracked", response);
				return;
			}

			var responseData = response.ToLower().Split(':');
			var responseType = string.IsNullOrEmpty(responseData[0]) ? "" : responseData[0];
			var responseValue = string.IsNullOrEmpty(responseData[1]) ? "" : responseData[1];

			this.LogVerbose("ProcessResponse: {responseType}, {responseValue}", responseType, responseValue);

			switch (responseType)
			{
				case "system.state":
					{
						if (responseValue.Equals("powering.on"))
						{
							_isCoolingDown = false;
							_isWarmingUp = true;

							PowerIsOn = true;
						}
						else if (responseValue.Equals("powering.down"))
						{
							_isWarmingUp = false;
							_isCoolingDown = true;

							PowerIsOn = false;
						}
						else if (responseValue.Equals("on"))
						{
							_isCoolingDown = false;
							_isWarmingUp = false;

							PowerIsOn = true;
						}
						else if (responseValue.Equals("standby"))
						{
							_isCoolingDown = false;
							_isWarmingUp = false;

							PowerIsOn = false;
						}
						else if (responseValue.Equals("fault"))
						{
							// TODO [ ] add logic for fault if needed
						}
						break;
					}
				case "display.power":
					{
						PowerIsOn = responseValue.Contains("on");
						break;
					}
				case "source.select":
					{
						UpdateInputFb(responseValue);
						break;
					}
				default:
					{
						this.LogDebug("ProcessResponse: unknown response '{0}'", responseType);
						break;
					}
			}
		}

		/// <summary>
		/// Send text command to device
		/// </summary>
		/// <param name="cmd"></param>
		public void SendText(string cmd)
		{
			if (!Communication.IsConnected)
			{
				this.LogDebug("SendText: device connected: {status}", Communication.IsConnected);
				return;
			}

			if (string.IsNullOrEmpty(cmd)) return;

			Communication.SendText(string.Format("{0}{1}", cmd, CmdDelimiter));
		}

		/// <summary>
		/// Executes a switch, turning on display if necessary.
		/// </summary>
		/// <param name="selector"></param>
		public override void ExecuteSwitch(object selector)
		{
			if (selector is null)
			{
				this.LogDebug("ExecuteSwitch: selector is null (no-op for USB input)");
				return;
			}

			if (PowerIsOn)
			{
				if (selector is Action action)
				{
					action();
				}
			}
			else // if power is off, wait until we get on FB to send it. 
			{
				// One-time local function to wait for power on before executing switch
				void handler(object o, FeedbackEventArgs a)
				{
					if (IsWarmingUp) return;

					IsWarmingUpFeedback.OutputChange -= handler;

					if (selector is Action action)
					{
						action();
					}
				} // necessary to allow reference inside lambda to handler

				IsWarmingUpFeedback.OutputChange += handler; // attach and wait for on FB
				PowerOn();
			}
		}

		#region Inputs

		/// <summary>
		/// Input power on constant
		/// </summary>
		public const int InputPowerOn = 101;

		/// <summary>
		/// Input power off constant
		/// </summary>
		public const int InputPowerOff = 102;

		/// <summary>
		/// Input key list
		/// </summary>
		public static List<string> InputKeys = new List<string>();

		/// <summary>
		/// Input (digital) feedback
		/// </summary>
		public List<BoolFeedback> InputFeedback;

		/// <summary>
		/// Input number (analog) feedback
		/// </summary>
		public IntFeedback CurrentInputNumberFeedback;

		private RoutingInputPort _currentInputPort;

		protected override Func<string> CurrentInputFeedbackFunc
		{
			get { return () => _currentInputPort != null ? _currentInputPort.Key : string.Empty; }
		}

		private List<bool> _inputFeedback;
		private int _currentInputNumber;

		/// <summary>
		/// Input number property
		/// </summary>
		public int CurrentInputNumber
		{
			get { return _currentInputNumber; }
			private set
			{
				if (_currentInputNumber == value) return;

				_currentInputNumber = value;
				CurrentInputNumberFeedback.FireUpdate();
				UpdateBooleanFeedback(value);
			}
		}

		/// <summary>
		/// Sets the requested input
		/// </summary>
		public int SetInput
		{
			set
			{
				if (value <= 0 || value >= InputPorts.Count) return;

				this.LogInformation("SetInput: value-'{0}'", value);

				// -1 to get actual input in list after 0d check
				var port = GetInputPort(value - 1);
				if (port == null)
				{
					this.LogWarning("SetInput: failed to get input port");
					return;
				}

				this.LogDebug("SetInput: port.key-'{0}', port.Selector-'{1}', port.ConnectionType-'{2}', port.FeedbackMatchObject-'{3}'",
					port.Key, port.Selector, port.ConnectionType, port.FeedbackMatchObject);

				ExecuteSwitch(port.Selector);
			}

		}

		private RoutingInputPort GetInputPort(int input)
		{
			return InputPorts.ElementAt(input);
		}

		private void AddRoutingInputPort(RoutingInputPort port, string fbMatch)
		{
			port.FeedbackMatchObject = fbMatch;
			InputPorts.Add(port);
		}

		private void InitializeInputs()
		{
			if (props.SupportsUsb)
			{
				AddRoutingInputPort(new RoutingInputPort("usb", eRoutingSignalType.UsbInput | eRoutingSignalType.UsbOutput, eRoutingPortConnectionType.UsbC, null, this), "usb");
			}

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn1, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi1), this), "hdmi.1");

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn2, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi2), this), "hdmi.2");

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn3, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi3), this), "hdmi.3");

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn4, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi4), this), "hdmi.4");

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.DisplayPortIn1, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.DisplayPort, new Action(InputDisplayPort1), this), "dp");

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.IpcOps, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.None, new Action(InputOps), this), "ops");

			// initialize feedbacks after adding input ports
			_inputFeedback = new List<bool>();
			InputFeedback = new List<BoolFeedback>();

			for (var i = 0; i < InputPorts.Count; i++)
			{
				var input = i + 1;
				InputFeedback.Add(new BoolFeedback($"input.{input}", () => CurrentInputNumber == input));
			}

			CurrentInputNumberFeedback = new IntFeedback("currentInput", () =>
			{
				this.LogDebug("InputNumberFeedback: CurrentInputNumber-'{0}'", CurrentInputNumber);
				return CurrentInputNumber;
			});

			Inputs = new PlanarQeInputs
			{
				Items = new Dictionary<string, ISelectableItem>()
				{
					{"usb", new PlanarQeInput("usb", "USB", () => { }) },
					{"hdmiIn1", new PlanarQeInput("hdmiIn1", "HDMI 1", InputHdmi1) },
					{"hdmiIn2", new PlanarQeInput("hdmiIn2", "HDMI 2", InputHdmi2) },
					{"hdmiIn3", new PlanarQeInput("hdmiIn3", "HDMI 3", InputHdmi3) },
					{"hdmiIn4", new PlanarQeInput("hdmiIn4", "HDMI 4", InputHdmi4) },
					{"displayPortIn1", new PlanarQeInput("displayPortIn1", "DisplayPort 1", InputDisplayPort1) },
					{"ipcOps", new PlanarQeInput("ipcOps", "OPS", InputOps) }
				}
			};
		}

		/// <summary>
		/// Select Hdmi 1 (id-1)
		/// </summary>
		public void InputHdmi1()
		{
			SendText("SOURCE.SELECT=HDMI.1");
		}

		/// <summary>
		/// Select Hdmi 2 (id-2)
		/// </summary>
		public void InputHdmi2()
		{
			SendText("SOURCE.SELECT=HDMI.2");
		}

		/// <summary>
		/// Select Hdmi 3 (id-3)
		/// </summary>
		public void InputHdmi3()
		{
			SendText("SOURCE.SELECT=HDMI.3");
		}


		/// <summary>
		/// Select Hdmi 4 (id-4)
		/// </summary>
		public void InputHdmi4()
		{
			SendText("SOURCE.SELECT=HDMI.4");
		}

		/// <summary>
		/// Select Display Port (id-5)
		/// </summary>
		public void InputDisplayPort1()
		{
			SendText("SOURCE.SELECT=DP");
		}

		/// <summary>
		/// Select OPS (id-0)
		/// </summary>
		public void InputOps()
		{
			SendText("SOURCE.SELECT=OPS");
		}

		/// <summary>
		/// Toggles the display input
		/// </summary>
		public void InputToggle()
		{
			SendText("SOURCE.SELECT+");
		}

		/// <summary>
		/// Poll input
		/// </summary>
		public void InputGet()
		{
			SendText("SOURCE.SELECT?");
		}

		/// <summary>
		/// Process Input Feedback from Response
		/// </summary>
		/// <param name="s">response from device</param>
		public void UpdateInputFb(string s)
		{
			if (string.IsNullOrEmpty(s))
			{
				this.LogWarning("UpdateInputFb: response is null or empty");
				return;
			}

			var newInput = InputPorts.FirstOrDefault(i => i.FeedbackMatchObject != null && i.FeedbackMatchObject.Equals(s.ToLower()));

			if (newInput == null) return;
			if (newInput == CurrentInputPort)
			{
				this.LogDebug("UpdateInputFb: CurrentInputPort-'{0}' == newInput-'{1}'", CurrentInputPort.Key, newInput.Key);
				return;
			}

			CurrentInputPort = newInput;
			CurrentInputFeedback.FireUpdate();

			var key = newInput.Key;

			if (Inputs.Items.TryGetValue(key, out var item))
			{
				Inputs.CurrentItem = key;
				item.IsSelected = true;
			}
			else
			{
				this.LogWarning("key '{0}' not found in Inputs.Items", key);
			}

			switch (key)
			{
				// TODO [ ] verify key names for accuracy
				case "usb":
					CurrentInputNumber = 1;
					break;
				case "hdmiIn1":
					CurrentInputNumber = props.SupportsUsb ? 2 : 1;
					break;
				case "hdmiIn2":
					CurrentInputNumber = props.SupportsUsb ? 3 : 2;
					break;
				case "hdmiIn3":
					CurrentInputNumber = props.SupportsUsb ? 4 : 3;
					break;
				case "hdmiIn4":
					CurrentInputNumber = props.SupportsUsb ? 5 : 4;
					break;
				case "displayPortIn1":
					CurrentInputNumber = props.SupportsUsb ? 6 : 5;
					break;
				case "ipcOps":
					CurrentInputNumber = props.SupportsUsb ? 7 : 6;
					break;
			}
		}

		/// <summary>
		/// Updates Digital Route Feedback for Simpl EISC
		/// </summary>
		/// <param name="data">currently routed source</param>
		private void UpdateBooleanFeedback(int data)
		{
			try
			{
				if (_inputFeedback[data])
				{
					return;
				}

				for (var i = 1; i < InputPorts.Count + 1; i++)
				{
					_inputFeedback[i] = false;
				}

				_inputFeedback[data] = true;
				foreach (var item in InputFeedback)
				{
					var update = item;
					update.FireUpdate();
				}
			}
			catch (Exception ex)
			{
				this.LogError("{0}", ex.Message);
				this.LogVerbose(ex, "UpdateBooleanFeedback Exception");
			}
		}

		#endregion

		#region Power

		private bool _isCoolingDown;
		private bool _isWarmingUp;
		private bool _powerIsOn;


		/// <summary>
		/// Power is on property
		/// </summary>
		public bool PowerIsOn
		{
			get { return _powerIsOn; }
			set
			{
				if (_powerIsOn == value)
				{
					return;
				}

				_powerIsOn = value;

				if (_powerIsOn)
				{
					IsWarmingUp = true;

					WarmupTimer = new CTimer(o =>
					{
						IsWarmingUp = false;
					}, WarmupTime);
				}
				else
				{
					IsCoolingDown = true;

					CooldownTimer = new CTimer(o =>
					{
						IsCoolingDown = false;
					}, CooldownTime);
				}

				PowerIsOnFeedback.FireUpdate();
			}
		}

		/// <summary>
		/// Is warming property
		/// </summary>
		public bool IsWarmingUp
		{
			get { return _isWarmingUp; }
			set
			{
				_isWarmingUp = value;
				IsWarmingUpFeedback.FireUpdate();
			}
		}

		/// <summary>
		/// Is cooling property
		/// </summary>
		public bool IsCoolingDown
		{
			get { return _isCoolingDown; }
			set
			{
				_isCoolingDown = value;
				IsCoolingDownFeedback.FireUpdate();
			}
		}

		protected override Func<bool> PowerIsOnFeedbackFunc
		{
			get { return () => PowerIsOn; }
		}

		protected override Func<bool> IsCoolingDownFeedbackFunc
		{
			get { return () => IsCoolingDown; }
		}

		protected override Func<bool> IsWarmingUpFeedbackFunc
		{
			get { return () => IsWarmingUp; }
		}

		public ISelectableItems<string> Inputs { get; set; }

		/// <summary>
		/// Set Power On For Device
		/// </summary>
		public override void PowerOn()
		{
			if (_isWarmingUp || _isCoolingDown) return;

			// power params: 1 || ON
			SendText("DISPLAY.POWER=ON");
			//PowerIsOn = true;
		}

		/// <summary>
		/// Set Power Off for Device
		/// </summary>
		public override void PowerOff()
		{
			if (_isWarmingUp || _isCoolingDown) return;

			// power params: 0 || OFF
			SendText("DISPLAY.POWER=OFF");
			//PowerIsOn = false;
		}

		/// <summary>
		/// Poll Power
		/// </summary>
		public void PowerGet()
		{
			//SendText("DISPLAY.POWER?");
			SendText("SYSTEM.STATE?");
		}


		/// <summary>
		/// Toggle current power state for device
		/// </summary>
		public override void PowerToggle()
		{
			if (PowerIsOn)
			{
				PowerOff();
			}
			else
			{
				PowerOn();
			}
		}

		#endregion

		/// <summary>
		/// Starts the Poll Ring
		/// </summary>
		public void StatusGet()
		{
			//PowerGet();
			SendText("SYSTEM.STATE?");

			if (!PowerIsOn) return;

			CrestronEnvironment.Sleep(2000);

			InputGet();
		}
	}
}