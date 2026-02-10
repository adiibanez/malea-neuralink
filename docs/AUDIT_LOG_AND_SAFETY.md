# Audit Logger & Safety Hardening

This document describes the persistent audit logging system and safety hardening features for the NeuraSteer wheelchair steering system.

## Overview

The system provides:

1. **Persistent audit trail** of every serial command sent/received, state transitions, relay activations, and macro executions
2. **Command validation** preventing malformed commands from reaching hardware
3. **Safety mechanisms** including stop-on-close, speed limiting, and idle timeouts

---

## Architecture

```
                  Main Thread                    Background Threads
              +------------------+          +-----------------------+
              |   DriveEvents    |          |   SendLoop (serial)   |
              |   MacroController|          |   AuditLog WriterLoop |
              |   RightMenuBtns  |          +-----------+-----------+
              |   DebugPanelCtrl |                      |
              +--------+---------+                      |
                       |                                |
            AuditLog.Log()  (lock-free enqueue)         |
                       |                                |
                       v                                v
              +------------------------------------------+
              |        ConcurrentQueue<string>            |
              +------------------------------------------+
                                    |
                            flush every 50ms
                                    v
              +------------------------------------------+
              |   {persistentDataPath}/AuditLogs/        |
              |   audit_2025-06-15_14-30-00.csv          |
              +------------------------------------------+
```

### Thread Safety

- `AuditLog.Log()` uses `ConcurrentQueue` for lock-free enqueue from any thread
- A dedicated `AuditLogWriter` background thread flushes to disk every 50ms
- Zero impact on serial timing (the SendLoop thread is never blocked by I/O)

---

## AuditLog (Static Class)

**File:** `Assets/Serial/AuditLog.cs`

### Lifecycle

| Method | Called From | When |
|--------|-----------|------|
| `AuditLog.Init()` | `JoystickController.Awake()` | App startup (earliest MonoBehaviour lifecycle) |
| `AuditLog.Shutdown()` | `JoystickController.OnApplicationQuit()` | App exit (flushes remaining entries) |

`Init()` is idempotent -- subsequent calls are no-ops.

### CSV Format

```
Timestamp,Category,Message,Command,Success
2025-06-15T14:30:00.123Z,SerialCommand,Sent,S31D31R8,True
2025-06-15T14:30:00.245Z,Safety,Override rejected: Missing S/D/R markers,badcmd,False
```

| Column | Type | Description |
|--------|------|-------------|
| Timestamp | ISO 8601 UTC | `yyyy-MM-ddTHH:mm:ss.fffZ` |
| Category | Enum | See categories below |
| Message | String | Human-readable description (CSV-escaped) |
| Command | String | The serial command string, if applicable |
| Success | Boolean | `True` or `False` |

### Output Location

```
{Application.persistentDataPath}/AuditLogs/audit_{yyyy-MM-dd_HH-mm-ss}.csv
```

Platform-specific `persistentDataPath`:

| Platform | Path |
|----------|------|
| macOS | `~/Library/Application Support/{company}/{product}/AuditLogs/` |
| Windows | `%APPDATA%/../LocalLow/{company}/{product}/AuditLogs/` |
| Linux | `~/.config/unity3d/{company}/{product}/AuditLogs/` |

Each app session creates a new CSV file.

### Categories

| Category | Usage |
|----------|-------|
| `SerialCommand` | Every `SafeSerialWrite` call -- command string and success/failure |
| `Override` | `SetOverride()` accepted, `ClearOverride()` called |
| `Connection` | Serial port connect, disconnect, reconnect, connection failure |
| `StateChange` | DriveEvents state transitions: Stopped, Ready, Driving |
| `Relay` | Relay ON/OFF from RightMenuButtons and DebugPanelController |
| `Macro` | Macro start, each macro command, macro complete |
| `Application` | Session started, session ended |
| `Safety` | Override rejections (malformed commands), stop-on-close command |

### API

```csharp
// Log from any thread (lock-free, non-blocking)
AuditLog.Log(AuditLog.Category.SerialCommand, "Sent", "S31D31R8", true);
AuditLog.Log(AuditLog.Category.Safety, "Override rejected", "badcmd", false);

// Lifecycle (called automatically by JoystickController)
AuditLog.Init();
AuditLog.Shutdown();
```

---

## CommandValidator (Static Class)

**File:** `Assets/Serial/CommandValidator.cs`

Validates the `S##D##R#` command format before it reaches hardware.

### Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `MinValue` | 0 | Minimum speed/direction value |
| `MaxValue` | 63 | Maximum speed/direction value |
| `Neutral` | 31 | Neutral (stop) position |
| `MinRobot` | 1 | Minimum robot index |
| `MaxRobot` | 8 | Maximum robot index |

### TryParseCommand

