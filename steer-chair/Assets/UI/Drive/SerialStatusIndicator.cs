using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Displays serial connection status in the UI.
/// Subscribes to JoystickController's connection events for real-time updates.
/// Only active in DriveChair scene.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class SerialStatusIndicator : MonoBehaviour
{
    private const string DRIVE_CHAIR_SCENE = "DriveChair";

    [Header("UI Settings")]
    [SerializeField] private Color connectedColor = new Color(0.2f, 0.8f, 0.2f); // Green
    [SerializeField] private Color disconnectedColor = new Color(0.8f, 0.2f, 0.2f); // Red
    [SerializeField] private Color connectingColor = new Color(0.8f, 0.8f, 0.2f); // Yellow

    [Header("References")]
    [SerializeField] private JoystickController joystickController;

    private VisualElement _root;
    private VisualElement _statusIndicator;
    private Label _statusLabel;

    void OnEnable()
    {
        // Only initialize in DriveChair scene
        if (SceneManager.GetActiveScene().name != DRIVE_CHAIR_SCENE)
        {
            enabled = false;
            return;
        }

        StartCoroutine(InitializeDelayed());
    }

    private System.Collections.IEnumerator InitializeDelayed()
    {
        yield return null;

        // Double-check we're in the right scene
        if (SceneManager.GetActiveScene().name != DRIVE_CHAIR_SCENE)
        {
            yield break;
        }

        _root = GetComponent<UIDocument>().rootVisualElement;
        if (_root == null) yield break;

        // Find or create status elements
        SetupStatusElements();

        // Find JoystickController if not assigned
        if (joystickController == null)
        {
            joystickController = FindAnyObjectByType<JoystickController>();
        }

        if (joystickController != null)
        {
            // Subscribe to connection events
            joystickController.OnSerialConnectionChanged += OnConnectionStateChanged;

            // Set initial state
            UpdateStatusDisplay(joystickController.IsConnected);

            Debug.Log("[SerialStatusIndicator] Initialized and subscribed to JoystickController");
        }
        else
        {
            Debug.LogWarning("[SerialStatusIndicator] JoystickController not found");
            UpdateStatusDisplay(false);
        }
    }

    void OnDisable()
    {
        if (joystickController != null)
        {
            joystickController.OnSerialConnectionChanged -= OnConnectionStateChanged;
        }
    }

    private void SetupStatusElements()
    {
        // Find the status elements defined in UXML
        _statusIndicator = _root.Q<VisualElement>(DriveUIElementNames.SerialStatusIndicator);
        _statusLabel = _root.Q<Label>(DriveUIElementNames.SerialStatusLabel);

        if (_statusIndicator == null)
        {
            Debug.LogWarning($"[SerialStatusIndicator] {DriveUIElementNames.SerialStatusIndicator} not found in UXML");
        }

        if (_statusLabel == null)
        {
            Debug.LogWarning($"[SerialStatusIndicator] {DriveUIElementNames.SerialStatusLabel} not found in UXML");
        }
    }

    private void OnConnectionStateChanged(bool isConnected)
    {
        UpdateStatusDisplay(isConnected);
    }

    private void UpdateStatusDisplay(bool isConnected)
    {
        if (_statusIndicator == null || _statusLabel == null) return;

        if (isConnected)
        {
            _statusIndicator.style.backgroundColor = connectedColor;
            _statusLabel.text = "Connected";
        }
        else
        {
            _statusIndicator.style.backgroundColor = disconnectedColor;
            _statusLabel.text = "Disconnected";
        }

        Debug.Log($"[SerialStatusIndicator] Status updated: {(isConnected ? "Connected" : "Disconnected")}");
    }

    /// <summary>
    /// Manually refresh the status display.
    /// </summary>
    public void RefreshStatus()
    {
        if (joystickController != null)
        {
            UpdateStatusDisplay(joystickController.IsConnected);
        }
    }
}
