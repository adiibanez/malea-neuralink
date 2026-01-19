using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Handles the right menu button behaviors:
/// - Lights/Hazards: Toggle with continuous blinking while active
/// - Other buttons: Highlight for a few seconds then return to standby
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class RightMenuButtons : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] private float highlightDuration = 2f;
    [SerializeField] private float blinkInterval = 0.5f;

    [Header("Colors")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.84f, 0f); // Gold
    [SerializeField] private Color lightsActiveColor = new Color(1f, 0.92f, 0.23f); // Yellow
    [SerializeField] private Color hazardActiveColor = new Color(1f, 0.6f, 0f); // Orange
    [SerializeField] private Color defaultColor = new Color(0.78f, 0.78f, 0.78f); // Light gray

    private VisualElement _root;
    private Dictionary<string, Button> _buttons = new Dictionary<string, Button>();
    private Dictionary<string, Color> _originalColors = new Dictionary<string, Color>();
    private Dictionary<string, Coroutine> _activeCoroutines = new Dictionary<string, Coroutine>();

    // Toggle states for lights and hazards
    private bool _lightsActive = false;
    private bool _hazardActive = false;

    private static readonly string[] ToggleButtons = { "LightsBtn", "HazardBtn" };
    private static readonly string[] MomentaryButtons = { "PowerBtn", "ProfileBtn", "HornBtn", "FasterBtn", "SlowerBtn" };

    void OnEnable()
    {
        StartCoroutine(InitializeDelayed());
    }

    private IEnumerator InitializeDelayed()
    {
        yield return null;

        _root = GetComponent<UIDocument>().rootVisualElement;
        if (_root == null) yield break;

        // Find and setup all right menu buttons
        SetupButton("PowerBtn");
        SetupButton("ProfileBtn");
        SetupButton("HornBtn");
        SetupButton("LightsBtn");
        SetupButton("HazardBtn");
        SetupButton("FasterBtn");
        SetupButton("SlowerBtn");

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

        _lightsActive = false;
        _hazardActive = false;
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

        // Handle toggle buttons (Lights, Hazard)
        if (buttonName == "LightsBtn")
        {
            _lightsActive = !_lightsActive;
            HandleToggleButton(buttonName, _lightsActive, lightsActiveColor);
        }
        else if (buttonName == "HazardBtn")
        {
            _hazardActive = !_hazardActive;
            HandleToggleButton(buttonName, _hazardActive, hazardActiveColor);
        }
        // Handle Profile button - open auth UI
        else if (buttonName == "ProfileBtn")
        {
            HandleMomentaryButton(buttonName);
            AuthUI.ShowModal();
        }
        // Handle momentary buttons
        else if (System.Array.Exists(MomentaryButtons, b => b == buttonName))
        {
            HandleMomentaryButton(buttonName);
        }
    }

    private void HandleToggleButton(string buttonName, bool isActive, Color activeColor)
    {
        // Stop existing coroutine if any
        if (_activeCoroutines.TryGetValue(buttonName, out Coroutine existing) && existing != null)
        {
            StopCoroutine(existing);
            _activeCoroutines.Remove(buttonName);
        }

        if (isActive)
        {
            // Start blinking
            _activeCoroutines[buttonName] = StartCoroutine(BlinkCoroutine(buttonName, activeColor));
        }
        else
        {
            // Restore original color
            if (_buttons.TryGetValue(buttonName, out Button button) &&
                _originalColors.TryGetValue(buttonName, out Color original))
            {
                button.style.backgroundColor = new StyleColor(original);
            }
        }
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

    private IEnumerator BlinkCoroutine(string buttonName, Color activeColor)
    {
        if (!_buttons.TryGetValue(buttonName, out Button button)) yield break;
        if (!_originalColors.TryGetValue(buttonName, out Color original)) yield break;

        bool isOn = true;
        while (true)
        {
            button.style.backgroundColor = new StyleColor(isOn ? activeColor : original);
            isOn = !isOn;
            yield return new WaitForSeconds(blinkInterval);
        }
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
    /// Gets if lights are currently active.
    /// </summary>
    public bool IsLightsActive => _lightsActive;

    /// <summary>
    /// Gets if hazard is currently active.
    /// </summary>
    public bool IsHazardActive => _hazardActive;

    /// <summary>
    /// Turns off all active toggles (lights, hazard).
    /// </summary>
    public void TurnOffAllToggles()
    {
        if (_lightsActive)
        {
            _lightsActive = false;
            HandleToggleButton("LightsBtn", false, lightsActiveColor);
        }
        if (_hazardActive)
        {
            _hazardActive = false;
            HandleToggleButton("HazardBtn", false, hazardActiveColor);
        }
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
}
