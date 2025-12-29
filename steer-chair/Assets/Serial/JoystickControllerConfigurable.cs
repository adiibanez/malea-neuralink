using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class JoystickControllerConfigurable : JoystickController
{
    [ContextMenu("Auto-Detect Port")]
    private void AutoDetectPort()
    {
        string detected = SerialPortUtility.GetSerialPort();
        Debug.Log($"Detected port: {detected}");
        // Note: Would need to expose serialPort field or add a setter
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(JoystickController))]
public class JoystickControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        if (GUILayout.Button("List Available Ports"))
        {
            string[] ports = SerialPortUtility.ListAllPorts();
            Debug.Log($"Available ports: {string.Join(", ", ports)}");
        }
        
        if (GUILayout.Button("Auto-Detect Port"))
        {
            string port = SerialPortUtility.GetSerialPort();
            Debug.Log($"Detected: {port}");
        }
    }
}
#endif