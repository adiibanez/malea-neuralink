using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Sensocto.SDK
{
    /// <summary>
    /// Main client for connecting to Sensocto API.
    /// Handles authentication, sensor management, and real-time data streaming.
    /// </summary>
    public class SensoctoClient : IDisposable
    {
        private readonly SensoctoConfig _config;
        private PhoenixSocket _socket;
        private PhoenixChannel _sensorChannel;
        private PhoenixChannel _connectorChannel;
        private PhoenixChannel _callChannel;

        private string _connectorId;
        private string _bearerToken;
        private bool _isConnected;
        private bool _disposed;

        // Reconnection state
        private bool _userInitiatedDisconnect;
        private int _reconnectAttempts;
        private CancellationTokenSource _reconnectCts;

        /// <summary>
        /// Event fired when connection state changes.
        /// </summary>
        public event Action<ConnectionState> OnConnectionStateChanged;

        /// <summary>
        /// Event fired when backpressure configuration is received from server.
        /// </summary>
        public event Action<BackpressureConfig> OnBackpressureConfigReceived;

        /// <summary>
        /// Event fired when an error occurs.
        /// </summary>
        public event Action<SensoctoError> OnError;

        /// <summary>
        /// Event fired when reconnection succeeds.
        /// Use this to re-register sensors and rejoin calls.
        /// </summary>
        public event Action OnReconnected;

        /// <summary>
        /// Current connection state.
        /// </summary>
        public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;

        /// <summary>
        /// Whether the client is currently connected.
        /// </summary>
        public bool IsConnected => _isConnected && _socket?.IsConnected == true;

        /// <summary>
        /// Creates a new SensoctoClient with the specified configuration.
        /// </summary>
        /// <param name="config">Client configuration including server URL and credentials.</param>
        public SensoctoClient(SensoctoConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _connectorId = config.ConnectorId ?? Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Connects to the Sensocto server.
        /// </summary>
        /// <param name="bearerToken">Optional bearer token for authentication.</param>
        /// <returns>True if connection was successful.</returns>
        public async Task<bool> ConnectAsync(string bearerToken = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SensoctoClient));

            _userInitiatedDisconnect = false;
            _reconnectAttempts = 0;
            _bearerToken = bearerToken ?? _config.BearerToken;

            try
            {
                SetConnectionState(ConnectionState.Connecting);

                var socketUrl = BuildSocketUrl();
                _socket = new PhoenixSocket(socketUrl, _config.HeartbeatIntervalMs);

                _socket.OnOpen += HandleSocketOpen;
                _socket.OnClose += HandleSocketClose;
                _socket.OnError += HandleSocketError;

                await _socket.ConnectAsync();

                // Join connector channel
                if (_config.AutoJoinConnector)
                {
                    await JoinConnectorChannelAsync();
                }

                _isConnected = true;
                SetConnectionState(ConnectionState.Connected);
                return true;
            }
            catch (Exception ex)
            {
                SetConnectionState(ConnectionState.Error);
                OnError?.Invoke(new SensoctoError(SensoctoErrorCode.ConnectionFailed, ex.Message, ex));
                return false;
            }
        }

        /// <summary>
        /// Disconnects from the Sensocto server.
        /// </summary>
        public async Task DisconnectAsync()
        {
            _userInitiatedDisconnect = true;
            _reconnectCts?.Cancel();

            if (_socket == null) return;

            try
            {
                if (_sensorChannel != null)
                {
                    await _sensorChannel.LeaveAsync();
                    _sensorChannel = null;
                }

                if (_connectorChannel != null)
                {
                    await _connectorChannel.LeaveAsync();
                    _connectorChannel = null;
                }

                if (_callChannel != null)
                {
                    await _callChannel.LeaveAsync();
                    _callChannel = null;
                }

                await _socket.DisconnectAsync();
            }
            finally
            {
                _isConnected = false;
                _reconnectAttempts = 0;
                SetConnectionState(ConnectionState.Disconnected);
            }
        }

        /// <summary>
        /// Attempts to reconnect to the server with exponential backoff.
        /// </summary>
        private async Task AttemptReconnectAsync()
        {
            if (!_config.AutoReconnect || _userInitiatedDisconnect || _disposed)
                return;

            _reconnectCts?.Cancel();
            _reconnectCts = new CancellationTokenSource();
            var token = _reconnectCts.Token;

            SetConnectionState(ConnectionState.Reconnecting);

            while (_reconnectAttempts < _config.MaxReconnectAttempts && !token.IsCancellationRequested)
            {
                _reconnectAttempts++;

                // Exponential backoff: 1s, 2s, 4s, 8s, 16s, max 30s
                var delayMs = Math.Min(1000 * (int)Math.Pow(2, _reconnectAttempts - 1), 30000);

                Debug.Log($"[Sensocto] Reconnect attempt {_reconnectAttempts}/{_config.MaxReconnectAttempts} in {delayMs}ms");

                try
                {
                    await Task.Delay(delayMs, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested)
                    return;

                try
                {
                    // Clean up old socket
                    _socket?.Dispose();
                    _socket = null;

                    // Attempt new connection
                    var socketUrl = BuildSocketUrl();
                    _socket = new PhoenixSocket(socketUrl, _config.HeartbeatIntervalMs);

                    _socket.OnOpen += HandleSocketOpen;
                    _socket.OnClose += HandleSocketClose;
                    _socket.OnError += HandleSocketError;

                    await _socket.ConnectAsync();

                    // Re-join connector channel if configured
                    if (_config.AutoJoinConnector)
                    {
                        await JoinConnectorChannelAsync();
                    }

                    _isConnected = true;
                    _reconnectAttempts = 0;
                    SetConnectionState(ConnectionState.Connected);

                    Debug.Log("[Sensocto] Reconnected successfully");
                    OnReconnected?.Invoke();
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Sensocto] Reconnect attempt {_reconnectAttempts} failed: {ex.Message}");

                    if (_reconnectAttempts >= _config.MaxReconnectAttempts)
                    {
                        Debug.LogError("[Sensocto] Max reconnection attempts reached");
                        SetConnectionState(ConnectionState.Error);
                        OnError?.Invoke(new SensoctoError(
                            SensoctoErrorCode.ConnectionFailed,
                            $"Failed to reconnect after {_config.MaxReconnectAttempts} attempts",
                            ex));
                    }
                }
            }
        }

        /// <summary>
        /// Registers a sensor with the server and starts streaming data.
        /// </summary>
        /// <param name="sensorConfig">Configuration for the sensor to register.</param>
        /// <returns>A SensorStream for sending measurements.</returns>
        public async Task<SensorStream> RegisterSensorAsync(SensorConfig sensorConfig)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to server");

            var sensorId = sensorConfig.SensorId ?? Guid.NewGuid().ToString();
            var channelTopic = $"sensocto:sensor:{sensorId}";

            var joinParams = new Dictionary<string, object>
            {
                ["connector_id"] = _connectorId,
                ["connector_name"] = _config.ConnectorName,
                ["sensor_id"] = sensorId,
                ["sensor_name"] = sensorConfig.SensorName,
                ["sensor_type"] = sensorConfig.SensorType,
                ["attributes"] = sensorConfig.Attributes ?? new List<string>(),
                ["sampling_rate"] = sensorConfig.SamplingRateHz,
                ["batch_size"] = sensorConfig.BatchSize,
                ["bearer_token"] = _bearerToken ?? ""
            };

            var channel = _socket.CreateChannel(channelTopic, joinParams);

            channel.On("backpressure_config", (payload) =>
            {
                var config = BackpressureConfig.FromPayload(payload);
                OnBackpressureConfigReceived?.Invoke(config);
            });

            var response = await channel.JoinAsync();

            if (response.IsError)
            {
                throw new SensoctoException(SensoctoErrorCode.ChannelJoinFailed,
                    $"Failed to join sensor channel: {response.ErrorReason}");
            }

            _sensorChannel = channel;
            return new SensorStream(channel, sensorId, sensorConfig);
        }

        /// <summary>
        /// Subscribes to an existing sensor to receive measurements.
        /// Use this to receive data from external sensors (e.g., head trackers, joysticks).
        /// </summary>
        /// <param name="sensorId">The sensor ID to subscribe to.</param>
        /// <param name="subscriberName">Optional name for this subscriber.</param>
        /// <returns>A SensorSubscription for receiving measurements.</returns>
        public async Task<SensorSubscription> SubscribeToSensorAsync(string sensorId, string subscriberName = null)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to server");

            var channelTopic = $"sensocto:sensor:{sensorId}";

            var joinParams = new Dictionary<string, object>
            {
                ["connector_id"] = _connectorId,
                ["connector_name"] = subscriberName ?? _config.ConnectorName,
                ["sensor_id"] = sensorId,
                ["sensor_type"] = "receiver",
                ["bearer_token"] = _bearerToken ?? ""
            };

            var channel = _socket.CreateChannel(channelTopic, joinParams);
            var response = await channel.JoinAsync();

            if (response.IsError)
            {
                throw new SensoctoException(SensoctoErrorCode.ChannelJoinFailed,
                    $"Failed to subscribe to sensor: {response.ErrorReason}");
            }

            Debug.Log($"[Sensocto] Subscribed to sensor: {sensorId}");
            return new SensorSubscription(channel, sensorId);
        }

        /// <summary>
        /// Joins a video/voice call in a room.
        /// </summary>
        /// <param name="roomId">The room ID to join.</param>
        /// <param name="userId">The user ID.</param>
        /// <param name="userInfo">Optional additional user information.</param>
        /// <returns>A CallSession for managing the call.</returns>
        public async Task<CallSession> JoinCallAsync(string roomId, string userId, Dictionary<string, object> userInfo = null)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to server");

            var channelTopic = $"call:{roomId}";
            var joinParams = new Dictionary<string, object>
            {
                ["user_id"] = userId,
                ["user_info"] = userInfo ?? new Dictionary<string, object>()
            };

            var channel = _socket.CreateChannel(channelTopic, joinParams);
            var response = await channel.JoinAsync();

            if (response.IsError)
            {
                throw new SensoctoException(SensoctoErrorCode.ChannelJoinFailed,
                    $"Failed to join call channel: {response.ErrorReason}");
            }

            _callChannel = channel;

            var iceServers = response.Payload.ContainsKey("ice_servers")
                ? response.Payload["ice_servers"] as List<object>
                : null;

            return new CallSession(channel, roomId, userId, iceServers);
        }

        private async Task JoinConnectorChannelAsync()
        {
            var channelTopic = $"sensocto:connector:{_connectorId}";
            var joinParams = new Dictionary<string, object>
            {
                ["connector_id"] = _connectorId,
                ["connector_name"] = _config.ConnectorName,
                ["connector_type"] = _config.ConnectorType,
                ["features"] = _config.Features ?? new List<string>(),
                ["bearer_token"] = _bearerToken ?? ""
            };

            _connectorChannel = _socket.CreateChannel(channelTopic, joinParams);
            var response = await _connectorChannel.JoinAsync();

            if (response.IsError)
            {
                throw new SensoctoException(SensoctoErrorCode.ChannelJoinFailed,
                    $"Failed to join connector channel: {response.ErrorReason}");
            }
        }

        private string BuildSocketUrl()
        {
            var baseUrl = _config.ServerUrl.TrimEnd('/');
            var protocol = baseUrl.StartsWith("https") ? "wss" : "ws";
            var host = baseUrl.Replace("https://", "").Replace("http://", "");
            return $"{protocol}://{host}/socket/websocket";
        }

        private void SetConnectionState(ConnectionState state)
        {
            if (ConnectionState != state)
            {
                ConnectionState = state;
                OnConnectionStateChanged?.Invoke(state);
            }
        }

        private void HandleSocketOpen()
        {
            Debug.Log("[Sensocto] Socket opened");
        }

        private void HandleSocketClose(string reason)
        {
            Debug.Log($"[Sensocto] Socket closed: {reason}");
            _isConnected = false;

            if (_userInitiatedDisconnect || _disposed)
            {
                SetConnectionState(ConnectionState.Disconnected);
                return;
            }

            // Trigger reconnection for unexpected disconnects
            _ = AttemptReconnectAsync();
        }

        private void HandleSocketError(string error)
        {
            Debug.LogError($"[Sensocto] Socket error: {error}");
            OnError?.Invoke(new SensoctoError(SensoctoErrorCode.SocketError, error));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _socket?.Dispose();
            _sensorChannel = null;
            _connectorChannel = null;
            _callChannel = null;
        }
    }
}
