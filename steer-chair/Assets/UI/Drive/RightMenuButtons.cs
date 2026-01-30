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
    [SerializeField] private AudioClip excuseMeClip;
    [SerializeField] private AudioClip noClip;
    [SerializeField] private AudioClip thankYouClip;
    [SerializeField] private AudioClip yesClip;

    [Header("Macro Controller")]
    [SerializeField] private MacroController macroController;

    [Header("Mode Indicator")]
    [SerializeField] private ModeIndicator modeIndicator;

    private VisualElement _root;
    private Dictionary<string, Button> _buttons = new Dictionary<string, Button>();
    private Dictionary<string, Color> _originalColors = new Dictionary<string, Color>();
    private Dictionary<string, Coroutine> _activeCoroutines = new Dictionary<string, Coroutine>();
    private Dictionary<string, AudioClip> _audioClips = new Dictionary<string, AudioClip>();

    private static readonly string[] AudioButtons = { "AudioBtn_excuse_me", "AudioBtn_no", "AudioBtn_thank_you", "AudioBtn_yes" };
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

        // Auto-find MacroController
        if (macroController == null)
        {
            macroController = FindFirstObjectByType<MacroController>();
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
        // Use serialized fields (assigned in Inspector) as the primary source
        if (excuseMeClip != null)
            _audioClips["excuse_me"] = excuseMeClip;
        if (noClip != null)
            _audioClips["no"] = noClip;
        if (thankYouClip != null)
            _audioClips["thank_you"] = thankYouClip;
        if (yesClip != null)
            _audioClips["yes"] = yesClip;

        // Log which clips are loaded
        foreach (var kvp in _audioClips)
        {
            Debug.Log($"[RightMenuButtons] Audio clip ready: {kvp.Key}");
        }

        if (_audioClips.Count == 0)
        {
            Debug.LogWarning("[RightMenuButtons] No audio clips assigned. Please assign clips in the Inspector.");
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

        // Setup audio buttons
        foreach (var btnName in AudioButtons)
        {
            SetupButton(btnName);
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
            // Toggle mode and activate relay 5 for 5 seconds
            modeIndicator?.ToggleMode();
            HandleRelayButton(buttonName, 5, 5.0f); // Relay 5 for 5 seconds
        }
        else if (buttonName == "ProfileBtn")
        {
            HandleRelayButton(buttonName, 5, 1.0f); // Relay 5 for 1 second
        }
        else if (buttonName == "PowerOnOffBtn")
        {
            HandleRelayButton(buttonName, 6, 6.0f); // Relay 6 for 6 seconds
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

    private void HandleRelayButton(string buttonName, int relayNumber, float duration)
    {
        // Stop existing coroutine if any
        if (_activeCoroutines.TryGetValue(buttonName, out Coroutine existing) && existing != null)
        {
            StopCoroutine(existing);
        }

        // Start relay activation coroutine
        _activeCoroutines[buttonName] = StartCoroutine(RelayActivationCoroutine(buttonName, relayNumber, duration));
    }

    private IEnumerator RelayActivationCoroutine(string buttonName, int relayNumber, float duration)
    {
        if (!_buttons.TryGetValue(buttonName, out Button button)) yield break;
        if (!_originalColors.TryGetValue(buttonName, out Color original)) yield break;

        // Set active color
        button.style.backgroundColor = new StyleColor(activeColor);
        button.SetEnabled(false);

        // Activate relay (send full forward command to robot/relay number)
        // S63 = full speed forward, D31 = neutral direction
        if (macroController != null)
        {
            string activateMacro = $"S63D31R{relayNumber}";
            macroController.SendRawCommand(activateMacro);
            Debug.Log($"[RightMenuButtons] Activated relay {relayNumber}: {activateMacro}");
        }

        // Wait for duration
        yield return new WaitForSeconds(duration);

        // Deactivate relay (send neutral command)
        if (macroController != null)
        {
            string deactivateMacro = $"S31D31R{relayNumber}";
            macroController.SendRawCommand(deactivateMacro);
            Debug.Log($"[RightMenuButtons] Deactivated relay {relayNumber}: {deactivateMacro}");
        }

        // Restore original color and enable button
        button.style.backgroundColor = new StyleColor(original);
        button.SetEnabled(true);

        _activeCoroutines.Remove(buttonName);
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
    /// Reloads audio clips from serialized fields.
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
