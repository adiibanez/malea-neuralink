using UnityEngine;

public class SimulatorMoveDemo : MonoBehaviour, IMoveReceiver, IVehicleTelemetry
{
    [SerializeField]
    private Camera mainCamera;

    [SerializeField]
    private float rotationSpeed = 5;

    [SerializeField]
    private float driveSpeed = 3;

    private Vector2 _lastDirection;
    private Vector3 _lastPosition;
    private Vector3 _velocity;

    #region IVehicleTelemetry Implementation

    public float Direction => _lastDirection.x;
    public float Speed => _lastDirection.y;
    public float BatteryLevel => -1f; // Not supported, let VehicleTelemetryProvider simulate
    public Vector3 Velocity => _velocity;
    public Vector3 Position => mainCamera != null ? mainCamera.transform.position : transform.position;
    public Quaternion Rotation => mainCamera != null ? mainCamera.transform.rotation : transform.rotation;

    #endregion

    public void Move(Vector2 direction)
    {
        _lastDirection = direction;
    }

    private void Start()
    {
        if (mainCamera != null)
        {
            _lastPosition = mainCamera.transform.position;
        }
    }

    private void Update()
    {
        if (mainCamera == null) return;

        // Calculate velocity before moving
        Vector3 currentPos = mainCamera.transform.position;
        _velocity = (currentPos - _lastPosition) / Time.deltaTime;
        _lastPosition = currentPos;

        // Apply movement
        mainCamera.transform.Rotate(Vector3.up, _lastDirection.x * Time.deltaTime * rotationSpeed);
        mainCamera.transform.position += new Vector3(mainCamera.transform.forward.x, 0, mainCamera.transform.forward.z) * _lastDirection.y * Time.deltaTime * driveSpeed;
    }
}
