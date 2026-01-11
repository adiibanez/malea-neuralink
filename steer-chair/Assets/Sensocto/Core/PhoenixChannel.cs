using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Sensocto.Core
{
    /// <summary>
    /// State of a Phoenix channel.
    /// </summary>
    public enum ChannelState
    {
        Closed,
        Joining,
        Joined,
        Leaving,
        Errored
    }

    /// <summary>
    /// Phoenix channel for pub/sub messaging on a specific topic.
    /// </summary>
    public class PhoenixChannel
    {
        private readonly PhoenixSocket _socket;
        private readonly string _topic;
        private readonly Dictionary<string, object> _params;
        private readonly ConcurrentDictionary<string, List<Action<object>>> _eventHandlers = new ConcurrentDictionary<string, List<Action<object>>>();

        private volatile ChannelState _state = ChannelState.Closed;
        private string _joinRef;
        private TaskCompletionSource<PhoenixReply> _joinTcs;
        private int _rejoinAttempts = 0;
        private const int MaxRejoinAttempts = 5;

        public string Topic => _topic;
        public ChannelState State => _state;
        public Dictionary<string, object> Params => _params;

        public event Action<PhoenixReply> OnJoin;
        public event Action<string> OnError;
        public event Action OnClose;

        internal PhoenixChannel(PhoenixSocket socket, string topic, Dictionary<string, object> @params)
        {
            _socket = socket;
            _topic = topic;
            _params = @params;
        }

        /// <summary>
        /// Join the channel with optional timeout.
        /// </summary>
        public async Task<PhoenixReply> JoinAsync(int timeoutMs = 10000)
        {
            if (_state == ChannelState.Joined)
            {
                return new PhoenixReply { Status = "ok", Response = new Dictionary<string, object>() };
            }

            if (_state == ChannelState.Joining)
            {
                // Wait for existing join attempt
                if (_joinTcs != null)
                {
                    return await _joinTcs.Task;
                }
            }

            _state = ChannelState.Joining;
            _joinRef = _socket.MakeRef();
            _joinTcs = new TaskCompletionSource<PhoenixReply>();

            var message = new PhoenixMessage(_topic, PhoenixEvents.Join, _params, _joinRef, _joinRef);

            _socket.RegisterReplyHandler(_joinRef, _joinTcs);
            _socket.Push(message);

            // Timeout handling
            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                var completedTask = await Task.WhenAny(_joinTcs.Task, Task.Delay(timeoutMs, cts.Token));

                if (completedTask == _joinTcs.Task)
                {
                    var reply = await _joinTcs.Task;

                    if (reply.IsOk)
                    {
                        _state = ChannelState.Joined;
                        _rejoinAttempts = 0;
                        Debug.Log($"[PhoenixChannel] Joined {_topic}");
                    }
                    else
                    {
                        _state = ChannelState.Errored;
                        Debug.LogWarning($"[PhoenixChannel] Join error for {_topic}: {reply.Response}");
                    }

                    OnJoin?.Invoke(reply);
                    return reply;
                }
                else
                {
                    // Timeout
                    _socket.RemoveReplyHandler(_joinRef);
                    _state = ChannelState.Errored;
                    var errorReply = new PhoenixReply { Status = "error", Response = "timeout" };
                    OnError?.Invoke("Join timeout");
                    return errorReply;
                }
            }
        }

        /// <summary>
        /// Rejoin the channel (used after reconnection).
        /// </summary>
        internal async Task RejoinAsync()
        {
            if (_rejoinAttempts >= MaxRejoinAttempts)
            {
                _state = ChannelState.Errored;
                OnError?.Invoke("Max rejoin attempts exceeded");
                return;
            }

            _rejoinAttempts++;
            var delay = Math.Min(1000 * (int)Math.Pow(2, _rejoinAttempts - 1), 10000);
            await Task.Delay(delay);

            _state = ChannelState.Closed;
            await JoinAsync();
        }

        /// <summary>
        /// Leave the channel.
        /// </summary>
        public async Task<PhoenixReply> LeaveAsync(int timeoutMs = 10000)
        {
            if (_state == ChannelState.Closed)
            {
                return new PhoenixReply { Status = "ok", Response = new Dictionary<string, object>() };
            }

            _state = ChannelState.Leaving;

            var @ref = _socket.MakeRef();
            var tcs = new TaskCompletionSource<PhoenixReply>();
            var message = new PhoenixMessage(_topic, PhoenixEvents.Leave, new Dictionary<string, object>(), @ref);

            _socket.RegisterReplyHandler(@ref, tcs);
            _socket.Push(message);

            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, cts.Token));

                if (completedTask == tcs.Task)
                {
                    var reply = await tcs.Task;
                    _state = ChannelState.Closed;
                    _socket.RemoveChannel(_topic);
                    OnClose?.Invoke();
                    Debug.Log($"[PhoenixChannel] Left {_topic}");
                    return reply;
                }
                else
                {
                    _socket.RemoveReplyHandler(@ref);
                    _state = ChannelState.Closed;
                    _socket.RemoveChannel(_topic);
                    return new PhoenixReply { Status = "ok", Response = "timeout" };
                }
            }
        }

        /// <summary>
        /// Push a message to the channel and await a reply.
        /// </summary>
        public async Task<PhoenixReply> PushAsync(string @event, object payload, int timeoutMs = 10000)
        {
            if (_state != ChannelState.Joined)
            {
                return new PhoenixReply { Status = "error", Response = "channel not joined" };
            }

            var @ref = _socket.MakeRef();
            var tcs = new TaskCompletionSource<PhoenixReply>();
            var message = new PhoenixMessage(_topic, @event, payload, @ref, _joinRef);

            _socket.RegisterReplyHandler(@ref, tcs);
            _socket.Push(message);

            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, cts.Token));

                if (completedTask == tcs.Task)
                {
                    return await tcs.Task;
                }
                else
                {
                    _socket.RemoveReplyHandler(@ref);
                    return new PhoenixReply { Status = "error", Response = "timeout" };
                }
            }
        }

        /// <summary>
        /// Push a message without waiting for a reply.
        /// </summary>
        public void Push(string @event, object payload)
        {
            if (_state != ChannelState.Joined)
            {
                Debug.LogWarning($"[PhoenixChannel] Cannot push to {_topic}: channel not joined");
                return;
            }

            var @ref = _socket.MakeRef();
            var message = new PhoenixMessage(_topic, @event, payload, @ref, _joinRef);
            _socket.Push(message);
        }

        /// <summary>
        /// Register a callback for a specific event.
        /// </summary>
        public void On(string @event, Action<object> callback)
        {
            if (!_eventHandlers.ContainsKey(@event))
            {
                _eventHandlers[@event] = new List<Action<object>>();
            }
            _eventHandlers[@event].Add(callback);
        }

        /// <summary>
        /// Remove callbacks for a specific event.
        /// </summary>
        public void Off(string @event)
        {
            _eventHandlers.TryRemove(@event, out _);
        }

        /// <summary>
        /// Handle an incoming message from the socket.
        /// </summary>
        internal void HandleMessage(PhoenixMessage message)
        {
            // Handle channel events
            switch (message.Event)
            {
                case PhoenixEvents.Close:
                    _state = ChannelState.Closed;
                    OnClose?.Invoke();
                    break;

                case PhoenixEvents.Error:
                    _state = ChannelState.Errored;
                    OnError?.Invoke(message.Payload?.ToString() ?? "Channel error");
                    // Attempt rejoin
                    _ = RejoinAsync();
                    break;

                case PhoenixEvents.Reply:
                    // Handled by socket
                    break;

                default:
                    // Custom event
                    if (_eventHandlers.TryGetValue(message.Event, out var handlers))
                    {
                        foreach (var handler in handlers)
                        {
                            try
                            {
                                handler(message.Payload);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"[PhoenixChannel] Error in event handler for {message.Event}: {ex}");
                            }
                        }
                    }
                    break;
            }
        }
    }
}
