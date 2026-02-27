using System;
using UnityEngine;
using UnityEngine.UIElements;

public class HoverActivateButton
{
    private const float ActivationTime = 0.4f;

    private readonly Button _button;
    private readonly VisualElement _fillBar;

    private float _hoverStartTime;
    private IVisualElementScheduledItem _scheduler;

    public HoverActivateButton(Button button, string gaugeName = "StopGauge")
    {
        _button = button;
        _fillBar = button.Q<VisualElement>(gaugeName);

        if(_fillBar == null)
            throw new ArgumentNullException($"Button: {button?.name} contains no VisualElement #{gaugeName}");

        button.RegisterCallback<MouseEnterEvent>(OnHoverEnter);
        button.RegisterCallback<MouseLeaveEvent>(OnHoverLeave);
        button.RegisterCallback<ClickEvent>(OnClick);
    }

    private void OnHoverEnter(MouseEnterEvent evnt)
    {
        Debug.Log("OnHoverEnter");

        if(!_button.enabledSelf)
            return;

        _hoverStartTime = Time.time;

        _fillBar.style.height = Length.Percent(100);

        _scheduler ??= _button.schedule
            .Execute(CheckActivation)
            .Every(16);

        _scheduler.Resume();
    }

    private void OnHoverLeave(MouseLeaveEvent evnt)
    {
        _hoverStartTime = float.MaxValue;
        _fillBar.style.height = Length.Percent(0);
    }

    private void OnClick(ClickEvent evnt)
    {
        _hoverStartTime = float.MaxValue;
        _fillBar.style.height = Length.Percent(0);
    }

    private void CheckActivation()
    {
        if (Time.time - _hoverStartTime >= ActivationTime)
        {
            _hoverStartTime = float.MaxValue;
            _scheduler.Pause();
            _button.SendEvent(new ClickEvent());
            _fillBar.style.height = Length.Percent(0);
        }
    }
}
