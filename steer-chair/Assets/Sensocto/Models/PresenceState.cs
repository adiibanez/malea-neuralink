using System;
using System.Collections.Generic;
using System.Linq;

namespace Sensocto.Models
{
    /// <summary>
    /// Metadata for a single presence entry.
    /// </summary>
    [Serializable]
    public class PresenceMeta
    {
        public string SensorId { get; set; }
        public long OnlineAt { get; set; }
        public string PhxRef { get; set; }
        public Dictionary<string, object> Extra { get; set; } = new Dictionary<string, object>();

        public static PresenceMeta FromDictionary(Dictionary<string, object> dict)
        {
            var meta = new PresenceMeta();

            if (dict.TryGetValue("sensor_id", out var sensorId))
                meta.SensorId = sensorId?.ToString();

            if (dict.TryGetValue("online_at", out var onlineAt))
                meta.OnlineAt = Convert.ToInt64(onlineAt);

            if (dict.TryGetValue("phx_ref", out var phxRef))
                meta.PhxRef = phxRef?.ToString();

            foreach (var kvp in dict)
            {
                if (kvp.Key != "sensor_id" && kvp.Key != "online_at" && kvp.Key != "phx_ref")
                {
                    meta.Extra[kvp.Key] = kvp.Value;
                }
            }

            return meta;
        }
    }

    /// <summary>
    /// Presence entry for a single key (sensor).
    /// </summary>
    [Serializable]
    public class PresenceEntry
    {
        public List<PresenceMeta> Metas { get; set; } = new List<PresenceMeta>();

        public static PresenceEntry FromDictionary(Dictionary<string, object> dict)
        {
            var entry = new PresenceEntry();

            if (dict.TryGetValue("metas", out var metas) && metas is List<object> metaList)
            {
                foreach (var meta in metaList)
                {
                    if (meta is Dictionary<string, object> metaDict)
                    {
                        entry.Metas.Add(PresenceMeta.FromDictionary(metaDict));
                    }
                }
            }

            return entry;
        }
    }

    /// <summary>
    /// Diff payload for presence updates.
    /// </summary>
    [Serializable]
    public class PresenceDiff
    {
        public Dictionary<string, PresenceEntry> Joins { get; set; } = new Dictionary<string, PresenceEntry>();
        public Dictionary<string, PresenceEntry> Leaves { get; set; } = new Dictionary<string, PresenceEntry>();

        public static PresenceDiff FromDictionary(Dictionary<string, object> dict)
        {
            var diff = new PresenceDiff();

            if (dict.TryGetValue("joins", out var joins) && joins is Dictionary<string, object> joinsDict)
            {
                foreach (var kvp in joinsDict)
                {
                    if (kvp.Value is Dictionary<string, object> entryDict)
                    {
                        diff.Joins[kvp.Key] = PresenceEntry.FromDictionary(entryDict);
                    }
                }
            }

            if (dict.TryGetValue("leaves", out var leaves) && leaves is Dictionary<string, object> leavesDict)
            {
                foreach (var kvp in leavesDict)
                {
                    if (kvp.Value is Dictionary<string, object> entryDict)
                    {
                        diff.Leaves[kvp.Key] = PresenceEntry.FromDictionary(entryDict);
                    }
                }
            }

            return diff;
        }
    }

    /// <summary>
    /// Complete presence state.
    /// </summary>
    [Serializable]
    public class PresenceState
    {
        public Dictionary<string, PresenceEntry> Entries { get; set; } = new Dictionary<string, PresenceEntry>();

        public IEnumerable<string> GetOnlineSensorIds()
        {
            return Entries.Keys;
        }

        public int GetConnectionCount(string sensorId)
        {
            return Entries.TryGetValue(sensorId, out var entry) ? entry.Metas.Count : 0;
        }

        public bool IsOnline(string sensorId)
        {
            return Entries.ContainsKey(sensorId) && Entries[sensorId].Metas.Count > 0;
        }

        public static PresenceState FromDictionary(Dictionary<string, object> dict)
        {
            var state = new PresenceState();

            foreach (var kvp in dict)
            {
                if (kvp.Value is Dictionary<string, object> entryDict)
                {
                    state.Entries[kvp.Key] = PresenceEntry.FromDictionary(entryDict);
                }
            }

            return state;
        }
    }
}
