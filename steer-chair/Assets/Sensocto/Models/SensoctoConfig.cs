using System;
using System.Collections.Generic;

namespace Sensocto.Models
{
    /// <summary>
    /// Configuration for connecting to a Sensocto server.
    /// </summary>
    [Serializable]
    public class SensoctoConfig
    {
        public string ServerUrl { get; set; } = "ws://localhost:4000/socket";
        public string BearerToken { get; set; }
        public int HeartbeatIntervalMs { get; set; } = 30000;
        public int ReconnectDelayMs { get; set; } = 1000;
        public int MaxReconnectDelayMs { get; set; } = 30000;
        public int ConnectionTimeoutMs { get; set; } = 10000;
        public Dictionary<string, string> Params { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Parameters for joining a sensor channel.
    /// </summary>
    [Serializable]
    public class SensorJoinParams
    {
        public string ConnectorId { get; set; }
        public string ConnectorName { get; set; }
        public string SensorId { get; set; }
        public string SensorName { get; set; }
        public string[] Attributes { get; set; }
        public string SensorType { get; set; }
        public int SamplingRate { get; set; } = 50;
        public int BatchSize { get; set; } = 10;
        public string BearerToken { get; set; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["connector_id"] = ConnectorId,
                ["connector_name"] = ConnectorName,
                ["sensor_id"] = SensorId,
                ["sensor_name"] = SensorName,
                ["attributes"] = Attributes,
                ["sensor_type"] = SensorType,
                ["sampling_rate"] = SamplingRate,
                ["batch_size"] = BatchSize,
                ["bearer_token"] = BearerToken
            };
        }
    }

    /// <summary>
    /// Parameters for joining a connector channel.
    /// </summary>
    [Serializable]
    public class ConnectorJoinParams
    {
        public string ConnectorId { get; set; }
        public string ConnectorName { get; set; }
        public string ConnectorType { get; set; }
        public string[] Features { get; set; }
        public string BearerToken { get; set; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["connector_id"] = ConnectorId,
                ["connector_name"] = ConnectorName,
                ["connector_type"] = ConnectorType,
                ["features"] = Features,
                ["bearer_token"] = BearerToken
            };
        }
    }
}
