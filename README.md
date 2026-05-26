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

## Features

### Input State-Aware Switching
This plugin implements intelligent input switching that prevents redundant commands from being sent to the display. 

**How it works:**
- The plugin tracks the current input state using device feedback responses
- Before sending an input selection command, it checks if the display is already on the requested input
- If the input is already selected, the command is skipped (no duplicate `SOURCE.SELECT` sent)
- On initialization and when the device comes online, the current input is queried to populate the state cache

**Benefits:**
- ✅ Eliminates video flicker caused by redundant HDMI handshakes
- ✅ Prevents audio dropout when switching to the same input
- ✅ Reduces unnecessary load on the display processor
- ✅ Improves responsiveness by avoiding queue delays from duplicate commands

This is particularly useful in systems with routing fabric (NVX, codec sharing) that may execute the same route multiple times, and in scenarios where the display is already on the target input from a previous operation.

### Runtime Decision Flow (New)
- On route/set-input request, the plugin stores the requested input as a pending request.
- It sends `SOURCE.SELECT?` to verify the current input before issuing a switch command.
- If current input matches requested input, no switch command is sent.
- If current input differs, the switch command is sent.
- If feedback is unavailable past timeout, the pending switch is executed as a fallback.

This behavior is applied in both switching paths:
- `ExecuteSwitch(...)`
- `SetInput`

### Operational Logs (New)
The plugin now emits reason-coded input decision logs:

- `DISPLAY INPUT UNCHANGED [STATE MATCH]`
- `SENT SWITCH DISPLAY INPUT [STATE CHANGE]`
- `SENT SWITCH DISPLAY INPUT [TIMEOUT FALLBACK]`
- `INPUT UNCHANGED UNKNOWN INPUT [UNKNOWN FEEDBACK]`

Source feedback parsing accepts both response formats:
- `SOURCE.SELECT=<value>`
- `SOURCE.SELECT:<value>`

## Changelog

### v2.3.0 (Unreleased)
- **feat:** Input state-aware switching to prevent redundant input selection commands
- **fix:** Query input state on initialization to ensure feedback is available immediately
- **feat:** Added pending-request decision flow to both `ExecuteSwitch` and `SetInput`
- **feat:** Added reason-coded logging for state match, state change, timeout fallback, and unknown feedback
- **fix:** Accept both `SOURCE.SELECT=<value>` and `SOURCE.SELECT:<value>` feedback delimiters
- Eliminates video flicker and audio dropout from duplicate HDMI commands