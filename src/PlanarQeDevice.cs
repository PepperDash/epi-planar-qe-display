using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using PepperDash.Essentials.Core.Queues;
using PepperDash.Essentials.Core.Routing;
using PepperDash.Essentials.Devices.Displays;

namespace PlanarQeDisplay
{
	public class PlanarQeController : TwoWayDisplayBase, ICommunicationMonitor,
		IInputHdmi1, IInputHdmi2, IInputHdmi3, IInputHdmi4, IInputDisplayPort1,
		IBridgeAdvanced
	{
		private bool _isSerialComm;
		
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
			var props = config;
			if (props == null)
			{
				Debug.Console(0, this, Debug.ErrorLogLevel.Error, "{0} configuration must be included", key);
				return;
			}

			ResetDebugLevels();

			Communication = comms;

			_receiveQueue = new GenericQueue(key + "-queue");

			PortGather = new CommunicationGather(Communication, GatherDelimiter);
			PortGather.LineReceived += PortGather_LineReceived;

			var socket = Communication as ISocketStatus;
			_isSerialComm = (socket == null);

			var pollIntervalMs = props.PollIntervalMs > 45000 ? props.PollIntervalMs : 45000;
			CommunicationMonitor = new GenericCommunicationMonitor(this, Communication, pollIntervalMs, 180000, 300000,
				StatusGet);

			CommunicationMonitor.StatusChange += CommunicationMonitor_StatusChange;

			DeviceManager.AddDevice(CommunicationMonitor);

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
			if (bridge != null)
			{
				bridge.AddJoinMap(Key, joinMap);
			}

			var customJoins = JoinMapHelper.TryGetJoinMapAdvancedForDevice(joinMapKey);
			if (customJoins != null)
			{
				joinMap.SetCustomJoinData(customJoins);
			}

			Debug.Console(0, this, "Linking to Trilist '{0}'", trilist.ID.ToString("X"));
			Debug.Console(0, this, "Linking to Bridge Type {0}", GetType().Name);

			// links to bridge
			// device name
			trilist.SetString(joinMap.Name.JoinNumber, Name);

			//var twoWayDisplay = this as TwoWayDisplayBase;
			//trilist.SetBool(joinMap.IsTwoWayDisplay.JoinNumber, twoWayDisplay != null);

			if (CommunicationMonitor != null)
			{
				CommunicationMonitor.IsOnlineFeedback.LinkInputSig(trilist.BooleanInput[joinMap.IsOnline.JoinNumber]);
			}

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
					Debug.Console(DebugVerbose, this, "InputSelect Digital-'{0}'", inputIndex + 1);
					SetInput = inputIndex + 1;
				});
                Debug.Console(2, this, "Setting Input Select Action on Digital Join {0} to Input: {1}", joinMap.InputSelectOffset.JoinNumber + inputIndex, this.InputPorts[input.Key.ToString()].Key.ToString());

				trilist.StringInput[(ushort)(joinMap.InputNamesOffset.JoinNumber + inputIndex)].StringValue = string.IsNullOrEmpty(input.Key) ? string.Empty : input.Key;

				InputFeedback[inputIndex].LinkInputSig(trilist.BooleanInput[joinMap.InputSelectOffset.JoinNumber + (uint)inputIndex]);
			}

			// input (analog select)
			trilist.SetUShortSigAction(joinMap.InputSelect.JoinNumber, analogValue =>
			{
				Debug.Console(DebugNotice, this, "InputSelect Analog-'{0}'", analogValue);
				SetInput = analogValue;
			});

			// input (analog feedback)
			if (CurrentInputNumberFeedback != null)
				CurrentInputNumberFeedback.LinkInputSig(trilist.UShortInput[joinMap.InputSelect.JoinNumber]);

			if(CurrentInputFeedback != null)
				CurrentInputFeedback.OutputChange += (sender, args) => Debug.Console(DebugNotice, this, "CurrentInputFeedback: {0}", args.StringValue);

			// bridge online change
			trilist.OnlineStatusChange += (sender, args) =>
			{
				if (!args.DeviceOnLine) return;

				// device name
				trilist.SetString(joinMap.Name.JoinNumber, Name);
				
				PowerIsOnFeedback.FireUpdate();

				if(CurrentInputFeedback != null)
					CurrentInputFeedback.FireUpdate();
				
				if(CurrentInputNumberFeedback != null)
					CurrentInputNumberFeedback.FireUpdate();

				for (var i = 0; i < InputPorts.Count; i++)
				{
					var inputIndex = i;
					if(InputFeedback != null)
						InputFeedback[inputIndex].FireUpdate();
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
				Debug.Console(DebugNotice, this, "PortGather_LineReceived: args are null");
				return;
			}

			if (string.IsNullOrEmpty(args.Text))
			{
				Debug.Console(DebugNotice, this, "PortGather_LineReceived: args.Text is null or empty");
				return;
			}

			try
			{
				Debug.Console(DebugVerbose, this, "PortGather_LineReceived: args.Text-'{0}'", args.Text);
				_receiveQueue.Enqueue(new ProcessStringMessage(args.Text, ProcessResponse));
			}
			catch (Exception ex)
			{
				Debug.Console(DebugNotice, this, Debug.ErrorLogLevel.Error, "HandleLineReceived Exception Message: {0}", ex.Message);
				Debug.Console(DebugVerbose, this, Debug.ErrorLogLevel.Error, "HandleLineRecieved Exception Stack Trace: {0}", ex.StackTrace);
				if (ex.InnerException != null) Debug.Console(DebugNotice, this, Debug.ErrorLogLevel.Error, "HandleLineReceived Inner Exception: '{0}'", ex.InnerException);
			}
		}

		private void ProcessResponse(string response)
		{
			if (string.IsNullOrEmpty(response)) return;

			Debug.Console(DebugNotice, this, "ProcessResponse: {0}", response);

			if (!response.Contains(":") || response.Contains("ERR"))
			{
				Debug.Console(DebugVerbose, this, "ProcessResponse: '{0}' is not tracked", response);
				return;
			}

			var responseData = response.ToLower().Split(':');
			var responseType = string.IsNullOrEmpty(responseData[0]) ? "" : responseData[0];
			var responseValue = string.IsNullOrEmpty(responseData[1]) ? "" : responseData[1];

			Debug.Console(DebugVerbose, this, "ProcessResponse: responseType-'{0}', responseValue-'{1}'", responseType, responseValue);

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
							Debug.Console(1, this, "ProcessResponse: {0}", responseValue);
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
						Debug.Console(DebugNotice, this, "ProcessRespopnse: unknown response '{0}'", responseType);
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
				Debug.Console(DebugNotice, this, "SendText: device {0} connected", Communication.IsConnected ? "is" : "not");
				Communication.Connect();
				return;
			}

			if (string.IsNullOrEmpty(cmd))
			{
				Debug.Console(DebugNotice, this, "SendText: cmd is null or empty");
				return;
			}

			Debug.Console(DebugNotice, this, "SendText: cmd = {0}", cmd);
			Communication.SendText(string.Format("{0}{1}", cmd, CmdDelimiter));
		}

		/// <summary>
		/// Executes a switch, turning on display if necessary.
		/// </summary>
		/// <param name="selector"></param>
		public override void ExecuteSwitch(object selector)
		{
			if (PowerIsOn)
			{
				var action = selector as Action;
				if (action != null)
				{
					action();
				}
			}
			else // if power is off, wait until we get on FB to send it. 
			{
				// One-time event handler to wait for power on before executing switch
				EventHandler<FeedbackEventArgs> handler = null; // necessary to allow reference inside lambda to handler
				handler = (o, a) =>
				{
					if (IsWarmingUp) return;

					IsWarmingUpFeedback.OutputChange -= handler;

					var action = selector as Action;
					if (action != null)
					{
						action();
					}
				};
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
				if (value <= 0 || value >= InputPorts.Count)
				{
					Debug.Console(DebugNotice, this, "SetInput: value <= 0 || value >= {0}", InputPorts.Count);
					return;
				}

				Debug.Console(DebugNotice, this, "SetInput: value-'{0}'", value);

				// -1 to get actual input in list after 0d check
				var port = GetInputPort(value - 1);
				if (port == null)
				{
					Debug.Console(DebugNotice, this, "SetInput: failed to get input port");
					return;
				}

				Debug.Console(DebugVerbose, this, "SetInput: port.key-'{0}', port.Selector-'{1}', port.ConnectionType-'{2}', port.FeebackMatchObject-'{3}'",
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
				InputFeedback.Add(new BoolFeedback(() => CurrentInputNumber == input));
			}

			CurrentInputNumberFeedback = new IntFeedback(() =>
			{
				Debug.Console(DebugVerbose, this, "InputNumberFeedback: CurrentInputNumber-'{0}'", CurrentInputNumber);
				return CurrentInputNumber;
			});
		}

		/// <summary>
		/// Lists available input routing ports
		/// </summary>
		public void ListRoutingInputPorts()
		{
			foreach (var inputPort in InputPorts)
			{
				Debug.Console(0, this, "ListRoutingInputPorts: key-'{0}', connectionType-'{1}', feedbackMatchObject-'{2}'",
					inputPort.Key, inputPort.ConnectionType, inputPort.FeedbackMatchObject);
			}
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
			var newInput = InputPorts.FirstOrDefault(i => i.FeedbackMatchObject.Equals(s.ToLower()));
			if (newInput == null) return;
			if (newInput == _currentInputPort)
			{
				Debug.Console(DebugNotice, this, "UpdateInputFb: _currentInputPort-'{0}' == newInput-'{1}'", _currentInputPort.Key, newInput.Key);
				return;
			}

			Debug.Console(DebugNotice, this, "UpdateInputFb: newInput key-'{0}', connectionType-'{1}', feedbackMatchObject-'{2}'",
				newInput.Key, newInput.ConnectionType, newInput.FeedbackMatchObject);

			_currentInputPort = newInput;
			CurrentInputFeedback.FireUpdate();

			var key = newInput.Key;
			Debug.Console(DebugNotice, this, "UpdateInputFb: key-'{0}'", key);
			switch (key)
			{
				// TODO [ ] verify key names for accuracy
				case "hdmiIn1":
					CurrentInputNumber = 1;
					break;
				case "hdmiIn2":
					CurrentInputNumber = 2;
					break;
				case "hdmiIn3":
					CurrentInputNumber = 3;
					break;
				case "hdmiIn4":
					CurrentInputNumber = 4;
					break;
				case "displayPortIn1":
					CurrentInputNumber = 5;
					break;
				case "ipcOps":
					CurrentInputNumber = 6;
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
			catch (Exception e)
			{
				Debug.Console(DebugTrace, this, "{0}", e.Message);
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
			// power params: 0 || OFF
			var cmd = "DISPLAY.POWER=OFF";

			if (_isWarmingUp || _isCoolingDown)
			{
				var warmCoolWait = new CTimer(o =>
				{
					// do something when timer expires
					SendText(cmd);
				}, 15000);

				return;
			}
			
			SendText(cmd);
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


		#region DebugLevels

		private uint DebugTrace { get; set; }
		private uint DebugNotice { get; set; }
		private uint DebugVerbose { get; set; }

		/// <summary>
		/// Initializes and resets debug levels to default
		/// </summary>
		public void ResetDebugLevels()
		{
			DebugTrace = 0;
			DebugNotice = 1;
			DebugVerbose = 2;
		}

		/// <summary>
		/// Sets debug levels to value passed in
		/// </summary>
		/// <param name="level"></param>
		public void SetDebugLevels(uint level)
		{
			if (level > 2) return;

			DebugTrace = level;
			DebugNotice = level;
			DebugVerbose = level;
		}

		#endregion

	}
}