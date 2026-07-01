using UnityEngine;
using UnityEngine.InputSystem;

namespace Engine.Monobehaviours.Managers
{
    [RequireComponent(typeof(Camera))]
    public class PlaySceneCameraController : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private float keyboardPanSpeed = 8f;
        [SerializeField] private float zoomSpeed = 5f;
        [SerializeField] private float minOrthographicSize = 2f;
        [SerializeField] private float maxOrthographicSize = 50f;

        private Vector3 panCameraStartPosition;
        private Vector3 panPointerStartWorldPosition;
        private bool isPanning;

        private void Awake()
        {
            targetCamera = targetCamera != null ? targetCamera : GetComponent<Camera>();
        }

        private void Update()
        {
            if (targetCamera == null || !targetCamera.orthographic)
                return;

            HandleKeyboardPan();
            HandleMousePan();
            HandleZoom();
        }

        private void HandleKeyboardPan()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            var panInput = Vector2.zero;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                panInput.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                panInput.y -= 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                panInput.x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                panInput.x += 1f;

            if (panInput == Vector2.zero)
                return;

            var delta = (Vector3)(panInput.normalized * keyboardPanSpeed * targetCamera.orthographicSize * Time.deltaTime);
            targetCamera.transform.position += delta;
        }

        private void HandleMousePan()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            var panPressed = mouse.middleButton.isPressed || mouse.rightButton.isPressed;
            if (mouse.middleButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
            {
                if (PlaySceneCampaignRenderer.IsPointerOverCampaignUi)
                    return;

                isPanning = true;
                panCameraStartPosition = targetCamera.transform.position;
                panPointerStartWorldPosition = ScreenToWorld(mouse.position.ReadValue());
            }
            else if (!panPressed)
            {
                isPanning = false;
            }

            if (!isPanning)
                return;

            var pointerWorldPosition = ScreenToWorld(mouse.position.ReadValue());
            var offset = panPointerStartWorldPosition - pointerWorldPosition;
            offset.z = 0f;
            targetCamera.transform.position = panCameraStartPosition + offset;
        }

        private void HandleZoom()
        {
            var mouse = Mouse.current;
            if (mouse == null || PlaySceneCampaignRenderer.IsPointerOverCampaignUi)
                return;

            var scrollDelta = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scrollDelta, 0f))
                return;

            var mouseScreenPosition = mouse.position.ReadValue();
            var worldBeforeZoom = ScreenToWorld(mouseScreenPosition);

            var nextSize = targetCamera.orthographicSize - scrollDelta * zoomSpeed * 0.01f;
            targetCamera.orthographicSize = Mathf.Clamp(nextSize, minOrthographicSize, maxOrthographicSize);

            var worldAfterZoom = ScreenToWorld(mouseScreenPosition);
            var zoomOffset = worldBeforeZoom - worldAfterZoom;
            zoomOffset.z = 0f;
            targetCamera.transform.position += zoomOffset;
        }

        private Vector3 ScreenToWorld(Vector2 screenPosition)
        {
            var planeDistance = Mathf.Abs(targetCamera.transform.position.z);
            var worldPosition = targetCamera.ScreenToWorldPoint(
                new Vector3(screenPosition.x, screenPosition.y, planeDistance));
            worldPosition.z = 0f;
            return worldPosition;
        }
    }
}