```csharp
bool valid = CommandValidator.TryParseCommand(
    "S31D31R8",
    out int speed,      // 31
    out int direction,   // 31
    out int robot,       // 8
    out string error     // null if valid
);
```

Validates:
- Command is not null/empty
- Contains S, D, R markers in order
- Speed and direction are integers in range [0, 63]
- Robot is an integer in range [0, 8] (0-7 for relays, 8 for driving)

### ClampSpeed

```csharp
int clamped = CommandValidator.ClampSpeed(speed: 50, maxDelta: 10);
// Result: 41 (clamped to Neutral + maxDelta = 31 + 10)
```

- `maxDelta = 0` means no clamping (default, preserves existing behavior)
- `maxDelta > 0` limits speed to `[Neutral - maxDelta, Neutral + maxDelta]`
- Example: `maxDelta = 5` restricts speed to range [26, 36]

---

## Safety Fixes (JoystickController.cs)

**File:** `Assets/Serial/JoystickController.cs`

### Fix 1: Send STOP on Close()

**Problem:** `Close()` killed the thread and closed the port. The hardware could hold the last non-neutral command indefinitely.

**Fix:** Sends neutral command `S31D31R8` via `SafeSerialWrite()` before setting `running = false`. Success/failure is audit-logged under the `Safety` category.

```csharp
public void Close()
{
    ClearOverride();

    // Safety: send STOP command before shutting down
    string stopCmd = "S31D31R8";
    bool stopSent = SafeSerialWrite(stopCmd);
    AuditLog.Log(AuditLog.Category.Safety, "Stop command on Close()", stopCmd, stopSent);

    running = false;
    // ... thread join, port close
}
```

### Fix 2: Validate SetOverride Commands

**Problem:** `SetOverride(string command)` sent any string verbatim to hardware with no validation.

**Fix:** Calls `CommandValidator.TryParseCommand()` first. Malformed commands are rejected, logged under the `Safety` category, and the method returns early (safe: hardware continues with the previous command or neutral).

```csharp
public void SetOverride(string command, string source = "")
{
    if (!CommandValidator.TryParseCommand(command, out _, out _, out _, out string error))
    {
        AuditLog.Log(AuditLog.Category.Safety, $"Override rejected: {error}", command, false);
        return;
    }
    // ... proceed with override
}
```

### Fix 3: Log Failed Commands in SafeSerialWrite

**Problem:** `TimeoutException` incremented a counter but did not log which command failed. General exceptions logged the error message but not the command.

**Fix:** Both exception handlers now log the specific command string and error type to the audit log.

### Fix 4: Speed Limiter

**Inspector field:**
```csharp
[Header("Safety")]
[Tooltip("Max speed deviation from neutral (31). 0 = no limit.")]
[SerializeField] private int maxSpeedDelta = 0;
```

- **Default 0** = no limit (preserves current behavior)
- Applied in `SendLoop()` before building the command string, only for non-override commands
- Override commands (relays, macros) bypass the limiter since they use their own validated values

---

## Integration Points

### JoystickController.cs

| Event | Category | Details |
|-------|----------|---------|
| Serial write success | `SerialCommand` | Logs every command sent (including neutral heartbeats) |
| Serial write timeout | `SerialCommand` | Logs command + timeout count, `success=False` |
| Serial write error | `SerialCommand` | Logs command + exception message, `success=False` |
| Connection established | `Connection` | Logs port name |
| Connection failed | `Connection` | Logs exception type and message, `success=False` |
| Disconnection | `Connection` | Logs error cause, `success=False` |
| Serial closed | `Connection` | Normal shutdown |
| Override accepted | `Override` | Logs source and command |
| Override cleared | `Override` | Logs previous source and command |
| Override rejected | `Safety` | Logs validation error, `success=False` |
| Stop on close | `Safety` | Logs stop command and whether it was sent successfully |

### MacroController.cs

| Event | Category | Details |
|-------|----------|---------|
| Macro started | `Macro` | Logs macro label, ID, and full macro string |
| Macro command | `Macro` | Logs each individual `S##D##R#` command in the sequence |
| Macro completed | `Macro` | Logs macro label and ID |

### DriveEvents.cs

| Event | Category | Details |
|-------|----------|---------|
| State -> Ready | `StateChange` | `"Drive state: Ready"` |
| State -> Driving | `StateChange` | `"Drive state: Driving"` |
| State -> Stopped | `StateChange` | `"Drive state: Stopped"` |

### RightMenuButtons.cs

| Event | Category | Details |
|-------|----------|---------|
| Relay ON | `Relay` | Logs relay number, duration, button name, and command |
| Relay OFF | `Relay` | Logs relay number, button name, and command |

Relay mapping (hardware is 0-indexed):

