using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Sensocto.Models;

namespace Sensocto.Core
{
    /// <summary>
    /// Connection state of the Phoenix socket.
    /// </summary>
    public enum SocketState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Closing
    }

    /// <summary>
    /// Phoenix WebSocket client that manages connection, heartbeat, and message routing.
    /// </summary>
    public class PhoenixSocket : IDisposable
    {
        private readonly string _url;
        private readonly Dictionary<string, string> _params;
        private readonly int _heartbeatIntervalMs;
        private readonly int _reconnectDelayMs;
        private readonly int _maxReconnectDelayMs;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private Thread _receiveThread;
        private Thread _sendThread;

        private readonly ConcurrentQueue<string> _sendQueue = new ConcurrentQueue<string>();
        private readonly ConcurrentDictionary<string, PhoenixChannel> _channels = new ConcurrentDictionary<string, PhoenixChannel>();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<PhoenixReply>> _pendingReplies = new ConcurrentDictionary<string, TaskCompletionSource<PhoenixReply>>();

        private volatile SocketState _state = SocketState.Disconnected;
        private int _refCounter = 0;
        private int _currentReconnectDelay;
        private Timer _heartbeatTimer;
        private string _pendingHeartbeatRef;
        private readonly object _stateLock = new object();

        public SocketState State => _state;
        public event Action<SocketState> OnStateChange;
        public event Action<string> OnError;
        public event Action<PhoenixMessage> OnMessage;

        public PhoenixSocket(SensoctoConfig config)
        {
            _url = BuildUrl(config.ServerUrl, config.Params);
            _params = config.Params;
            _heartbeatIntervalMs = config.HeartbeatIntervalMs;
            _reconnectDelayMs = config.ReconnectDelayMs;
            _maxReconnectDelayMs = config.MaxReconnectDelayMs;
            _currentReconnectDelay = _reconnectDelayMs;
        }

        public PhoenixSocket(string url, Dictionary<string, string> @params = null, int heartbeatIntervalMs = 30000)
        {
            _params = @params ?? new Dictionary<string, string>();
            _url = BuildUrl(url, _params);
            _heartbeatIntervalMs = heartbeatIntervalMs;
            _reconnectDelayMs = 1000;
            _maxReconnectDelayMs = 30000;
            _currentReconnectDelay = _reconnectDelayMs;
        }

        private static string BuildUrl(string baseUrl, Dictionary<string, string> @params)
        {
            var uri = new UriBuilder(baseUrl);

            // Ensure websocket protocol
            if (uri.Scheme == "http") uri.Scheme = "ws";
            else if (uri.Scheme == "https") uri.Scheme = "wss";

            // Add vsn parameter for Phoenix protocol version
            var queryParams = new List<string> { "vsn=2.0.0" };
            if (@params != null)
            {
                foreach (var kvp in @params)
                {
                    queryParams.Add($"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}");
                }
            }

            uri.Path = uri.Path.TrimEnd('/') + "/websocket";
            uri.Query = string.Join("&", queryParams);

            return uri.ToString();
        }

        public string MakeRef()
        {
            return Interlocked.Increment(ref _refCounter).ToString();
        }

        public async Task ConnectAsync()
        {
            if (_state == SocketState.Connected || _state == SocketState.Connecting)
                return;

            SetState(SocketState.Connecting);

            try
            {
                _cts = new CancellationTokenSource();
                _ws = new ClientWebSocket();

                await _ws.ConnectAsync(new Uri(_url), _cts.Token);

                if (_ws.State == WebSocketState.Open)
                {
                    SetState(SocketState.Connected);
                    _currentReconnectDelay = _reconnectDelayMs;

                    StartReceiveLoop();
                    StartSendLoop();
                    StartHeartbeat();

                    // Rejoin channels
                    foreach (var channel in _channels.Values)
                    {
                        if (channel.State == ChannelState.Joined || channel.State == ChannelState.Joining)
                        {
                            _ = channel.RejoinAsync();
                        }
                    }
                }
                else
                {
                    SetState(SocketState.Disconnected);
                    OnError?.Invoke("Failed to connect");
                }
            }
            catch (Exception ex)
            {
                SetState(SocketState.Disconnected);
                OnError?.Invoke($"Connection error: {ex.Message}");
                Debug.LogError($"[PhoenixSocket] Connection error: {ex}");
            }
        }

        public void Connect()
        {
            _ = ConnectAsync();
        }

        public async Task DisconnectAsync()
        {
            if (_state == SocketState.Disconnected || _state == SocketState.Closing)
                return;

            SetState(SocketState.Closing);

            StopHeartbeat();
            _cts?.Cancel();

            try
            {
                if (_ws?.State == WebSocketState.Open)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhoenixSocket] Error during disconnect: {ex.Message}");
            }
            finally
            {
                _ws?.Dispose();
                _ws = null;
                SetState(SocketState.Disconnected);
            }
        }

        public void Disconnect()
        {
            _ = DisconnectAsync();
        }

        public PhoenixChannel Channel(string topic, Dictionary<string, object> @params = null)
        {
            if (_channels.TryGetValue(topic, out var existing))
                return existing;

            var channel = new PhoenixChannel(this, topic, @params ?? new Dictionary<string, object>());
            _channels[topic] = channel;
            return channel;
        }

        public void RemoveChannel(string topic)
        {
            _channels.TryRemove(topic, out _);
        }

        internal void Push(PhoenixMessage message)
        {
            var json = PhoenixSerializer.Encode(message);
            _sendQueue.Enqueue(json);
        }

        internal void RegisterReplyHandler(string @ref, TaskCompletionSource<PhoenixReply> tcs)
        {
            _pendingReplies[@ref] = tcs;
        }

        internal void RemoveReplyHandler(string @ref)
        {
            _pendingReplies.TryRemove(@ref, out _);
        }

        private void SetState(SocketState newState)
        {
            lock (_stateLock)
            {
                if (_state != newState)
                {
                    _state = newState;
                    OnStateChange?.Invoke(newState);
                }
            }
        }

        private void StartReceiveLoop()
        {
            _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            _receiveThread.Start();
        }

        private void ReceiveLoop()
        {
            var buffer = new byte[8192];
            var messageBuffer = new StringBuilder();

            while (!_cts.Token.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                try
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = _ws.ReceiveAsync(segment, _cts.Token).GetAwaiter().GetResult();

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        HandleDisconnect();
                        return;
                    }

                    messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var json = messageBuffer.ToString();
                        messageBuffer.Clear();
                        HandleMessage(json);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PhoenixSocket] Receive error: {ex.Message}");
                    HandleDisconnect();
                    break;
                }
            }
        }

        private void StartSendLoop()
        {
            _sendThread = new Thread(SendLoop) { IsBackground = true };
            _sendThread.Start();
        }

        private void SendLoop()
        {
            while (!_cts.Token.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                try
                {
                    if (_sendQueue.TryDequeue(out var json))
                    {
                        var bytes = Encoding.UTF8.GetBytes(json);
                        var segment = new ArraySegment<byte>(bytes);
                        _ws.SendAsync(segment, WebSocketMessageType.Text, true, _cts.Token).GetAwaiter().GetResult();
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PhoenixSocket] Send error: {ex.Message}");
                }
            }
        }

        private void HandleMessage(string json)
        {
            try
            {
                var message = PhoenixSerializer.Decode(json);
                if (message == null) return;

                // Handle heartbeat response
                if (message.IsReply && message.Ref == _pendingHeartbeatRef)
                {
                    _pendingHeartbeatRef = null;
                    return;
                }

                // Handle pending reply
                if (message.IsReply && !string.IsNullOrEmpty(message.Ref))
                {
                    if (_pendingReplies.TryRemove(message.Ref, out var tcs))
                    {
                        var reply = PhoenixReply.FromPayload(message.Payload);
                        tcs.TrySetResult(reply);
                    }
                }

                // Route to channel
                if (_channels.TryGetValue(message.Topic, out var channel))
                {
                    channel.HandleMessage(message);
                }

                OnMessage?.Invoke(message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhoenixSocket] Error handling message: {ex}");
            }
        }

        private void StartHeartbeat()
        {
            _heartbeatTimer = new Timer(SendHeartbeat, null, _heartbeatIntervalMs, _heartbeatIntervalMs);
        }

        private void StopHeartbeat()
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
        }

        private void SendHeartbeat(object state)
        {
            if (_state != SocketState.Connected)
                return;

            // Check if previous heartbeat was not acknowledged
            if (_pendingHeartbeatRef != null)
            {
                Debug.LogWarning("[PhoenixSocket] Heartbeat timeout, reconnecting...");
                HandleDisconnect();
                return;
            }

            _pendingHeartbeatRef = MakeRef();
            var message = new PhoenixMessage("phoenix", PhoenixEvents.Heartbeat, new Dictionary<string, object>(), _pendingHeartbeatRef);
            Push(message);
        }

        private void HandleDisconnect()
        {
            if (_state == SocketState.Closing || _state == SocketState.Disconnected)
                return;

            StopHeartbeat();
            _cts?.Cancel();
            _ws?.Dispose();
            _ws = null;

            SetState(SocketState.Reconnecting);

            // Schedule reconnection
            _ = Task.Run(async () =>
            {
                await Task.Delay(_currentReconnectDelay);

                // Exponential backoff
                _currentReconnectDelay = Math.Min(_currentReconnectDelay * 2, _maxReconnectDelayMs);

                if (_state == SocketState.Reconnecting)
                {
                    Debug.Log($"[PhoenixSocket] Attempting reconnection...");
                    await ConnectAsync();
                }
            });
        }

        public void Dispose()
        {
            _ = DisconnectAsync();
        }
    }
}
