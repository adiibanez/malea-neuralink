using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Sensocto.Models;

namespace Sensocto.Core
{
    /// <summary>
    /// Phoenix Presence implementation for tracking online users/sensors.
    /// Implements the sync algorithm from Phoenix.Presence JavaScript client.
    /// </summary>
    public class PhoenixPresence
    {
        private readonly PhoenixChannel _channel;
        private PresenceState _state = new PresenceState();
        private readonly object _stateLock = new object();

        public PresenceState State
        {
            get
            {
                lock (_stateLock) { return _state; }
            }
        }

        /// <summary>
        /// Called when a new presence joins. (key, currentPresence, newPresence)
        /// </summary>
        public event Action<string, PresenceEntry, PresenceEntry> OnJoin;

        /// <summary>
        /// Called when a presence leaves. (key, currentPresence, leftPresence)
        /// </summary>
        public event Action<string, PresenceEntry, PresenceEntry> OnLeave;

        /// <summary>
        /// Called after syncing state.
        /// </summary>
        public event Action<PresenceState> OnSync;

        public PhoenixPresence(PhoenixChannel channel)
        {
            _channel = channel;

            // Subscribe to presence events
            _channel.On("presence_state", OnPresenceState);
            _channel.On("presence_diff", OnPresenceDiff);
        }

        /// <summary>
        /// Get all online keys (sensor IDs).
        /// </summary>
        public IEnumerable<string> GetOnlineKeys()
        {
            lock (_stateLock)
            {
                return _state.Entries.Keys.ToList();
            }
        }

        /// <summary>
        /// Check if a key is online.
        /// </summary>
        public bool IsOnline(string key)
        {
            lock (_stateLock)
            {
                return _state.IsOnline(key);
            }
        }

        /// <summary>
        /// Get connection count for a key.
        /// </summary>
        public int GetConnectionCount(string key)
        {
            lock (_stateLock)
            {
                return _state.GetConnectionCount(key);
            }
        }

        /// <summary>
        /// Get presence entry for a key.
        /// </summary>
        public PresenceEntry GetEntry(string key)
        {
            lock (_stateLock)
            {
                return _state.Entries.TryGetValue(key, out var entry) ? entry : null;
            }
        }

        private void OnPresenceState(object payload)
        {
            if (payload is Dictionary<string, object> dict)
            {
                var serverState = ParsePresenceState(dict);
                SyncState(serverState);
            }
        }

        private void OnPresenceDiff(object payload)
        {
            if (payload is Dictionary<string, object> dict)
            {
                var diff = PresenceDiff.FromDictionary(dict);
                SyncDiff(diff);
            }
        }

        /// <summary>
        /// Sync full presence state from server (initial state).
        /// </summary>
        public void SyncState(Dictionary<string, PresenceEntry> serverState)
        {
            lock (_stateLock)
            {
                // Process joins for new entries
                foreach (var kvp in serverState)
                {
                    var key = kvp.Key;
                    var newEntry = kvp.Value;

                    _state.Entries.TryGetValue(key, out var currentEntry);

                    if (currentEntry == null)
                    {
                        // New presence
                        _state.Entries[key] = newEntry;
                        OnJoin?.Invoke(key, null, newEntry);
                    }
                    else
                    {
                        // Update existing - merge metas
                        var refs = currentEntry.Metas.Select(m => m.PhxRef).ToHashSet();
                        var newMetas = newEntry.Metas.Where(m => !refs.Contains(m.PhxRef)).ToList();

                        if (newMetas.Count > 0)
                        {
                            currentEntry.Metas.AddRange(newMetas);
                            OnJoin?.Invoke(key, currentEntry, new PresenceEntry { Metas = newMetas });
                        }
                    }
                }

                // Process leaves for removed entries
                var keysToRemove = _state.Entries.Keys.Where(k => !serverState.ContainsKey(k)).ToList();
                foreach (var key in keysToRemove)
                {
                    var leftEntry = _state.Entries[key];
                    _state.Entries.Remove(key);
                    OnLeave?.Invoke(key, leftEntry, leftEntry);
                }
            }

            OnSync?.Invoke(_state);
        }

        /// <summary>
        /// Sync presence diff from server (incremental update).
        /// </summary>
        public void SyncDiff(PresenceDiff diff)
        {
            lock (_stateLock)
            {
                // Process joins
                foreach (var kvp in diff.Joins)
                {
                    var key = kvp.Key;
                    var joinEntry = kvp.Value;

                    _state.Entries.TryGetValue(key, out var currentEntry);

                    if (currentEntry == null)
                    {
                        // New presence
                        _state.Entries[key] = joinEntry;
                        OnJoin?.Invoke(key, null, joinEntry);
                    }
                    else
                    {
                        // Add new metas
                        var refs = currentEntry.Metas.Select(m => m.PhxRef).ToHashSet();
                        var newMetas = joinEntry.Metas.Where(m => !refs.Contains(m.PhxRef)).ToList();

                        if (newMetas.Count > 0)
                        {
                            currentEntry.Metas.AddRange(newMetas);
                            OnJoin?.Invoke(key, currentEntry, new PresenceEntry { Metas = newMetas });
                        }
                    }
                }

                // Process leaves
                foreach (var kvp in diff.Leaves)
                {
                    var key = kvp.Key;
                    var leaveEntry = kvp.Value;

                    if (_state.Entries.TryGetValue(key, out var currentEntry))
                    {
                        var leftRefs = leaveEntry.Metas.Select(m => m.PhxRef).ToHashSet();
                        var remainingMetas = currentEntry.Metas.Where(m => !leftRefs.Contains(m.PhxRef)).ToList();

                        if (remainingMetas.Count == 0)
                        {
                            // All connections left, remove entry
                            _state.Entries.Remove(key);
                            OnLeave?.Invoke(key, currentEntry, leaveEntry);
                        }
                        else
                        {
                            // Some connections remain
                            currentEntry.Metas = remainingMetas;
                            OnLeave?.Invoke(key, currentEntry, leaveEntry);
                        }
                    }
                }
            }

            OnSync?.Invoke(_state);
        }

        private Dictionary<string, PresenceEntry> ParsePresenceState(Dictionary<string, object> dict)
        {
            var result = new Dictionary<string, PresenceEntry>();

            foreach (var kvp in dict)
            {
                if (kvp.Value is Dictionary<string, object> entryDict)
                {
                    result[kvp.Key] = PresenceEntry.FromDictionary(entryDict);
                }
            }

            return result;
        }

        /// <summary>
        /// Disconnect from presence tracking.
        /// </summary>
        public void Dispose()
        {
            _channel.Off("presence_state");
            _channel.Off("presence_diff");
        }
    }
}
