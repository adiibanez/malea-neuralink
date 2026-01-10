using UnityEngine;

/// <summary>
/// Plays audio clips when the object is hovered over or clicked with the mouse.
/// Uses raycasting to detect mouse interaction with this object or any child colliders.
/// </summary>
public class VehicleSoundInteraction : MonoBehaviour
{
    [Header("Audio Clips")]
    [Tooltip("Sound to play on mouse hover (optional)")]
    public AudioClip hoverSound;

    [Tooltip("Sound to play on mouse click")]
    public AudioClip clickSound;

    [Header("Settings")]
    [Range(0f, 1f)]
    public float volume = 1f;

    [Tooltip("Prevent hover sound from playing repeatedly while hovering")]
    public bool playHoverOnce = true;

    [Tooltip("Automatically add MeshColliders to child meshes if no collider exists")]
    public bool autoAddColliders = true;

    private AudioSource audioSource;
    private bool isMouseOver = false;
    private bool hasPlayedHover = false;
    private Camera mainCamera;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f; // 3D sound

        if (autoAddColliders)
        {
            EnsureColliders();
        }
    }

    void Start()
    {
        mainCamera = Camera.main;
    }

    void EnsureColliders()
    {
        // Check if this object or any child has a collider
        Collider existingCollider = GetComponentInChildren<Collider>();
        if (existingCollider != null) return;

        // Add MeshColliders to all child MeshFilters
        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();
        foreach (MeshFilter mf in meshFilters)
        {
            if (mf.GetComponent<Collider>() == null && mf.sharedMesh != null)
            {
                MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
        }
    }

    void Update()
    {
        if (mainCamera == null) return;

        bool wasMouseOver = isMouseOver;
        isMouseOver = false;

        // Raycast from mouse position
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            // Check if the hit object is this object or a child of this object
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                isMouseOver = true;
            }
        }

        // Handle mouse enter
        if (isMouseOver && !wasMouseOver)
        {
            OnHoverEnter();
        }
        // Handle mouse exit
        else if (!isMouseOver && wasMouseOver)
        {
            OnHoverExit();
        }

        // Handle click
        if (isMouseOver && Input.GetMouseButtonDown(0))
        {
            OnClick();
        }
    }

    void OnHoverEnter()
    {
        if (hoverSound != null && (!playHoverOnce || !hasPlayedHover))
        {
            audioSource.PlayOneShot(hoverSound, volume);
            hasPlayedHover = true;
        }
    }

    void OnHoverExit()
    {
        hasPlayedHover = false;
    }

    void OnClick()
    {
        if (clickSound != null)
        {
            audioSource.PlayOneShot(clickSound, volume);
        }
        else if (hoverSound != null)
        {
            // Fall back to hover sound if no click sound assigned
            audioSource.PlayOneShot(hoverSound, volume);
        }
    }
}
