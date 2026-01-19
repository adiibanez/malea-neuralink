using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sensocto.SDK
{
    /// <summary>
    /// Represents a Phoenix channel for topic-based communication.
    /// </summary>
    public class PhoenixChannel
    {
        private readonly PhoenixSocket _socket;
        private readonly string _topic;
        private readonly Dictionary<string, object> _joinParams;
        private readonly Dictionary<string, List<Action<Dictionary<string, object>>>> _eventHandlers;
        private readonly object _lock = new object();

        private ChannelState _state = ChannelState.Closed;

        /// <summary>
        /// The channel topic.
        /// </summary>
        public string Topic => _topic;

        /// <summary>
        /// Current state of the channel.
        /// </summary>
        public ChannelState State => _state;

        /// <summary>
        /// Whether the channel is currently joined.
        /// </summary>
        public bool IsJoined => _state == ChannelState.Joined;

        internal PhoenixChannel(PhoenixSocket socket, string topic, Dictionary<string, object> joinParams)
        {
            _socket = socket;
            _topic = topic;
            _joinParams = joinParams;
            _eventHandlers = new Dictionary<string, List<Action<Dictionary<string, object>>>>();
        }

        /// <summary>
        /// Joins the channel.
        /// </summary>
        /// <returns>The join response from the server.</returns>
        public async Task<PhoenixResponse> JoinAsync()
        {
            if (_state == ChannelState.Joined)
            {
                return new PhoenixResponse { IsOk = true };
            }

            _state = ChannelState.Joining;

            try
            {
                var response = await _socket.SendAsync(_topic, "phx_join", _joinParams);

                if (response.IsOk)
                {
                    _state = ChannelState.Joined;
                }
                else
                {
                    _state = ChannelState.Errored;
                }

                return response;
            }
            catch (Exception)
            {
                _state = ChannelState.Errored;
                throw;
            }
        }

        /// <summary>
        /// Leaves the channel.
        /// </summary>
        public async Task LeaveAsync()
        {
            if (_state != ChannelState.Joined)
            {
                return;
            }

            _state = ChannelState.Leaving;

            try
            {
                await _socket.SendAsync(_topic, "phx_leave", new Dictionary<string, object>());
            }
            finally
            {
                _state = ChannelState.Closed;
                _socket.RemoveChannel(_topic);
            }
        }

        /// <summary>
        /// Pushes a message to the channel.
        /// </summary>
        /// <param name="event">The event name.</param>
        /// <param name="payload">The message payload.</param>
        /// <returns>The response from the server.</returns>
        public async Task<PhoenixResponse> PushAsync(string @event, Dictionary<string, object> payload)
        {
            if (_state != ChannelState.Joined)
            {
                throw new InvalidOperationException("Channel is not joined");
            }

            return await _socket.SendAsync(_topic, @event, payload);
        }

        /// <summary>
        /// Pushes a message to the channel (list payload for batch operations).
        /// </summary>
        /// <param name="event">The event name.</param>
        /// <param name="payload">The message payload as a list.</param>
        /// <returns>The response from the server.</returns>
        public async Task<PhoenixResponse> PushAsync(string @event, List<Dictionary<string, object>> payload)
        {
            if (_state != ChannelState.Joined)
            {
                throw new InvalidOperationException("Channel is not joined");
            }

            return await _socket.SendAsync(_topic, @event, payload);
        }

        /// <summary>
        /// Registers an event handler for the specified event.
        /// </summary>
        /// <param name="event">The event name to listen for.</param>
        /// <param name="handler">The handler to invoke when the event is received.</param>
        public void On(string @event, Action<Dictionary<string, object>> handler)
        {
            lock (_lock)
            {
                if (!_eventHandlers.TryGetValue(@event, out var handlers))
                {
                    handlers = new List<Action<Dictionary<string, object>>>();
                    _eventHandlers[@event] = handlers;
                }

                handlers.Add(handler);
            }
        }

        /// <summary>
        /// Removes an event handler for the specified event.
        /// </summary>
        /// <param name="event">The event name.</param>
        /// <param name="handler">The handler to remove.</param>
        public void Off(string @event, Action<Dictionary<string, object>> handler)
        {
            lock (_lock)
            {
                if (_eventHandlers.TryGetValue(@event, out var handlers))
                {
                    handlers.Remove(handler);
                }
            }
        }

        /// <summary>
        /// Removes all event handlers for the specified event.
        /// </summary>
        /// <param name="event">The event name.</param>
        public void Off(string @event)
        {
            lock (_lock)
            {
                _eventHandlers.Remove(@event);
            }
        }

        /// <summary>
        /// Handles an incoming message from the socket.
        /// </summary>
        internal void HandleMessage(string @event, Dictionary<string, object> payload)
        {
            List<Action<Dictionary<string, object>>> handlers;

            lock (_lock)
            {
                if (!_eventHandlers.TryGetValue(@event, out handlers))
                {
                    return;
                }

                // Copy to avoid issues if handlers modify the list
                handlers = new List<Action<Dictionary<string, object>>>(handlers);
            }

            foreach (var handler in handlers)
            {
                try
                {
                    handler(payload);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[PhoenixChannel] Error in event handler for {_topic}:{@event}: {ex.Message}");
                }
            }
        }
    }

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
}
