using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Sensocto.SDK
{
    /// <summary>
    /// Represents an active video/voice call session.
    /// </summary>
    public class CallSession : IDisposable
    {
        private readonly PhoenixChannel _channel;
        private readonly string _roomId;
        private readonly string _userId;
        private readonly List<object> _iceServers;

        private bool _disposed;
        private bool _inCall;
        private string _endpointId;

        /// <summary>
        /// Event fired when a participant joins the call.
        /// </summary>
        public event Action<CallParticipant> OnParticipantJoined;

        /// <summary>
        /// Event fired when a participant leaves the call.
        /// </summary>
        public event Action<string> OnParticipantLeft;

        /// <summary>
        /// Event fired when a media event is received from the server.
        /// Used for WebRTC signaling (SDP offers/answers, ICE candidates).
        /// </summary>
        public event Action<object> OnMediaEvent;

        /// <summary>
        /// Event fired when a participant's audio state changes.
        /// </summary>
        public event Action<string, bool> OnParticipantAudioChanged;

        /// <summary>
        /// Event fired when a participant's video state changes.
        /// </summary>
        public event Action<string, bool> OnParticipantVideoChanged;

        /// <summary>
        /// Event fired when the call quality changes.
        /// </summary>
        public event Action<string> OnQualityChanged;

        /// <summary>
        /// Event fired when the call ends.
        /// </summary>
        public event Action OnCallEnded;

        /// <summary>
        /// The room ID for this call.
        /// </summary>
        public string RoomId => _roomId;

        /// <summary>
        /// The user ID in this call.
        /// </summary>
        public string UserId => _userId;

        /// <summary>
        /// Whether the user is currently in the call.
        /// </summary>
        public bool InCall => _inCall;

        /// <summary>
        /// The endpoint ID assigned by the server.
        /// </summary>
        public string EndpointId => _endpointId;

        /// <summary>
        /// ICE servers for WebRTC connection.
        /// </summary>
        public IReadOnlyList<object> IceServers => _iceServers?.AsReadOnly();

        internal CallSession(PhoenixChannel channel, string roomId, string userId, List<object> iceServers)
        {
            _channel = channel;
            _roomId = roomId;
            _userId = userId;
            _iceServers = iceServers ?? new List<object>();

            SetupEventHandlers();
        }

        /// <summary>
        /// Joins the actual call (after joining the channel).
        /// </summary>
        /// <returns>Dictionary with endpoint_id and participants.</returns>
        public async Task<Dictionary<string, object>> JoinCallAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CallSession));

            var response = await _channel.PushAsync("join_call", new Dictionary<string, object>());

            if (response.IsOk)
            {
                _inCall = true;
                if (response.Payload.TryGetValue("endpoint_id", out var endpointId))
                {
                    _endpointId = endpointId?.ToString();
                }
                return response.Payload;
            }

            throw new SensoctoException(SensoctoErrorCode.ServerError,
                $"Failed to join call: {response.ErrorReason}");
        }

        /// <summary>
        /// Leaves the call.
        /// </summary>
        public async Task LeaveCallAsync()
        {
            if (!_inCall) return;

            await _channel.PushAsync("leave_call", new Dictionary<string, object>());
            _inCall = false;
            _endpointId = null;
        }

        /// <summary>
        /// Sends a media event (SDP offer/answer, ICE candidate) to the server.
        /// </summary>
        /// <param name="data">The media event data.</param>
        public async Task SendMediaEventAsync(object data)
        {
            if (!_inCall)
                throw new InvalidOperationException("Not in call");

            await _channel.PushAsync("media_event", new Dictionary<string, object>
            {
                ["data"] = data
            });
        }

        /// <summary>
        /// Toggles the local audio state.
        /// </summary>
        /// <param name="enabled">Whether audio should be enabled.</param>
        public async Task ToggleAudioAsync(bool enabled)
        {
            if (!_inCall)
                throw new InvalidOperationException("Not in call");

            await _channel.PushAsync("toggle_audio", new Dictionary<string, object>
            {
                ["enabled"] = enabled
            });
        }

        /// <summary>
        /// Toggles the local video state.
        /// </summary>
        /// <param name="enabled">Whether video should be enabled.</param>
        public async Task ToggleVideoAsync(bool enabled)
        {
            if (!_inCall)
                throw new InvalidOperationException("Not in call");

            await _channel.PushAsync("toggle_video", new Dictionary<string, object>
            {
                ["enabled"] = enabled
            });
        }

        /// <summary>
        /// Sets the video quality for the call.
        /// </summary>
        /// <param name="quality">Quality level: "high", "medium", "low", or "auto".</param>
        public async Task SetQualityAsync(string quality)
        {
            if (!_inCall)
                throw new InvalidOperationException("Not in call");

            await _channel.PushAsync("set_quality", new Dictionary<string, object>
            {
                ["quality"] = quality
            });
        }

        /// <summary>
        /// Gets the current list of participants.
        /// </summary>
        public async Task<Dictionary<string, CallParticipant>> GetParticipantsAsync()
        {
            var response = await _channel.PushAsync("get_participants", new Dictionary<string, object>());

            if (response.IsOk && response.Payload.TryGetValue("participants", out var participants))
            {
                return ParseParticipants(participants as Dictionary<string, object>);
            }

            return new Dictionary<string, CallParticipant>();
        }

        private void SetupEventHandlers()
        {
            _channel.On("participant_joined", payload =>
            {
                var participant = ParseParticipant(payload);
                OnParticipantJoined?.Invoke(participant);
            });

            _channel.On("participant_left", payload =>
            {
                if (payload.TryGetValue("user_id", out var userId))
                {
                    OnParticipantLeft?.Invoke(userId?.ToString());
                }
            });

            _channel.On("media_event", payload =>
            {
                if (payload.TryGetValue("data", out var data))
                {
                    OnMediaEvent?.Invoke(data);
                }
            });

            _channel.On("participant_audio_changed", payload =>
            {
                if (payload.TryGetValue("user_id", out var userId) &&
                    payload.TryGetValue("audio_enabled", out var enabled))
                {
                    OnParticipantAudioChanged?.Invoke(userId?.ToString(), Convert.ToBoolean(enabled));
                }
            });

            _channel.On("participant_video_changed", payload =>
            {
                if (payload.TryGetValue("user_id", out var userId) &&
                    payload.TryGetValue("video_enabled", out var enabled))
                {
                    OnParticipantVideoChanged?.Invoke(userId?.ToString(), Convert.ToBoolean(enabled));
                }
            });

            _channel.On("quality_changed", payload =>
            {
                if (payload.TryGetValue("quality", out var quality))
                {
                    OnQualityChanged?.Invoke(quality?.ToString());
                }
            });

            _channel.On("call_ended", _ =>
            {
                _inCall = false;
                OnCallEnded?.Invoke();
            });
        }

        private CallParticipant ParseParticipant(Dictionary<string, object> payload)
        {
            return new CallParticipant
            {
                UserId = payload.GetValueOrDefault("user_id")?.ToString(),
                EndpointId = payload.GetValueOrDefault("endpoint_id")?.ToString(),
                UserInfo = payload.GetValueOrDefault("user_info") as Dictionary<string, object>,
                AudioEnabled = payload.TryGetValue("audio_enabled", out var audio) && Convert.ToBoolean(audio),
                VideoEnabled = payload.TryGetValue("video_enabled", out var video) && Convert.ToBoolean(video)
            };
        }

        private Dictionary<string, CallParticipant> ParseParticipants(Dictionary<string, object> payload)
        {
            var result = new Dictionary<string, CallParticipant>();
            if (payload == null) return result;

            foreach (var kvp in payload)
            {
                if (kvp.Value is Dictionary<string, object> participantData)
                {
                    result[kvp.Key] = ParseParticipant(participantData);
                }
            }

            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_inCall)
            {
                _ = LeaveCallAsync();
            }
        }
    }
}
