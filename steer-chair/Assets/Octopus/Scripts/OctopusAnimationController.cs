using UnityEngine;

namespace Warcool.OctopusUnderwater
{
    public class OctopusAnimationController : MonoBehaviour
    {
        [Header("Joystick Input")]
        public OctopusVirtualJoystick joystick;        // Link to our virtual joystick

        [Header("Animator Settings")]
        public Animator animator;               // Link to Character Animator
        public float movementThreshold = 0.1f;  // Threshold for determining the start of movement
        public float animationSpeed = 1f;       // Animation playback speed multiplier

        [Header("Character Rotation")]
        public Transform characterTransform;    // Character object reference for angle calculation
        public float maxTurnSpeed = 180f;       // Maximum expected turning speed (deg/sec)
        public float turnSmoothing = 5f;        // Speed ​​of smoothing changes in the Turn parameter

        private float lastFrameAngle = 0f;
        private float currentTurnParam = 0f;     // Current smoothed Turn value

        private void Start()
        {
            if (characterTransform == null)
            {
                characterTransform = this.transform;
            }

            lastFrameAngle = characterTransform.eulerAngles.z;
        }

        private void Update()
        {
            // Receiving input values ​​from the joystick
            float moveX = joystick.Horizontal();
            float moveY = joystick.Vertical();

            // Calculating the amount of movement
            float magnitude = new Vector2(moveX, moveY).magnitude;

            // Determining if the character is moving
            bool isMoving = magnitude > movementThreshold;
            animator.SetBool("IsMoving", isMoving);

            // Speed ​​Option for Basic Blend Tree Motion
            float speedParam = isMoving ? Mathf.Clamp01(magnitude) : 0f;
            animator.SetFloat("Speed", speedParam);

            // Setting the animation playback speed
            animator.speed = animationSpeed;

            // --- Calculate Turn for the second layer ---
            float currentAngle = characterTransform.eulerAngles.z;
            float angleDelta = Mathf.DeltaAngle(lastFrameAngle, currentAngle);
            lastFrameAngle = currentAngle;

            // Convert the change in angle per frame to degrees/second
            float angleDeltaPerSecond = angleDelta / Time.deltaTime;

            // Normalize to the range [-1..1]
            float targetTurnParam = Mathf.Clamp(angleDeltaPerSecond / maxTurnSpeed, -1f, 1f);

            // Smoothing out the change in turnParam
            currentTurnParam = Mathf.Lerp(currentTurnParam, targetTurnParam, turnSmoothing * Time.deltaTime);

            // Set a smooth Turn value in the animator
            animator.SetFloat("Turn", currentTurnParam);
        }
    }
}


