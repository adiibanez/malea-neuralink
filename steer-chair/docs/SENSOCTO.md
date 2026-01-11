# Sensocto Unity Integration

Full Phoenix Channels implementation in C# for Unity, enabling real-time bidirectional sensor communication with the Sensocto backend.

## Overview

This integration allows the wheelchair control system to:
- **Receive** sensor data from external devices (head trackers, wearables) via Sensocto
- **Send** wheelchair telemetry (position, velocity, state) to Sensocto for monitoring

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Unity Application                             │
├─────────────────────────────────────────────────────────────────────┤
│  SensoctoSensorProvider (MonoBehaviour)                             │
│    - Implements IMoveReceiver                                        │
│    - Bridges sensor data to existing DriveEvents system              │
├─────────────────────────────────────────────────────────────────────┤
│  SensoctoClient                                                      │
│    - High-level API (Connect, SendMeasurement, JoinSensor)          │
│    - Manages multiple sensor channels                                │
├─────────────────────────────────────────────────────────────────────┤
│  BackpressureManager               │  PhoenixPresence               │
│    - Adaptive batching             │    - Track connected sensors   │
│    - Respects attention levels     │    - Sync/diff state           │
├────────────────────────────────────┴────────────────────────────────┤
│  PhoenixChannel                                                      │
│    - Join/Leave topics                                               │
│    - Push/Receive messages                                           │
│    - Reply handling with ref tracking                                │
├─────────────────────────────────────────────────────────────────────┤
│  PhoenixSocket                                                       │
│    - WebSocket connection                                            │
│    - Heartbeat (30s)                                                 │
│    - Reconnection with exponential backoff                           │
└─────────────────────────────────────────────────────────────────────┘
```

## File Structure

```
Assets/Sensocto/
├── Core/
│   ├── PhoenixSocket.cs          # WebSocket transport layer
│   ├── PhoenixChannel.cs         # Channel join/push/receive
│   ├── PhoenixPresence.cs        # Presence sync and tracking
│   └── PhoenixSerializer.cs      # JSON message serialization
├── Models/
│   ├── SensoctoConfig.cs         # Connection configuration
│   ├── Measurement.cs            # Sensor measurement data
│   ├── BackpressureConfig.cs     # Backpressure settings
│   └── PresenceState.cs          # Presence tracking state
├── SensoctoClient.cs             # High-level client API
├── BackpressureManager.cs        # Adaptive batching logic
├── SensoctoSensorProvider.cs     # MonoBehaviour integration
└── Sensocto.asmdef               # Assembly definition
```

## Quick Start

### 1. Add SensoctoSensorProvider to Your Scene

Add the `SensoctoSensorProvider` component to a GameObject (typically the same one with `DriveEvents`).

### 2. Configure in Inspector

| Field | Description | Example |
|-------|-------------|---------|
| Server URL | Sensocto WebSocket endpoint | `ws://localhost:4000/socket` |
| Bearer Token | Authentication token | `your-token` |
| Connect On Start | Auto-connect when scene loads | `true` |
| **Inbound Settings** | | |
| Enable Inbound | Receive external sensor data | `true` |
| Inbound Sensor ID | Sensor to subscribe to | `head-tracker-001` |
| Direction Attribute | Attribute for X movement | `direction` |
| Speed Attribute | Attribute for Y movement | `speed` |
| **Outbound Settings** | | |
| Enable Outbound | Send wheelchair telemetry | `true` |
| Outbound Sensor ID | Sensor ID for telemetry | `wheelchair-001` |

### 3. Configure DriveEvents Input Source

In the `DriveEvents` component:
1. Assign the `SensoctoSensorProvider` reference
2. Set `Input Source` to:
   - `MouseJoystick` - UI joystick only
   - `Sensocto` - External sensors only
   - `Both` - Accept input from either

### 4. Start Sensocto Server

```bash
cd /path/to/sensocto
mix phx.server
```

## API Reference

### SensoctoSensorProvider

```csharp
// Properties
bool IsConnected { get; }
Vector2 CurrentMovement { get; }
ConnectionState ConnectionState { get; }

// Methods
void Connect();
void Disconnect();
void SendTelemetry(string attributeId, object value);
void SendPosition(Vector3 position);
void SendState(string state);

// Events
MovementEvent OnMovementReceived;      // Vector2 direction
MeasurementEvent OnMeasurementReceived; // (attributeId, payload)
UnityEvent OnConnected;
UnityEvent OnDisconnected;
```

