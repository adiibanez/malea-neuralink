using UnityEngine;

public class WheelchairSimulator : MonoBehaviour
{
    private JoystickController joystick;
    
    [Header("Input Simulation")]
    public float yaw;    // -30 to 30
    public float pitch;  // -20 to 20
    
    void Start()
    {
        // Auto-detect port
        string port = SerialPortUtility.GetSerialPort();
        Debug.Log($"Using serial port: {port}");
        
        // Create controller (if not already a component)
        joystick = gameObject.AddComponent<JoystickController>();
        
        // Or if you want to set port programmatically before Start():
        // Use SerializeField or a custom init method
    }
    
    void Update()
    {
        // Example: Map keyboard to joystick input
        float inputDirection = Input.GetAxis("Horizontal") * 30f;  // -30 to 30
        float inputSpeed = Input.GetAxis("Vertical") * 20f;        // -20 to 20
        
        //joystick.UpdateInput(inputDirection, inputSpeed);
        
        // Debug display
        var (speed, direction) = joystick.GetCurrentValues();
        // Use these values to drive your simulator wheelchair
    }
}