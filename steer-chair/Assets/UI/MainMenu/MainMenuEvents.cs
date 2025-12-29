using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(UIDocument))]
public class MainMenuEvents : MonoBehaviour
{
    private List<Button> _buttonList;

    public void OnEnable()
    {
        _buttonList ??= GetComponent<UIDocument>().rootVisualElement
            .Query<Button>()
            .ToList();

        foreach (var b in _buttonList)
        {
            switch (b.name)
            {
                case "StartChair":
                b.RegisterCallback<ClickEvent>(OnDriveButtonClicked);
                break;
                case "StartSimulator":
                b.RegisterCallback<ClickEvent>(OnSimulatorButtonClicked);
                break;
                case "Quit":
                b.RegisterCallback<ClickEvent>(OnQuitButtonClicked);
                break;
            }
        }
    }

    public void OnDisable()
    {
        foreach (var b in _buttonList)
        {
            switch (b.name)
            {
                case "StartChair":
                b.UnregisterCallback<ClickEvent>(OnDriveButtonClicked);
                break;
                case "StartSimulator":
                b.UnregisterCallback<ClickEvent>(OnSimulatorButtonClicked);
                break;
                case "Quit":
                b.UnregisterCallback<ClickEvent>(OnQuitButtonClicked);
                break;
            }
        }
    }

    private void OnDriveButtonClicked(ClickEvent evt)
    {
        Debug.Log("OnDriveButtonClicked");

        SceneManager.LoadSceneAsync("DriveChair", LoadSceneMode.Single);
    }

    private void OnSimulatorButtonClicked(ClickEvent evt)
    {
        Debug.Log("OnSimulatorButtonClicked");

        SceneManager.LoadSceneAsync("Simulator", LoadSceneMode.Single);
    }

    private void OnQuitButtonClicked(ClickEvent evt)
    {
        Debug.Log("OnQuitButtonClicked");

        Application.Quit(0);
    }
}
