# Sensocto Unity SDK

[![Unity SDK CI](https://github.com/sensocto/sensocto/actions/workflows/unity-sdk.yml/badge.svg)](https://github.com/sensocto/sensocto/actions/workflows/unity-sdk.yml)
[![Unity 2020.3+](https://img.shields.io/badge/Unity-2020.3%2B-blue.svg)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Unity SDK for connecting to the Sensocto sensor platform. Stream real-time sensor data, join video/voice calls, and manage rooms.

## Requirements

- Unity 2020.3 LTS or later
- .NET Standard 2.0 or .NET 4.x

## Installation

### Option 1: Unity Package Manager (Git URL)

1. Open Window > Package Manager
2. Click the + button and select "Add package from git URL"
3. Enter: `https://github.com/sensocto/unity-sdk.git`

### Option 2: Manual Installation

1. Download or clone this repository
2. Copy the `SensoctoSDK` folder into your project's `Packages` directory

## Quick Start

### Basic Connection

```csharp
using Sensocto.SDK;
using UnityEngine;

public class SensorExample : MonoBehaviour
{
    private SensoctoClient _client;
    private SensorStream _sensorStream;

    async void Start()
    {
        // Create configuration
        var config = SensoctoConfig.CreateRuntime("https://your-sensocto-server.com");
        config.ConnectorName = "Unity Game";

        // Create client
        _client = new SensoctoClient(config);

        // Connect
        var connected = await _client.ConnectAsync("your-bearer-token");

        if (connected)
        {
            Debug.Log("Connected to Sensocto!");

            // Register a sensor
            var sensorConfig = new SensorConfig
            {
                SensorName = "Player Sensor",
                SensorType = "unity",
                Attributes = new List<string> { "position", "rotation", "velocity" },
                SamplingRateHz = 30,
                BatchSize = 10
            };

            _sensorStream = await _client.RegisterSensorAsync(sensorConfig);
        }
    }

    void Update()
    {
        if (_sensorStream != null && _sensorStream.IsActive)
        {
            // Send position data
            _sensorStream.AddToBatch("position", new Dictionary<string, object>
            {
                ["x"] = transform.position.x,
                ["y"] = transform.position.y,
                ["z"] = transform.position.z
            });
        }
    }

    async void OnDestroy()
    {
        if (_sensorStream != null)
        {
            await _sensorStream.CloseAsync();
        }

        if (_client != null)
        {
            await _client.DisconnectAsync();
            _client.Dispose();
        }
    }
}
```

### IMU Sensor Data

```csharp
// Send accelerometer data
_sensorStream.AddToBatch("accelerometer", new Dictionary<string, object>
{
    ["x"] = Input.acceleration.x,
    ["y"] = Input.acceleration.y,
    ["z"] = Input.acceleration.z
});

// Send gyroscope data (if available)
if (SystemInfo.supportsGyroscope)
{
    Input.gyro.enabled = true;
    _sensorStream.AddToBatch("gyroscope", new Dictionary<string, object>
    {
        ["x"] = Input.gyro.rotationRate.x,
        ["y"] = Input.gyro.rotationRate.y,
        ["z"] = Input.gyro.rotationRate.z
    });
}
```

### Heart Rate (from external source)

```csharp
// When you receive heart rate data from a Bluetooth device or other source
public void OnHeartRateReceived(int bpm)
{
    _sensorStream.SendMeasurementAsync("heart_rate", new Dictionary<string, object>
    {
        ["bpm"] = bpm
    });
}
```

### Handling Backpressure

The server sends backpressure configuration to help you adjust data transmission rates based on current attention levels:

```csharp
_client.OnBackpressureConfigReceived += (config) =>
{
    Debug.Log($"Attention level: {config.AttentionLevel}");
    Debug.Log($"Recommended batch size: {config.RecommendedBatchSize}");
    Debug.Log($"Recommended batch window: {config.RecommendedBatchWindow}ms");

    // Apply the server's recommendations
    _sensorStream.ApplyBackpressure();
};
```

### Video Calls

```csharp
// Join a call in a room
var callSession = await _client.JoinCallAsync("room-id", "user-id", new Dictionary<string, object>
{
    ["name"] = "Player Name",
    ["avatar"] = "https://example.com/avatar.png"
});

// Actually join the call
var joinResult = await callSession.JoinCallAsync();
Debug.Log($"Joined call with endpoint: {joinResult["endpoint_id"]}");

// Handle participants
callSession.OnParticipantJoined += (participant) =>
{
    Debug.Log($"Participant joined: {participant.UserId}");
};

callSession.OnParticipantLeft += (userId) =>
{
    Debug.Log($"Participant left: {userId}");
};

// Handle media events for WebRTC
callSession.OnMediaEvent += (data) =>
{
    // Forward to your WebRTC implementation
    // (Unity WebRTC package or similar)
};

// Toggle audio/video
await callSession.ToggleAudioAsync(true);
await callSession.ToggleVideoAsync(false);

// Leave call
await callSession.LeaveCallAsync();
```

## Configuration Options

You can create a `SensoctoConfig` ScriptableObject for easy configuration in the Unity Editor:

1. Right-click in Project window
2. Create > Sensocto > Client Configuration
3. Configure settings in the Inspector
4. Reference in your scripts

### Configuration Properties

| Property | Description | Default |
|----------|-------------|---------|
| ServerUrl | Sensocto server URL | http://localhost:4000 |
| ConnectorId | Unique connector identifier | Auto-generated UUID |
| ConnectorName | Human-readable name | "Unity Connector" |
| ConnectorType | Connector type | "unity" |
| BearerToken | Authentication token | (empty) |
| AutoJoinConnector | Auto-join connector channel | true |
| HeartbeatIntervalMs | WebSocket heartbeat interval | 30000 |
| AutoReconnect | Auto-reconnect on disconnect | true |
| MaxReconnectAttempts | Max reconnection attempts | 5 |

## Auto-Reconnection

The SDK automatically handles reconnection when the connection drops unexpectedly. Enable it via configuration:

```csharp
var config = SensoctoConfig.CreateRuntime("https://your-server.com");
config.AutoReconnect = true;        // Enabled by default
config.MaxReconnectAttempts = 5;    // Default: 5 attempts
```

Reconnection uses exponential backoff: 1s, 2s, 4s, 8s, 16s (max 30s between attempts).

### Handling Reconnection

After reconnection, you need to re-register sensors and rejoin calls:

```csharp
private SensorConfig _sensorConfig;
private SensorStream _sensorStream;

async void Start()
{
    _sensorConfig = new SensorConfig
    {
        SensorName = "Player Sensor",
        SensorType = "unity",
        Attributes = new List<string> { "position" }
    };

    _client.OnReconnected += async () =>
    {
        Debug.Log("Reconnected! Re-registering sensor...");

        // Previous SensorStream is invalid after reconnect
        _sensorStream = await _client.RegisterSensorAsync(_sensorConfig);
    };

    _client.OnConnectionStateChanged += (state) =>
    {
        Debug.Log($"Connection state: {state}");

        if (state == ConnectionState.Reconnecting)
        {
            // Optionally pause sensor data collection
        }
    };
}
```

### Connection States

| State | Description |
|-------|-------------|
| Disconnected | Not connected to server |
| Connecting | Initial connection in progress |
| Connected | Successfully connected |
| Reconnecting | Auto-reconnect in progress |
| Error | Connection failed (check OnError for details) |

## Error Handling

```csharp
_client.OnError += (error) =>
{
    Debug.LogError($"Sensocto error [{error.Code}]: {error.Message}");

    switch (error.Code)
    {
        case SensoctoErrorCode.ConnectionFailed:
            // Handle connection failure (including max reconnect attempts reached)
            break;
        case SensoctoErrorCode.AuthenticationFailed:
            // Handle auth failure
            break;
        case SensoctoErrorCode.SocketError:
            // Handle socket error
            break;
    }
};

_client.OnConnectionStateChanged += (state) =>
{
    Debug.Log($"Connection state: {state}");

    if (state == ConnectionState.Disconnected)
    {
        // Handle disconnection
    }
};
```

## Sensor Types

Common sensor types supported:

- `imu` - Accelerometer, gyroscope, magnetometer
- `heartrate` - Heart rate monitors
- `geolocation` - GPS/location data
- `ecg` - ECG waveform data
- `battery` - Battery level
- `generic` - Custom sensor data

## Attribute IDs

Attribute IDs must:
- Start with a letter
- Contain only alphanumeric characters, underscores, or hyphens
- Be maximum 64 characters

Examples: `heart_rate`, `accelerometer`, `gps-location`, `batteryLevel`

## Serial Port Communication

The SDK includes cross-platform serial port support for connecting to hardware devices (Arduino, sensors, etc.).

### Basic Serial Connection

```csharp
using Sensocto.SDK;
using UnityEngine;

public class SerialExample : MonoBehaviour
{
    private SerialConnection _serial;

    void Start()
    {
        // Auto-detect port, 115200 baud, auto-reconnect enabled
        _serial = new SerialConnection(
            port: null,           // Auto-detect
            baudRate: 115200,
            autoReconnect: true,
            maxReconnectAttempts: 10
        );

        // Subscribe to events
        _serial.OnConnected += (port) => Debug.Log($"Connected to {port}");
        _serial.OnDisconnected += (port) => Debug.Log($"Disconnected from {port}");
        _serial.OnReconnecting += (attempt, max) => Debug.Log($"Reconnecting {attempt}/{max}");
        _serial.OnError += (error) => Debug.LogError($"Serial error: {error}");

        // Connect
        _serial.Connect();
    }

    void Update()
    {
        if (_serial.IsConnected)
        {
            // Write data
            _serial.Write("Hello Arduino!\n");

            // Read data
            string line = _serial.ReadLine();
            if (line != null)
            {
                Debug.Log($"Received: {line}");
            }
        }

        // Handle reconnection (if using manual reconnect loop)
        if (_serial.ShouldReconnect())
        {
            _serial.TryReconnect();
        }
    }

    void OnDestroy()
    {
        _serial?.Dispose();
    }
}
```

### Using SerialConnectionMonitor (Recommended)

The `SerialConnectionMonitor` component handles Unity lifecycle and reconnection automatically:

```csharp
public class SerialMonitorExample : MonoBehaviour
{
    [SerializeField] private SerialConnectionMonitor serialMonitor;

    void Start()
    {
        // Events are already handled by the monitor
        serialMonitor.OnConnected += (port) => Debug.Log($"Connected: {port}");
        serialMonitor.OnStateChanged += (state) => Debug.Log($"State: {state}");
    }

    void Update()
    {
        if (serialMonitor.IsConnected)
        {
            serialMonitor.Write("DATA\n");
        }
    }
}
```

### Serial Connection Indicator (UI)

Add a visual indicator to show connection status:

1. Create a UI Image for the status dot
2. Add the `SerialConnectionIndicator` component
3. Assign the `SerialConnectionMonitor` reference
4. Optionally add Text components for status and port name

```csharp
// Or set up programmatically
var indicator = gameObject.AddComponent<SerialConnectionIndicator>();
indicator.SetConnectionMonitor(mySerialMonitor);
```

### Serial Configuration

| Property | Description | Default |
|----------|-------------|---------|
| Port | Serial port name (e.g., "COM3", "/dev/cu.usbmodem123") | Auto-detect |
| BaudRate | Communication speed | 115200 |
| AutoReconnect | Auto-reconnect on disconnect | true |
| MaxReconnectAttempts | Max reconnection attempts | 10 |

### Platform Notes

**Windows**: Uses `System.IO.Ports.SerialPort` in Editor, auto-detects COM ports.

**macOS**: Uses native POSIX implementation in builds (IL2CPP compatible). May require disabling App Sandbox:
```bash
codesign --remove-signature "YourApp.app"
```

**Linux**: Supports `/dev/ttyUSB*` and `/dev/ttyACM*` devices.

### Manual Port Configuration

Create `StreamingAssets/serial_port.txt` with the port path:
```
/dev/cu.usbmodem21101
```

### Serial Connection States

| State | Description |
|-------|-------------|
| Disconnected | Not connected |
| Connecting | Initial connection in progress |
| Connected | Successfully connected |
| Reconnecting | Auto-reconnect in progress (exponential backoff) |
| Error | Max reconnect attempts reached |

Reconnection uses exponential backoff: 100ms, 200ms, 400ms, 800ms... (max 5s).

## Thread Safety

The SDK is designed to be called from the Unity main thread. If you need to receive data from background threads (e.g., Bluetooth callbacks), use Unity's main thread dispatcher:

```csharp
// From a background thread
UnityMainThreadDispatcher.Instance.Enqueue(() =>
{
    _sensorStream.AddToBatch("heart_rate", new { bpm = heartRate });
});
```

## License

MIT License - see LICENSE file for details.

## Support

- Documentation: https://docs.sensocto.com
- Issues: https://github.com/sensocto/unity-sdk/issues
- Email: support@sensocto.com
