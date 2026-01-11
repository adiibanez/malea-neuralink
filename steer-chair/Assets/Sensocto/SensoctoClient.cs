using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Sensocto.Core;
using Sensocto.Models;

namespace Sensocto
{
    /// <summary>
    /// Connection state of the Sensocto client.
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting
    }

    /// <summary>
    /// Sensor channel information.
    /// </summary>
    public class SensorChannelInfo
    {
        public string SensorId { get; set; }
        public PhoenixChannel Channel { get; set; }
        public PhoenixPresence Presence { get; set; }
        public BackpressureManager Backpressure { get; set; }
        public SensorJoinParams JoinParams { get; set; }
    }

    /// <summary>
    /// High-level Sensocto client API for connecting to the sensor platform.
    /// Manages WebSocket connection, channels, presence, and backpressure.
    /// </summary>
    public class SensoctoClient : IDisposable
    {
        private PhoenixSocket _socket;
        private readonly SensoctoConfig _config;
        private readonly ConcurrentDictionary<string, SensorChannelInfo> _sensors = new ConcurrentDictionary<string, SensorChannelInfo>();

        /// <summary>
        /// Current connection state.
        /// </summary>
        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        /// <summary>
        /// Event fired when connection state changes.
        /// </summary>
        public event Action<ConnectionState> OnConnectionStateChange;

        /// <summary>
        /// Event fired when backpressure config is received for any sensor.
        /// (sensorId, config)
        /// </summary>
        public event Action<string, BackpressureConfig> OnBackpressureConfig;

        /// <summary>
        /// Event fired when a measurement is received from the server.
        /// (sensorId, measurement)
        /// </summary>
        public event Action<string, Measurement> OnMeasurement;

        /// <summary>
        /// Event fired when presence state changes for any sensor.
        /// (sensorId, presenceState)
        /// </summary>
        public event Action<string, PresenceState> OnPresenceChange;

        /// <summary>
        /// Event fired on connection error.
        /// </summary>
        public event Action<string> OnError;

        public SensoctoClient(SensoctoConfig config)
        {
            _config = config;
        }

        public SensoctoClient(string serverUrl, string bearerToken = null)
        {
            _config = new SensoctoConfig
            {
                ServerUrl = serverUrl,
                BearerToken = bearerToken
            };
        }

        /// <summary>
        /// Connect to the Sensocto server.
        /// </summary>
        public async Task ConnectAsync()
        {
            if (State == ConnectionState.Connected || State == ConnectionState.Connecting)
                return;

            SetState(ConnectionState.Connecting);

            _socket = new PhoenixSocket(_config);
            _socket.OnStateChange += HandleSocketStateChange;
            _socket.OnError += HandleSocketError;

            await _socket.ConnectAsync();
        }

        /// <summary>
        /// Connect to the Sensocto server (non-async).
        /// </summary>
        public void Connect()
        {
            _ = ConnectAsync();
        }

        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (State == ConnectionState.Disconnected)
                return;

            // Leave all channels
            foreach (var info in _sensors.Values)
            {
                try
                {
                    await info.Channel.LeaveAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SensoctoClient] Error leaving channel: {ex.Message}");
                }
            }
            _sensors.Clear();

            await _socket.DisconnectAsync();
            _socket = null;

            SetState(ConnectionState.Disconnected);
        }

        /// <summary>
        /// Disconnect from the server (non-async).
        /// </summary>
        public void Disconnect()
        {
            _ = DisconnectAsync();
        }

        /// <summary>
        /// Join a sensor channel.
        /// </summary>
        public async Task<bool> JoinSensorAsync(SensorJoinParams @params)
        {
            if (State != ConnectionState.Connected)
            {
                Debug.LogWarning("[SensoctoClient] Cannot join sensor: not connected");
                return false;
            }

            var sensorId = @params.SensorId;
            if (_sensors.ContainsKey(sensorId))
            {
                Debug.LogWarning($"[SensoctoClient] Already joined sensor: {sensorId}");
                return true;
            }

            // Ensure bearer token is set
            if (string.IsNullOrEmpty(@params.BearerToken))
            {
                @params.BearerToken = _config.BearerToken;
            }

            var topic = $"sensocto:sensor:{sensorId}";
            var channel = _socket.Channel(topic, @params.ToDictionary());

            // Setup backpressure handling
            var backpressure = new BackpressureManager();
            channel.On("backpressure_config", payload =>
            {
                if (payload is Dictionary<string, object> dict)
                {
                    backpressure.UpdateConfig(dict);
                    var config = BackpressureConfig.FromDictionary(dict);
                    OnBackpressureConfig?.Invoke(sensorId, config);
                }
            });

            // Setup measurement receiving (for bidirectional flow)
            channel.On("measurement", payload =>
            {
                if (payload is Dictionary<string, object> dict)
                {
                    var measurement = Measurement.FromDictionary(dict);
                    OnMeasurement?.Invoke(sensorId, measurement);
                }
            });

            // Setup presence
            var presence = new PhoenixPresence(channel);
            presence.OnSync += state => OnPresenceChange?.Invoke(sensorId, state);

            var info = new SensorChannelInfo
            {
                SensorId = sensorId,
                Channel = channel,
                Presence = presence,
                Backpressure = backpressure,
                JoinParams = @params
            };

            var reply = await channel.JoinAsync();
            if (reply.IsOk)
            {
                _sensors[sensorId] = info;
                Debug.Log($"[SensoctoClient] Joined sensor: {sensorId}");
                return true;
            }
            else
            {
                Debug.LogError($"[SensoctoClient] Failed to join sensor {sensorId}: {reply.Response}");
                return false;
            }
        }

        /// <summary>
        /// Join a sensor channel (non-async).
        /// </summary>
        public void JoinSensor(SensorJoinParams @params, Action<bool> callback = null)
        {
            _ = Task.Run(async () =>
            {
                var result = await JoinSensorAsync(@params);
                callback?.Invoke(result);
            });
        }

        /// <summary>
        /// Leave a sensor channel.
        /// </summary>
        public async Task LeaveSensorAsync(string sensorId)
        {
            if (_sensors.TryRemove(sensorId, out var info))
            {
                info.Presence?.Dispose();
                await info.Channel.LeaveAsync();
                Debug.Log($"[SensoctoClient] Left sensor: {sensorId}");
            }
        }

        /// <summary>
        /// Leave a sensor channel (non-async).
        /// </summary>
        public void LeaveSensor(string sensorId)
        {
            _ = LeaveSensorAsync(sensorId);
        }

        /// <summary>
        /// Send a single measurement to a sensor channel.
        /// </summary>
        public void SendMeasurement(string sensorId, string attributeId, object payload)
        {
            if (!_sensors.TryGetValue(sensorId, out var info))
            {
                Debug.LogWarning($"[SensoctoClient] Cannot send measurement: sensor {sensorId} not joined");
                return;
            }

            var measurement = new Measurement(attributeId, payload);
            info.Channel.Push("measurement", measurement.ToDictionary());
        }

        /// <summary>
        /// Send a single measurement with explicit timestamp.
        /// </summary>
        public void SendMeasurement(string sensorId, string attributeId, object payload, long timestamp)
        {
            if (!_sensors.TryGetValue(sensorId, out var info))
            {
                Debug.LogWarning($"[SensoctoClient] Cannot send measurement: sensor {sensorId} not joined");
                return;
            }

            var measurement = new Measurement(attributeId, payload, timestamp);
            info.Channel.Push("measurement", measurement.ToDictionary());
        }

        /// <summary>
        /// Send a batch of measurements to a sensor channel.
        /// </summary>
        public void SendMeasurementBatch(string sensorId, List<Measurement> measurements)
        {
            if (!_sensors.TryGetValue(sensorId, out var info))
            {
                Debug.LogWarning($"[SensoctoClient] Cannot send batch: sensor {sensorId} not joined");
                return;
            }

            var batch = new List<Dictionary<string, object>>();
            foreach (var m in measurements)
            {
                batch.Add(m.ToDictionary());
            }

            info.Channel.Push("measurements_batch", batch);
        }

        /// <summary>
        /// Add a measurement to the backpressure buffer (will be sent automatically).
        /// </summary>
        public void BufferMeasurement(string sensorId, string attributeId, object payload)
        {
            if (!_sensors.TryGetValue(sensorId, out var info))
            {
                Debug.LogWarning($"[SensoctoClient] Cannot buffer measurement: sensor {sensorId} not joined");
                return;
            }

            info.Backpressure.AddMeasurement(attributeId, payload);
        }

        /// <summary>
        /// Flush buffered measurements if ready according to backpressure config.
        /// Call this from Update() or a coroutine.
        /// </summary>
        public void FlushBufferedMeasurements(string sensorId)
        {
            if (!_sensors.TryGetValue(sensorId, out var info))
                return;

            var batch = info.Backpressure.FlushIfReady();
            if (batch != null && batch.Count > 0)
            {
                SendMeasurementBatch(sensorId, batch);
            }
        }

        /// <summary>
        /// Flush all sensors' buffered measurements.
        /// </summary>
        public void FlushAllBufferedMeasurements()
        {
            foreach (var sensorId in _sensors.Keys)
            {
                FlushBufferedMeasurements(sensorId);
            }
        }

        /// <summary>
        /// Update attribute metadata for a sensor.
        /// </summary>
        public void UpdateAttribute(string sensorId, string action, string attributeId, Dictionary<string, object> metadata)
        {
            if (!_sensors.TryGetValue(sensorId, out var info))
            {
                Debug.LogWarning($"[SensoctoClient] Cannot update attribute: sensor {sensorId} not joined");
                return;
            }

            var update = new AttributeUpdate
            {
                Action = action,
                AttributeId = attributeId,
                Metadata = metadata
            };

            info.Channel.Push("update_attributes", update.ToDictionary());
        }

        /// <summary>
        /// Get the backpressure manager for a sensor.
        /// </summary>
        public BackpressureManager GetBackpressureManager(string sensorId)
        {
            return _sensors.TryGetValue(sensorId, out var info) ? info.Backpressure : null;
        }

        /// <summary>
        /// Get the presence tracker for a sensor.
        /// </summary>
        public PhoenixPresence GetPresence(string sensorId)
        {
            return _sensors.TryGetValue(sensorId, out var info) ? info.Presence : null;
        }

        /// <summary>
        /// Check if a sensor is joined.
        /// </summary>
        public bool IsSensorJoined(string sensorId)
        {
            return _sensors.ContainsKey(sensorId);
        }

        /// <summary>
        /// Get all joined sensor IDs.
        /// </summary>
        public IEnumerable<string> GetJoinedSensorIds()
        {
            return _sensors.Keys;
        }

        /// <summary>
        /// Subscribe to a specific event on a sensor channel.
        /// </summary>
        public void On(string sensorId, string @event, Action<object> callback)
        {
            if (_sensors.TryGetValue(sensorId, out var info))
            {
                info.Channel.On(@event, callback);
            }
        }

        /// <summary>
        /// Unsubscribe from a specific event on a sensor channel.
        /// </summary>
        public void Off(string sensorId, string @event)
        {
            if (_sensors.TryGetValue(sensorId, out var info))
            {
                info.Channel.Off(@event);
            }
        }

        private void HandleSocketStateChange(SocketState state)
        {
            var newState = state switch
            {
                SocketState.Connected => ConnectionState.Connected,
                SocketState.Connecting => ConnectionState.Connecting,
                SocketState.Reconnecting => ConnectionState.Reconnecting,
                _ => ConnectionState.Disconnected
            };

            SetState(newState);
        }

        private void HandleSocketError(string error)
        {
            Debug.LogError($"[SensoctoClient] Socket error: {error}");
            OnError?.Invoke(error);
        }

        private void SetState(ConnectionState state)
        {
            if (State != state)
            {
                State = state;
                OnConnectionStateChange?.Invoke(state);
            }
        }

        public void Dispose()
        {
            _ = DisconnectAsync();
        }
    }
}
