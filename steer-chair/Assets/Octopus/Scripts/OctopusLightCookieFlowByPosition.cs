using UnityEngine;

namespace Warcool.OctopusUnderwater
{
    public class OctopusLightCookieFlowByPosition : MonoBehaviour
    {
        public Light targetLight;       // Light source reference
        public float flowSpeedX = 0.1f; // X Shift Speed

        void Update()
        {
            // Shifting the position of the light source along the X axis over time
            if (targetLight != null)
            {
                Vector3 pos = targetLight.transform.position;
                pos.x += flowSpeedX * Time.deltaTime;
                targetLight.transform.position = pos;
            }
        }
    }
}


