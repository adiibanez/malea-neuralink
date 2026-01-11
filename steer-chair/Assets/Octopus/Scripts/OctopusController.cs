using UnityEngine;


namespace Warcool.OctopusUnderwater
{
    public class OctopusController : MonoBehaviour
    {
        public OctopusVirtualJoystick joystick; // Joystick link
        public float moveSpeed = 5f; // Traveling speed
        public float rotationOffset = 90f; // Angular adjustment (if model is oriented along X)
        public float rotationSmoothness = 5f; // Turn smoothing speed
        public float alignmentSpeed = 2f; // Vertical alignment speed

        private float currentAngle = 0f; // Current direction angle

        private void Update()
        {
            // Receiving input values ​​from the joystick
            float moveX = -joystick.Horizontal(); // Inversion of horizontal control
            float moveY = joystick.Vertical();

            // Create a direction vector
            Vector3 moveDirection = new Vector3(moveX, moveY, 0f);

            // Character movement
            transform.position += moveDirection * moveSpeed * Time.deltaTime;

            // We limit movement along the X and Y axes
            transform.position = new Vector3(transform.position.x, transform.position.y, 0f);

            // If there is joystick input
            if (moveDirection != Vector3.zero)
            {
                // Calculate the desired angle
                float targetAngle = Mathf.Atan2(moveY, moveX) * Mathf.Rad2Deg;

                // Smooth change of the current angle
                currentAngle = Mathf.LerpAngle(currentAngle, targetAngle - rotationOffset, Time.deltaTime * rotationSmoothness);
            }
            else
            {
                // Gradual alignment to the vertical (angle Z tends to 0)
                currentAngle = Mathf.LerpAngle(currentAngle, 0f, Time.deltaTime * alignmentSpeed);
            }

            // Setting the character's rotation
            transform.rotation = Quaternion.Euler(0f, 0f, currentAngle);
        }
    }
}