### SensoctoClient (Low-Level)

```csharp
// Connection
await client.ConnectAsync();
await client.DisconnectAsync();

// Sensors
await client.JoinSensorAsync(sensorParams);
await client.LeaveSensorAsync(sensorId);

// Data
client.SendMeasurement(sensorId, attributeId, payload);
client.SendMeasurementBatch(sensorId, measurements);
client.BufferMeasurement(sensorId, attributeId, payload); // Uses backpressure
client.FlushBufferedMeasurements(sensorId);

// Events
client.OnBackpressureConfig += (sensorId, config) => { };
client.OnMeasurement += (sensorId, measurement) => { };
client.OnPresenceChange += (sensorId, state) => { };
```

## Backpressure System

The server sends `backpressure_config` messages to adjust data transmission rates based on user attention:

| Attention Level | Batch Window | Batch Size | Use Case |
|-----------------|--------------|------------|----------|
| `High` | 100ms | 1 | User actively focused |
| `Medium` | 500ms | 5 | User viewing data |
| `Low` | 2000ms | 10 | Sensor connected, not viewed |
| `None` | 5000ms | 20 | No active viewers |

The `BackpressureManager` automatically buffers measurements and flushes them according to server recommendations.

## Message Formats

### Measurement (Single)
```json
{
  "payload": {"x": 0.5, "y": -0.3},
  "timestamp": 1703856000000,
  "attribute_id": "direction"
}
```

### Measurement Batch
```json
[
  {"payload": 0.5, "timestamp": 1703856000000, "attribute_id": "speed"},
  {"payload": 0.6, "timestamp": 1703856000100, "attribute_id": "speed"}
]
```

### Backpressure Config (from server)
```json
{
  "attention_level": "medium",
  "recommended_batch_window": 500,
  "recommended_batch_size": 5,
  "timestamp": 1703856000000
}
```

## Presence Tracking

Track which sensors are online:

```csharp
var presence = client.GetPresence(sensorId);

// Check if sensor is online
bool isOnline = presence.IsOnline("other-sensor-id");

// Get all online sensors
var onlineSensors = presence.GetOnlineKeys();

// Listen for changes
presence.OnJoin += (key, current, joined) => Debug.Log($"{key} joined");
presence.OnLeave += (key, current, left) => Debug.Log($"{key} left");
```

## Integration with Existing Code

The integration hooks into the existing wheelchair control system:

1. **DriveEvents** now supports multiple input sources
2. **SensoctoSensorProvider** implements `IMoveReceiver` for outbound telemetry
3. Inbound sensor data is converted to `Vector2` movement and broadcast via `OnMovementReceived`

### Data Flow

**Inbound (External Sensors → Unity):**
```
External Device → Sensocto Server → WebSocket → SensoctoClient
  → SensoctoSensorProvider.OnMovementReceived → DriveEvents.BroadcastMovement
  → IMoveReceiver.Move (JoystickController, SimulatorMoveDemo, etc.)
```

**Outbound (Unity → Sensocto):**
```
DriveEvents.OnJoystickMoved → IMoveReceiver.Move
  → SensoctoSensorProvider.Move → BackpressureManager.Buffer
  → SensoctoClient.SendBatch → WebSocket → Sensocto Server
```

## Dependencies

No external packages required. Uses:
- `System.Net.WebSockets.ClientWebSocket` (.NET Standard 2.1)
- `System.Threading` for async operations
- Built-in JSON parser (no external JSON library needed)

## Troubleshooting

### Connection Issues
- Verify sensocto server is running: `mix phx.server`
- Check WebSocket URL includes `/socket` path
- Ensure bearer token is valid

### No Data Received
- Verify sensor IDs match between Unity and sensocto
- Check attribute names (`direction`, `speed`) match sensor configuration
- Enable debug logging in SensoctoSensorProvider

### High Latency
- Check backpressure settings - high attention level = lower latency
- Verify network connection to sensocto server
- Consider running sensocto locally for testing

## Testing

1. **Start sensocto:**
   ```bash
   cd /Users/adrianibanez/Documents/projects/2024_sensor-platform/sensocto
   mix phx.server
   ```

2. **Open Unity project and enter Play mode**

3. **Check Console for:**
   - `[SensoctoSensorProvider] Connecting to ws://...`
   - `[SensoctoSensorProvider] Joined inbound sensor: ...`
   - `[SensoctoSensorProvider] Connection state: Connected`

4. **Send test data from sensocto dashboard or simulator**

5. **Verify movement in Unity scene**