| Button | Relay Number | Command | Duration |
|--------|-------------|---------|----------|
| ModeBtn | 5 | `S31D31R4` | 5.0s |
| ProfileBtn | 5 | `S31D31R4` | 1.0s |
| PowerOnOffBtn | 6 | `S31D31R5` | 6.0s |

### DebugPanelController.cs

| Event | Category | Details |
|-------|----------|---------|
| Debug relay 5 ON | `Relay` | `"Debug relay 5 ON"`, command `S31D31R4` |
| Debug relay 5 OFF | `Relay` | `"Debug relay 5 OFF"`, command `S31D31R4` |
| Debug relay 6 ON | `Relay` | `"Debug relay 6 ON"`, command `S31D31R5` |
| Debug relay 6 OFF | `Relay` | `"Debug relay 6 OFF"`, command `S31D31R5` |

---

## Verification Checklist

After building and launching the app:

| Check | How to Verify |
|-------|---------------|
| Log directory created | Look for `AuditLogs/` in `Application.persistentDataPath` |
| CSV header present | Open the CSV -- first row is `Timestamp,Category,Message,Command,Success` |
| Session started | First data row has `Application` category, `"Session started"` message |
| Heartbeat commands logged | Every `S31D31R8` neutral heartbeat appears as `SerialCommand,Sent` |
| Non-neutral drive logged | Drive the joystick -- verify `S##D##R8` with non-31 values appear |
| Relay activation logged | Press Mode/Profile/Power -- verify `Relay` entries with ON/OFF and correct relay number |
| Debug relay logged | Toggle debug relay 5/6 -- verify entries appear |
| Macro logged | Run a macro -- verify start, individual commands, and complete entries |
| State transitions logged | Go through Stopped -> Ready -> Driving -> Stopped, verify all 3 `StateChange` entries |
| Malformed override rejected | Send a bad override -- verify `Safety` entry with `success=False` |
| Stop on close logged | Quit the app -- verify `Safety` entry with `"Stop command on Close()"` |
| Session ended | Last entry has `Application` category, `"Session ended"` message |
| Speed limiter | Set `maxSpeedDelta > 0` in Inspector, drive, verify speed never exceeds `31 +/- delta` |

---

## Example CSV Output

```csv
Timestamp,Category,Message,Command,Success
2025-06-15T14:30:00.001Z,Application,Session started,,True
2025-06-15T14:30:00.050Z,Connection,Connected on /dev/cu.usbmodem21101,,True
2025-06-15T14:30:00.170Z,SerialCommand,Sent,S31D31R8,True
2025-06-15T14:30:00.290Z,SerialCommand,Sent,S31D31R8,True
2025-06-15T14:30:01.500Z,StateChange,Drive state: Ready,,True
2025-06-15T14:30:03.200Z,StateChange,Drive state: Driving,,True
2025-06-15T14:30:03.320Z,SerialCommand,Sent,S45D28R8,True
2025-06-15T14:30:03.440Z,SerialCommand,Sent,S42D30R8,True
2025-06-15T14:30:04.000Z,StateChange,Drive state: Stopped,,True
2025-06-15T14:30:05.100Z,Relay,Relay 5 ON for 5s (ModeBtn),S31D31R4,True
2025-06-15T14:30:05.220Z,Override,Override set from Relay5:ModeBtn,S31D31R4,True
2025-06-15T14:30:05.220Z,SerialCommand,Sent,S31D31R4,True
2025-06-15T14:30:10.100Z,Override,Override cleared (was Relay5:ModeBtn),S31D31R4,True
2025-06-15T14:30:10.100Z,Relay,Relay 5 OFF (ModeBtn),S31D31R4,True
2025-06-15T14:30:15.000Z,Macro,Macro started: Recline + (recline_increase),S50D31R1,True
2025-06-15T14:30:15.050Z,Macro,Macro command (Recline +),S50D31R1,True
2025-06-15T14:30:15.100Z,Override,Override set from Macro:Recline +,S50D31R1,True
2025-06-15T14:30:15.550Z,Macro,Macro completed: Recline + (recline_increase),,True
2025-06-15T14:30:20.000Z,Safety,"Override rejected: Missing S/D/R markers in 'badcmd'",badcmd,False
2025-06-15T14:30:25.000Z,Safety,Stop command on Close(),S31D31R8,True
2025-06-15T14:30:25.001Z,Connection,Serial connection closed,,True
2025-06-15T14:30:25.002Z,Application,Session ended,,True
```

---

## Configuration

All safety parameters are exposed in the Unity Inspector on the `JoystickController` component:

| Parameter | Default | Description |
|-----------|---------|-------------|
| `maxSpeedDelta` | 0 | Max speed deviation from neutral. 0 = unlimited. |
| `updateInterval` | 0.12s | Minimum time between serial writes |
| `idleTimeout` | 0.3s | Time after last input before resetting to neutral |
