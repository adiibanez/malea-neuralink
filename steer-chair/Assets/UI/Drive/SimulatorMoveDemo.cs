using UnityEngine;
using Sensocto;

public class SimulatorMoveDemo : MonoBehaviour, IMoveReceiver
{
    [SerializeField]
    private Camera mainCamera;

    [SerializeField]
    private float rotationSpeed = 5;

    [SerializeField]
    private float driveSpeed = 3;

    private Vector2 _lastDirection;

    public void Move(Vector2 direction)
    {
        _lastDirection = direction;
    }

    private void Update()
    {
        mainCamera.transform.Rotate(Vector3.up, _lastDirection.x * Time.deltaTime * rotationSpeed);

        mainCamera.transform.position += new Vector3(mainCamera.transform.forward.x, 0, mainCamera.transform.forward.z) * _lastDirection.y * Time.deltaTime * driveSpeed;
    }
}
