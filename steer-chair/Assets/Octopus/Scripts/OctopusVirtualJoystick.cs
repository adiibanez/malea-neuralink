using UnityEngine;
using UnityEngine.EventSystems;

namespace Warcool.OctopusUnderwater
{
    public class OctopusVirtualJoystick : MonoBehaviour, IDragHandler, IPointerUpHandler, IPointerDownHandler
    {
        public RectTransform background;
        public RectTransform handle;

        private Vector2 inputVector;

        private Canvas canvas;
        private Camera uiCamera;

        private bool isDragging = false;

        private void Start()
        {
            // We get Canvas and its camera
            canvas = GetComponentInParent<Canvas>();
            if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                uiCamera = canvas.worldCamera;
            }

            // Hiding the joystick at startup
            background.gameObject.SetActive(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            StartDragging(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (isDragging)
            {
                DragJoystick(eventData.position);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            StopDragging();
        }

        private void StartDragging(Vector2 screenPosition)
        {
            // Show joystick where you click or touch
            Vector2 touchPosition;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.transform as RectTransform,
                screenPosition,
                uiCamera,
                out touchPosition
            );

            background.anchoredPosition = touchPosition;
            background.gameObject.SetActive(true);
            isDragging = true;
        }

        private void DragJoystick(Vector2 screenPosition)
        {
            Vector2 position;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(background, screenPosition, uiCamera, out position);

            position.x = position.x / background.sizeDelta.x;
            position.y = position.y / background.sizeDelta.y;

            inputVector = new Vector2(position.x * 2, position.y * 2);
            inputVector = inputVector.magnitude > 1.0f ? inputVector.normalized : inputVector;

            handle.anchoredPosition = new Vector2(inputVector.x * (background.sizeDelta.x / 2), inputVector.y * (background.sizeDelta.y / 2));
        }

        private void StopDragging()
        {
            inputVector = Vector2.zero;
            handle.anchoredPosition = Vector2.zero;

            // Hiding the joystick when the mouse is released
            background.gameObject.SetActive(false);
            isDragging = false;
        }

        public float Horizontal()
        {
            return inputVector.x;
        }

        public float Vertical()
        {
            return inputVector.y;
        }

        private void Update()
        {
            // Checking mouse input for testing on a PC
            if (Input.GetMouseButtonDown(0))
            {
                StartDragging(Input.mousePosition);
            }
            else if (Input.GetMouseButton(0) && isDragging)
            {
                DragJoystick(Input.mousePosition);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                StopDragging();
            }
        }
    }
}


