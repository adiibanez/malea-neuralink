using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sensocto.SDK
{
    /// <summary>
    /// Configuration for the Sensocto client.
    /// Can be used as a ScriptableObject for easy Unity Editor configuration.
    /// </summary>
    [CreateAssetMenu(fileName = "SensoctoConfig", menuName = "Sensocto/Client Configuration")]
    public class SensoctoConfig : ScriptableObject
    {
        [Header("Server Settings")]
        [Tooltip("The Sensocto server URL (e.g., https://sensocto.example.com)")]
        [SerializeField] private string serverUrl = "http://localhost:4000";

        [Header("Connector Settings")]
        [Tooltip("Unique identifier for this connector (auto-generated if empty)")]
        [SerializeField] private string connectorId;

        [Tooltip("Human-readable name for this connector")]
        [SerializeField] private string connectorName = "Unity Connector";

        [Tooltip("Type of connector (e.g., unity, mobile, iot)")]
        [SerializeField] private string connectorType = "unity";

        [Header("Authentication")]
        [Tooltip("Bearer token for authentication (can be set at runtime)")]
        [SerializeField] private string bearerToken;

        [Header("Connection Options")]
        [Tooltip("Automatically join connector channel on connect")]
        [SerializeField] private bool autoJoinConnector = true;

        [Tooltip("Heartbeat interval in milliseconds")]
        [SerializeField] private int heartbeatIntervalMs = 30000;

        [Tooltip("Connection timeout in milliseconds")]
        [SerializeField] private int connectionTimeoutMs = 10000;

        [Tooltip("Auto-reconnect on disconnect")]
        [SerializeField] private bool autoReconnect = true;

        [Tooltip("Maximum reconnection attempts")]
        [SerializeField] private int maxReconnectAttempts = 5;

        [Header("Features")]
        [Tooltip("List of supported features by this connector")]
        [SerializeField] private List<string> features = new List<string>();

        // Properties for runtime access
        public string ServerUrl
        {
            get => serverUrl;
            set => serverUrl = value;
        }

        public string ConnectorId
        {
            get => connectorId;
            set => connectorId = value;
        }

        public string ConnectorName
        {
            get => connectorName;
            set => connectorName = value;
        }

        public string ConnectorType
        {
            get => connectorType;
            set => connectorType = value;
        }

        public string BearerToken
        {
            get => bearerToken;
            set => bearerToken = value;
        }

        public bool AutoJoinConnector
        {
            get => autoJoinConnector;
            set => autoJoinConnector = value;
        }

        public int HeartbeatIntervalMs
        {
            get => heartbeatIntervalMs;
            set => heartbeatIntervalMs = value;
        }

        public int ConnectionTimeoutMs
        {
            get => connectionTimeoutMs;
            set => connectionTimeoutMs = value;
        }

        public bool AutoReconnect
        {
            get => autoReconnect;
            set => autoReconnect = value;
        }

        public int MaxReconnectAttempts
        {
            get => maxReconnectAttempts;
            set => maxReconnectAttempts = value;
        }

        public List<string> Features
        {
            get => features;
            set => features = value;
        }

        /// <summary>
        /// Creates a runtime configuration (not a ScriptableObject).
        /// </summary>
        public static SensoctoConfig CreateRuntime(string serverUrl, string connectorName = "Unity Connector")
        {
            var config = CreateInstance<SensoctoConfig>();
            config.serverUrl = serverUrl;
            config.connectorName = connectorName;
            return config;
        }

        /// <summary>
        /// Validates the configuration.
        /// </summary>
        public bool Validate(out string error)
        {
            if (string.IsNullOrEmpty(serverUrl))
            {
                error = "Server URL is required";
                return false;
            }

            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
            {
                error = "Invalid server URL format";
                return false;
            }

            if (heartbeatIntervalMs < 1000)
            {
                error = "Heartbeat interval must be at least 1000ms";
                return false;
            }

            error = null;
            return true;
        }
    }
}
