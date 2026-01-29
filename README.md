![PepperDash Essentials Plugin Logo](/images/essentials-plugin-blue.png)

# Planar QE Series Display Essentials Plugin (c) 2022

## Overview

This plugin is designed to work with Planar QE Series Displays controlled via RS-232 and  TCP/IP. For config information, see the [config snippets](##Configuration)

Other supported models:
UltraRes L Series

## Configuration

### RS-232

```json
{
  "key": "display-1",
  "uid": 4,
  "type": "planarqe",
  "name": "Display",
  "group": "display",
  "properties": {
    "control": {
      "controlPortDevKey": "processor",
      "controlPortNumber": 1,
      "method": "com",
      "comParams": {
        "protocol": "RS232",
        "baudRate": 19200,
        "hardwareHandshake": "None",
        "softwareHandshake": "None",
        "dataBits": 8,
        "parity": "None",
        "stopBits": 1
      }
    },
    "pollIntervalMs": 60000,
    "coolingTimeMs": 15000,
    "warmingTimeMs": 15000
  }
}
```

### TCP/IP

```json
{
  "key": "display-1",
  "uid": 4,
  "type": "planarqe",
  "name": "Display",
  "group": "display",
  "properties": {
    "control": {
      "method": "tcpIp",
      "tcpSshProperties": {
        "port": 57,
        "address": "0.0.0.0",
        "username": "",
        "password": "",
        "autoReconnect": true,
        "autoReconnectIntervalMs": 5000,
        "bufferSize": 32768
      }
    },
    "pollIntervalMs": 60000,
    "coolingTimeMs": 15000,
    "warmingTimeMs": 15000
  }
}
```

## Bridge JoinMap

### Digitals

| Join Number | JoinSpan | JoinName               | Description              | Type          | Capabilities |
| ----------- | -------- | ---------------------- | ------------------------ | ------------- | ------------ |
| 1           | 1        | PowerOff               | Power Off                | Digital       | FromSIMPL    |
| 2           | 1        | PowerOn                | Power On                 | Digital       | ToFromSIMPL  |
| 3           | 1        | IsTwoWayDisplay        | Is Two Way Display       | Digital       | ToSIMPL      |
| 5           | 1        | VolumeUp               | Volume Up                | Digital       | FromSIMPL    |
| 6           | 1        | VolumeDown             | Volume Down              | Digital       | FromSIMPL    |
| 7           | 1        | VolumeMute             | Volume Mute              | Digital       | ToFromSIMPL  |
| 8           | 1        | VolumeMuteOn           | Volume Mute On           | Digital       | ToFromSIMPL  |
| 9           | 1        | VolumeMuteOff          | Volume Mute Off          | Digital       | ToFromSIMPL  |
| 11          | 10       | InputSelectOffset      | Input Select             | Digital       | ToFromSIMPL  |
| 41          | 10       | ButtonVisibilityOffset | Button Visibility Offset | DigitalSerial | ToFromSIMPL  |
| 50          | 1        | IsOnline               | Is Online                | Digital       | ToSIMPL      |

### Analogs

| Join Number | JoinSpan | JoinName    | Description  | Type   | Capabilities |
| ----------- | -------- | ----------- | ------------ | ------ | ------------ |
| 5           | 1        | VolumeLevel | Volume Level | Analog | ToFromSIMPL  |
| 11          | 1        | InputSelect | Input Select | Analog | ToFromSIMPL  |

### Serials

| Join Number | JoinSpan | JoinName               | Description              | Type          | Capabilities |
| ----------- | -------- | ---------------------- | ------------------------ | ------------- | ------------ |
| 1           | 1        | Name                   | Name                     | Serial        | ToSIMPL      |
| 11          | 10       | InputNamesOffset       | Input Names Offset       | Serial        | ToSIMPL      |
| 41          | 10       | ButtonVisibilityOffset | Button Visibility Offset | DigitalSerial | ToFromSIMPL  |
<!-- START Minimum Essentials Framework Versions -->
### Minimum Essentials Framework Versions

- 2.12.1
<!-- END Minimum Essentials Framework Versions -->
<!-- START Config Example -->
### Config Example

```json
{
    "key": "GeneratedKey",
    "uid": 1,
    "name": "GeneratedName",
    "type": "PlanarQeProperties",
    "group": "Group",
    "properties": {
        "pollIntervalMs": 0,
        "coolingTimeMs": "SampleValue",
        "warmingTimeMs": "SampleValue",
        "supportsUsb": true
    }
}
```
<!-- END Config Example -->
<!-- START Supported Types -->

<!-- END Supported Types -->
<!-- START Join Maps -->

<!-- END Join Maps -->
<!-- START Interfaces Implemented -->
### Interfaces Implemented

- ISelectableItem
- ISelectableItems<string>
- ICommunicationMonitor
- IHasInputs<string>
- IBridgeAdvanced
- IRoutingSinkWithSwitchingWithInputPort
<!-- END Interfaces Implemented -->
<!-- START Base Classes -->
### Base Classes

- TwoWayDisplayBase
- DisplayControllerJoinMap
<!-- END Base Classes -->
<!-- START Public Methods -->
### Public Methods

- public void Select()
- public void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApiAdvanced bridge)
- public void SendText(string cmd)
- public void InputHdmi1()
- public void InputHdmi2()
- public void InputHdmi3()
- public void InputHdmi4()
- public void InputDisplayPort1()
- public void InputOps()
- public void InputToggle()
- public void InputGet()
- public void UpdateInputFb(string s)
- public void PowerGet()
- public void StatusGet()
<!-- END Public Methods -->
<!-- START Bool Feedbacks -->

<!-- END Bool Feedbacks -->
<!-- START Int Feedbacks -->
### Int Feedbacks

- CurrentInputNumberFeedback
<!-- END Int Feedbacks -->
<!-- START String Feedbacks -->

<!-- END String Feedbacks -->
