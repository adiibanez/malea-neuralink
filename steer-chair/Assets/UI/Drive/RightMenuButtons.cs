using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Handles the right menu button behaviors:
/// - Audio buttons: Play audio clips from Audio/Eoin folder
/// - Mode button: Switch relay 5 for 5 seconds
/// - Profile button: Switch relay 5 for 1 second
/// - Power On/Off button: Switch relay 6 for 6 seconds
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class RightMenuButtons : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] private float highlightDuration = 2f;

    [Header("Colors")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.84f, 0f); // Gold
    [SerializeField] private Color activeColor = new Color(0.30f, 0.69f, 0.31f); // Green
    [SerializeField] private Color defaultColor = new Color(0.78f, 0.78f, 0.78f); // Light gray

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private string audioResourcePath = "Audio/Eoin";

    [Header("Serial Controller")]
    [SerializeField] private JoystickController joystickController;

    [Header("Mode Indicator")]
    [SerializeField] private ModeIndicator modeIndicator;

    private VisualElement _root;
    private Dictionary<string, Button> _buttons = new Dictionary<string, Button>();
    private Dictionary<string, Color> _originalColors = new Dictionary<string, Color>();
    private Dictionary<string, Coroutine> _activeCoroutines = new Dictionary<string, Coroutine>();
    private Dictionary<string, AudioClip> _audioClips = new Dictionary<string, AudioClip>();

    private static readonly string[] ControlButtons = { "ModeBtn", "ProfileBtn", "PowerOnOffBtn" };

    void Awake()
    {
        // Auto-find or create AudioSource
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }

        // Auto-find JoystickController
        if (joystickController == null)
        {
            joystickController = FindFirstObjectByType<JoystickController>();
        }

        // Auto-find or create ModeIndicator
        if (modeIndicator == null)
        {
            modeIndicator = GetComponent<ModeIndicator>();
        }
        if (modeIndicator == null)
        {
            modeIndicator = gameObject.AddComponent<ModeIndicator>();
            Debug.Log("[RightMenuButtons] Created ModeIndicator automatically");
        }

        // Load audio clips
        LoadAudioClips();
    }

    private void LoadAudioClips()
    {
        // Auto-load all clips from Resources folder by filename
        var clips = Resources.LoadAll<AudioClip>(audioResourcePath);
        foreach (var clip in clips)
        {
            _audioClips[clip.name] = clip;
            Debug.Log($"[RightMenuButtons] Audio clip loaded: {clip.name}");
        }

        if (_audioClips.Count == 0)
        {
            Debug.LogWarning($"[RightMenuButtons] No audio clips found in Resources/{audioResourcePath}");
        }
    }

    void OnEnable()
    {
        StartCoroutine(InitializeDelayed());
    }

    private IEnumerator InitializeDelayed()
    {
        yield return null;

        _root = GetComponent<UIDocument>().rootVisualElement;
        if (_root == null) yield break;

        // Setup audio buttons (auto-discover all AudioBtn_* buttons)
        var allButtons = _root.Query<Button>().ToList();
        foreach (var btn in allButtons)
        {
            if (btn.name != null && btn.name.StartsWith("AudioBtn_"))
                SetupButton(btn.name);
        }

        // Setup control buttons
        foreach (var btnName in ControlButtons)
        {
            SetupButton(btnName);
        }

        Debug.Log($"[RightMenuButtons] Initialized {_buttons.Count} buttons");
    }

    void OnDisable()
    {
        // Stop all coroutines and restore colors
        foreach (var kvp in _activeCoroutines)
        {
            if (kvp.Value != null)
                StopCoroutine(kvp.Value);
        }
        _activeCoroutines.Clear();

        // Restore original colors
        foreach (var kvp in _buttons)
        {
            if (_originalColors.TryGetValue(kvp.Key, out Color original))
            {
                kvp.Value.style.backgroundColor = new StyleColor(original);
            }
        }

        // Unregister callbacks
        foreach (var kvp in _buttons)
        {
            kvp.Value.UnregisterCallback<ClickEvent>(OnButtonClicked);
        }
        _buttons.Clear();
        _originalColors.Clear();
    }

    private void SetupButton(string buttonName)
    {
        var button = _root.Q<Button>(buttonName);
        if (button == null)
        {
            Debug.LogWarning($"[RightMenuButtons] Button not found: {buttonName}");
            return;
        }

        _buttons[buttonName] = button;

        // Store and set the default color explicitly
        _originalColors[buttonName] = defaultColor;
        button.style.backgroundColor = new StyleColor(defaultColor);

        button.RegisterCallback<ClickEvent>(OnButtonClicked);
    }

    private void OnButtonClicked(ClickEvent evt)
    {
        var button = evt.target as Button;
        if (button == null) return;

        string buttonName = button.name;
        Debug.Log($"[RightMenuButtons] Button clicked: {buttonName}");

        // Handle audio buttons
        if (buttonName.StartsWith("AudioBtn_"))
        {
            string clipName = buttonName.Replace("AudioBtn_", "");
            PlayAudioClip(clipName);
            HandleMomentaryButton(buttonName);
        }
        // Handle control buttons
        else if (buttonName == "ModeBtn")
        {
            // Toggle mode AFTER relay 5 finishes (5 seconds)
            HandleRelayButton(buttonName, 5, 5.0f, () => modeIndicator?.ToggleMode());
        }
        else if (buttonName == "ProfileBtn")
        {
            HandleRelayButton(buttonName, 5, 1.0f);
        }
        else if (buttonName == "PowerOnOffBtn")
        {
            // Reset to driving mode AFTER relay 6 finishes (6 seconds)
            HandleRelayButton(buttonName, 6, 6.0f, () => modeIndicator?.SetMode(ModeIndicator.OperatingMode.Driving));
        }
    }

    private void PlayAudioClip(string clipName)
    {
        if (_audioClips.TryGetValue(clipName, out AudioClip clip))
        {
            audioSource.PlayOneShot(clip);
            Debug.Log($"[RightMenuButtons] Playing audio: {clipName}");
        }
        else
        {
            Debug.LogWarning($"[RightMenuButtons] Audio clip not found: {clipName}");
        }
    }

    private void HandleRelayButton(string buttonName, int relayNumber, float duration, System.Action onComplete = null)
    {
        // Stop existing coroutine if any
        if (_activeCoroutines.TryGetValue(buttonName, out Coroutine existing) && existing != null)
        {
            StopCoroutine(existing);
        }

        // Start relay activation coroutine
        _activeCoroutines[buttonName] = StartCoroutine(RelayActivationCoroutine(buttonName, relayNumber, duration, onComplete));
    }

    private IEnumerator RelayActivationCoroutine(string buttonName, int relayNumber, float duration, System.Action onComplete = null)
    {
        if (!_buttons.TryGetValue(buttonName, out Button button)) yield break;
        if (!_originalColors.TryGetValue(buttonName, out Color original)) yield break;

        // Set active color
        button.style.backgroundColor = new StyleColor(activeColor);
        button.SetEnabled(false);

        // Hardware is 0-indexed, so relay 5 = index 4
        int relayIndex = relayNumber - 1;
        string activateCmd = $"S31D31R{relayIndex}";

        Debug.Log($"[RightMenuButtons] Relay {relayNumber} ON for {duration}s: {activateCmd}");
        AuditLog.Log(AuditLog.Category.Relay, $"Relay {relayNumber} ON for {duration}s ({buttonName})", activateCmd);

        // Send relay command every 100ms for the duration.
        // This interleaves with SendLoop's R8 heartbeat on the background thread.
        float elapsed = 0f;
        while (elapsed < duration)
        {
            joystickController?.SharedWrite(activateCmd);
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        Debug.Log($"[RightMenuButtons] Relay {relayNumber} OFF (stopped sending)");
        AuditLog.Log(AuditLog.Category.Relay, $"Relay {relayNumber} OFF ({buttonName})", activateCmd);

        // Restore original color and enable button
        button.style.backgroundColor = new StyleColor(original);
        button.SetEnabled(true);

        _activeCoroutines.Remove(buttonName);

        onComplete?.Invoke();
    }

    private void HandleMomentaryButton(string buttonName)
    {
        // Stop existing coroutine if any
        if (_activeCoroutines.TryGetValue(buttonName, out Coroutine existing) && existing != null)
        {
            StopCoroutine(existing);
        }

        // Start highlight coroutine
        _activeCoroutines[buttonName] = StartCoroutine(HighlightCoroutine(buttonName));
    }

    private IEnumerator HighlightCoroutine(string buttonName)
    {
        if (!_buttons.TryGetValue(buttonName, out Button button)) yield break;
        if (!_originalColors.TryGetValue(buttonName, out Color original)) yield break;

        // Set highlight color
        button.style.backgroundColor = new StyleColor(highlightColor);

        // Wait for duration
        yield return new WaitForSeconds(highlightDuration);

        // Restore original color
        button.style.backgroundColor = new StyleColor(original);

        _activeCoroutines.Remove(buttonName);
    }

    /// <summary>
    /// Sets enabled state for all buttons.
    /// </summary>
    public void SetAllButtonsEnabled(bool enabled)
    {
        foreach (var button in _buttons.Values)
        {
            button.SetEnabled(enabled);
        }
    }

    /// <summary>
    /// Reloads audio clips from Resources.
    /// </summary>
    public void ReloadAudioClips()
    {
        _audioClips.Clear();
        LoadAudioClips();
    }

    /// <summary>
    /// Gets the current mode indicator reference.
    /// </summary>
    public ModeIndicator ModeIndicator => modeIndicator;
}
