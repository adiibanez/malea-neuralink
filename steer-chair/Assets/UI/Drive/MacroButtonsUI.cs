using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Manages the dynamic creation of actuator controls in the Drive UI.
/// Creates rows with labels and +/- buttons for each actuator, plus action buttons.
/// Auto-finds or creates MacroController if not assigned.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class MacroButtonsUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MacroController macroController;

    [Header("UI Settings")]
    [SerializeField] private string containerName = "MacroButtonsContainer";

    private VisualElement _root;
    private VisualElement _container;
    private Dictionary<string, Button> _allButtons = new Dictionary<string, Button>();

    void Awake()
    {
        // Auto-find or create MacroController
        if (macroController == null)
        {
            macroController = FindFirstObjectByType<MacroController>();
        }

        if (macroController == null)
        {
            macroController = gameObject.AddComponent<MacroController>();
            Debug.Log("[MacroButtonsUI] Created MacroController automatically");
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

        if (_root == null)
        {
            Debug.LogError("[MacroButtonsUI] UIDocument root is null");
            yield break;
        }

        _container = _root.Q<VisualElement>(containerName);

        if (_container == null)
        {
            Debug.LogWarning($"[MacroButtonsUI] Container '{containerName}' not found in UI");
            yield break;
        }

        Debug.Log($"[MacroButtonsUI] Found container: {containerName}");

        if (macroController != null)
        {
            macroController.OnMacroStarted += OnMacroStarted;
            macroController.OnMacroCompleted += OnMacroCompleted;
        }

        CreateUI();
    }

    void OnDisable()
    {
        if (macroController != null)
        {
            macroController.OnMacroStarted -= OnMacroStarted;
            macroController.OnMacroCompleted -= OnMacroCompleted;
        }

        ClearUI();
    }

    /// <summary>
    /// Creates the full UI from configuration.
    /// </summary>
    public void CreateUI()
    {
        if (_container == null)
        {
            Debug.LogError("[MacroButtonsUI] Container is null");
            return;
        }

        if (macroController == null)
        {
            Debug.LogError("[MacroButtonsUI] MacroController is null");
            AddErrorLabel("MacroController not found");
            return;
        }

        ClearUI();

        var actuators = macroController.GetActuators();
        var actions = macroController.GetActions();

        if ((actuators == null || actuators.Length == 0) && (actions == null || actions.Length == 0))
        {
            Debug.LogWarning("[MacroButtonsUI] No actuators or actions configured");
            AddErrorLabel("No config found");
            return;
        }

        // Create actuator rows
        foreach (var actuator in actuators)
        {
            CreateActuatorRow(actuator);
        }

        // Add separator if we have both actuators and actions
        if (actuators.Length > 0 && actions.Length > 0)
        {
            var separator = new VisualElement();
            separator.style.height = 10;
            separator.style.marginTop = 5;
            separator.style.marginBottom = 5;
            separator.style.borderBottomWidth = 1;
            separator.style.borderBottomColor = new StyleColor(new Color(0, 0, 0, 0.2f));
            _container.Add(separator);
        }

        // Create action buttons
        foreach (var action in actions)
        {
            CreateActionButton(action);
        }

        Debug.Log($"[MacroButtonsUI] Created UI: {actuators.Length} actuators, {actions.Length} actions");
    }

    /// <summary>
    /// Creates a row for an actuator with label and +/- buttons.
    /// </summary>
    private void CreateActuatorRow(ActuatorConfig actuator)
    {
        // Container row
        var row = new VisualElement
        {
            name = $"ActuatorRow_{actuator.id}"
        };
        row.AddToClassList("actuator-row");
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 8;

        // Label
        var label = new Label(actuator.label)
        {
            name = $"ActuatorLabel_{actuator.id}"
        };
        label.AddToClassList("actuator-label");
        label.style.color = new StyleColor(Color.black);
        label.style.fontSize = 14;
        label.style.flexGrow = 1;
        row.Add(label);

        // Button container
        var buttonContainer = new VisualElement();
        buttonContainer.style.flexDirection = FlexDirection.Row;

        // Decrease button
        var decreaseBtn = new Button(() => OnActuatorDecrease(actuator))
        {
            name = $"ActuatorBtn_{actuator.id}_decrease",
            text = actuator.decreaseLabel
        };
        decreaseBtn.AddToClassList("button");
        decreaseBtn.AddToClassList("actuator-btn");
        decreaseBtn.style.width = 40;
        decreaseBtn.style.height = 40;
        decreaseBtn.style.fontSize = 20;
        decreaseBtn.style.marginRight = 4;
        decreaseBtn.style.backgroundColor = new StyleColor(new Color(0.96f, 0.26f, 0.21f)); // Red
        decreaseBtn.style.color = new StyleColor(Color.white);
        buttonContainer.Add(decreaseBtn);
        _allButtons[$"{actuator.id}_decrease"] = decreaseBtn;

        // Increase button
        var increaseBtn = new Button(() => OnActuatorIncrease(actuator))
        {
            name = $"ActuatorBtn_{actuator.id}_increase",
            text = actuator.increaseLabel
        };
        increaseBtn.AddToClassList("button");
        increaseBtn.AddToClassList("actuator-btn");
        increaseBtn.style.width = 40;
        increaseBtn.style.height = 40;
        increaseBtn.style.fontSize = 20;
        increaseBtn.style.backgroundColor = new StyleColor(new Color(0.30f, 0.69f, 0.31f)); // Green
        increaseBtn.style.color = new StyleColor(Color.white);
        buttonContainer.Add(increaseBtn);
        _allButtons[$"{actuator.id}_increase"] = increaseBtn;

        row.Add(buttonContainer);
        _container.Add(row);
    }

    /// <summary>
    /// Creates an action button.
    /// </summary>
    private void CreateActionButton(ActionConfig action)
    {
        var button = new Button(() => OnActionClicked(action))
        {
            name = $"ActionBtn_{action.id}",
            text = action.label
        };
        button.AddToClassList("button");
        button.AddToClassList("action-btn");
        button.AddToClassList("mb-1");
        button.style.height = 40;

        // Apply custom color
        if (!string.IsNullOrEmpty(action.color))
        {
            if (ColorUtility.TryParseHtmlString(action.color, out Color color))
            {
                button.style.backgroundColor = new StyleColor(color);
                float brightness = (color.r * 299 + color.g * 587 + color.b * 114) / 1000f;
                button.style.color = new StyleColor(brightness > 0.5f ? Color.black : Color.white);
            }
        }

        _container.Add(button);
        _allButtons[action.id] = button;
    }

    private void AddErrorLabel(string message)
    {
        var label = new Label(message)
        {
            name = "MacroErrorLabel"
        };
        label.style.color = new StyleColor(Color.red);
        label.style.fontSize = 12;
        _container?.Add(label);
    }

    private void ClearUI()
    {
        if (_container == null) return;

        // Remove all children except the header label
        var children = new List<VisualElement>();
        foreach (var child in _container.Children())
        {
            // Keep HeaderLabel from UXML
            if (child.name == "HeaderLabel")
                continue;

            children.Add(child);
        }

        foreach (var child in children)
        {
            _container.Remove(child);
        }

        _allButtons.Clear();
    }

    private void OnActuatorIncrease(ActuatorConfig actuator)
    {
        Debug.Log($"[MacroButtonsUI] Actuator increase: {actuator.label}");
        macroController?.ExecuteActuatorIncrease(actuator);
    }

    private void OnActuatorDecrease(ActuatorConfig actuator)
    {
        Debug.Log($"[MacroButtonsUI] Actuator decrease: {actuator.label}");
        macroController?.ExecuteActuatorDecrease(actuator);
    }

    private void OnActionClicked(ActionConfig action)
    {
        Debug.Log($"[MacroButtonsUI] Action clicked: {action.label}");
        macroController?.ExecuteAction(action);
    }

    private void OnMacroStarted(string id, string label)
    {
        if (_allButtons.TryGetValue(id, out Button button))
        {
            button.SetEnabled(false);
        }
        // Also try with _increase/_decrease suffix stripped
        string baseId = id.Replace("_increase", "").Replace("_decrease", "");
        if (id.EndsWith("_increase") && _allButtons.TryGetValue($"{baseId}_increase", out Button incBtn))
        {
            incBtn.SetEnabled(false);
        }
        if (id.EndsWith("_decrease") && _allButtons.TryGetValue($"{baseId}_decrease", out Button decBtn))
        {
            decBtn.SetEnabled(false);
        }
    }

    private void OnMacroCompleted(string id, string label)
    {
        if (_allButtons.TryGetValue(id, out Button button))
        {
            button.SetEnabled(true);
        }
        string baseId = id.Replace("_increase", "").Replace("_decrease", "");
        if (id.EndsWith("_increase") && _allButtons.TryGetValue($"{baseId}_increase", out Button incBtn))
        {
            incBtn.SetEnabled(true);
        }
        if (id.EndsWith("_decrease") && _allButtons.TryGetValue($"{baseId}_decrease", out Button decBtn))
        {
            decBtn.SetEnabled(true);
        }
    }

    public void RefreshUI()
    {
        macroController?.LoadConfig();
        CreateUI();
    }

    public void SetAllButtonsEnabled(bool enabled)
    {
        foreach (var button in _allButtons.Values)
        {
            button.SetEnabled(enabled);
        }
    }

    public void StopCurrentMacro()
    {
        macroController?.StopCurrentMacro();
    }
}
