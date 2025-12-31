using UnityEngine;
using UnityEngine.UIElements;

#nullable enable

[RequireComponent(typeof(UIDocument))]

public class WebcamTexDemo : MonoBehaviour
{
    private WebCamTexture _webCamTexture;

    private Image _uiImage;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void OnEnable()
    {
        // Get CameraImage
        _uiImage ??= GetComponent<UIDocument>().rootVisualElement.Q<Image>(DriveUIElementNames.CameraImage);
        _webCamTexture ??= new WebCamTexture();
        _webCamTexture.Play();


        _uiImage.image = _webCamTexture;
    }

    // Update is called once per frame
    void OnDisable()
    {
        _webCamTexture.Pause();
    }
}
