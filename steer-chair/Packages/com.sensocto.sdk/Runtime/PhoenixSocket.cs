using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Sensocto.SDK
{
    /// <summary>
    /// Phoenix WebSocket client for Unity.
    /// Implements the Phoenix socket protocol for real-time communication.
    /// </summary>
    public class PhoenixSocket : IDisposable
    {
        private readonly string _url;
        private readonly int _heartbeatIntervalMs;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _receiveTask;
        private Task _heartbeatTask;

        private readonly Dictionary<string, PhoenixChannel> _channels;
        private readonly Dictionary<string, TaskCompletionSource<PhoenixResponse>> _pendingReplies;
        private readonly object _lock = new object();

        private int _refCounter;
        private bool _disposed;

        /// <summary>
        /// Event fired when the socket opens.
        /// </summary>
        public event Action OnOpen;

        /// <summary>
        /// Event fired when the socket closes.
        /// </summary>
        public event Action<string> OnClose;

        /// <summary>
        /// Event fired when a socket error occurs.
        /// </summary>
        public event Action<string> OnError;

        /// <summary>
        /// Whether the socket is currently connected.
        /// </summary>
        public bool IsConnected => _webSocket?.State == WebSocketState.Open;

        /// <summary>
        /// Creates a new Phoenix socket.
        /// </summary>
        /// <param name="url">WebSocket URL (e.g., wss://example.com/socket/websocket)</param>
        /// <param name="heartbeatIntervalMs">Heartbeat interval in milliseconds.</param>
        public PhoenixSocket(string url, int heartbeatIntervalMs = 30000)
        {
            _url = url;
            _heartbeatIntervalMs = heartbeatIntervalMs;
            _channels = new Dictionary<string, PhoenixChannel>();
            _pendingReplies = new Dictionary<string, TaskCompletionSource<PhoenixResponse>>();
        }

        /// <summary>
        /// Connects to the Phoenix server.
        /// </summary>
        public async Task ConnectAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PhoenixSocket));

            _webSocket = new ClientWebSocket();
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await _webSocket.ConnectAsync(new Uri(_url), _cancellationTokenSource.Token);

                _receiveTask = ReceiveLoopAsync();
                _heartbeatTask = HeartbeatLoopAsync();

                OnOpen?.Invoke();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Disconnects from the Phoenix server.
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_webSocket == null) return;

            _cancellationTokenSource?.Cancel();

            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect",
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhoenixSocket] Error during disconnect: {ex.Message}");
            }

            OnClose?.Invoke("Client initiated disconnect");
        }

        /// <summary>
        /// Creates a new channel for the given topic.
        /// </summary>
        /// <param name="topic">The channel topic.</param>
        /// <param name="params">Join parameters.</param>
        /// <returns>A new PhoenixChannel instance.</returns>
        public PhoenixChannel CreateChannel(string topic, Dictionary<string, object> @params = null)
        {
            var channel = new PhoenixChannel(this, topic, @params ?? new Dictionary<string, object>());

            lock (_lock)
            {
                _channels[topic] = channel;
            }

            return channel;
        }

        /// <summary>
        /// Removes a channel from tracking.
        /// </summary>
        internal void RemoveChannel(string topic)
        {
            lock (_lock)
            {
                _channels.Remove(topic);
            }
        }

        /// <summary>
        /// Sends a message through the socket.
        /// </summary>
        internal async Task<PhoenixResponse> SendAsync(string topic, string @event, object payload, bool expectReply = true)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Socket is not connected");

            var @ref = GenerateRef();

            var message = new Dictionary<string, object>
            {
                ["topic"] = topic,
                ["event"] = @event,
                ["payload"] = payload,
                ["ref"] = @ref
            };

            TaskCompletionSource<PhoenixResponse> tcs = null;

            if (expectReply)
            {
                tcs = new TaskCompletionSource<PhoenixResponse>();
                lock (_lock)
                {
                    _pendingReplies[@ref] = tcs;
                }
            }

            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);

            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
                true, _cancellationTokenSource.Token);

            if (expectReply)
            {
                // Wait with timeout
                var timeoutTask = Task.Delay(10000);
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    lock (_lock)
                    {
                        _pendingReplies.Remove(@ref);
                    }
                    throw new TimeoutException($"Reply timeout for {topic}:{@event}");
                }

                return await tcs.Task;
            }

            return new PhoenixResponse { IsOk = true };
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[8192];
            var messageBuffer = new List<byte>();

            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested && IsConnected)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer),
                        _cancellationTokenSource.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        OnClose?.Invoke(result.CloseStatusDescription ?? "Server closed connection");
                        break;
                    }

                    messageBuffer.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                        messageBuffer.Clear();

                        ProcessMessage(json);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex.Message);
            }
        }

        private async Task HeartbeatLoopAsync()
        {
            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested && IsConnected)
                {
                    await Task.Delay(_heartbeatIntervalMs, _cancellationTokenSource.Token);

                    if (IsConnected)
                    {
                        await SendAsync("phoenix", "heartbeat", new Dictionary<string, object>(), expectReply: false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhoenixSocket] Heartbeat error: {ex.Message}");
            }
        }

        private void ProcessMessage(string json)
        {
            try
            {
                var message = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                var topic = message.GetValueOrDefault("topic")?.ToString();
                var @event = message.GetValueOrDefault("event")?.ToString();
                var payload = message.GetValueOrDefault("payload") as Dictionary<string, object>;
                var @ref = message.GetValueOrDefault("ref")?.ToString();

                // Handle reply
                if (@event == "phx_reply" && @ref != null)
                {
                    TaskCompletionSource<PhoenixResponse> tcs;
                    lock (_lock)
                    {
                        if (_pendingReplies.TryGetValue(@ref, out tcs))
                        {
                            _pendingReplies.Remove(@ref);
                        }
                    }

                    if (tcs != null)
                    {
                        var status = payload?.GetValueOrDefault("status")?.ToString();
                        var response = payload?.GetValueOrDefault("response") as Dictionary<string, object>;

                        tcs.SetResult(new PhoenixResponse
                        {
                            IsOk = status == "ok",
                            Payload = response ?? new Dictionary<string, object>(),
                            ErrorReason = status != "ok" ? response?.GetValueOrDefault("reason")?.ToString() : null
                        });
                    }
                    return;
                }

                // Route to channel
                PhoenixChannel channel;
                lock (_lock)
                {
                    _channels.TryGetValue(topic, out channel);
                }

                channel?.HandleMessage(@event, payload ?? new Dictionary<string, object>());
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhoenixSocket] Error processing message: {ex.Message}");
            }
        }

        private string GenerateRef()
        {
            return Interlocked.Increment(ref _refCounter).ToString();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cancellationTokenSource?.Cancel();
            _webSocket?.Dispose();
            _cancellationTokenSource?.Dispose();

            lock (_lock)
            {
                _channels.Clear();
                _pendingReplies.Clear();
            }
        }
    }

    /// <summary>
    /// Response from a Phoenix channel operation.
    /// </summary>
    public class PhoenixResponse
    {
        public bool IsOk { get; set; }
        public bool IsError => !IsOk;
        public Dictionary<string, object> Payload { get; set; } = new Dictionary<string, object>();
        public string ErrorReason { get; set; }
    }
}
