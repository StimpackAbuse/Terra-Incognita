using TerraIncognita.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TerraIncognita.Core
{
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class PeriodicTerrainCameraController : MonoBehaviour
    {
        [SerializeField] private PeriodicTerrainWorld terrainWorld;
        [SerializeField] private float panSpeed = 35f;
        [SerializeField] private float zoomSpeed = 5f;
        [SerializeField] private float minZoom = 12f;
        [SerializeField] private float maxZoom = 60f;
        [SerializeField] private bool enableEdgeScroll = true;
        [SerializeField] private float edgeMargin = 15f;

        private Camera controlledCamera;

        private void Awake()
        {
            controlledCamera = GetComponent<Camera>();
            if (terrainWorld == null)
                terrainWorld = FindFirstObjectByType<PeriodicTerrainWorld>();
        }

        private void Update()
        {
            HandlePan();
            HandleZoom();
            WrapCameraX();
        }

        private void HandlePan()
        {
            Vector2 input = Vector2.zero;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.isPressed) input.x -= 1f;
                if (keyboard.rightArrowKey.isPressed) input.x += 1f;
                if (keyboard.downArrowKey.isPressed) input.y -= 1f;
                if (keyboard.upArrowKey.isPressed) input.y += 1f;
            }

            if (enableEdgeScroll && Mouse.current != null)
            {
                Vector2 mousePosition = Mouse.current.position.ReadValue();
                if (mousePosition.x >= 0f && mousePosition.x <= Screen.width &&
                    mousePosition.y >= 0f && mousePosition.y <= Screen.height)
                {
                    if (mousePosition.x <= edgeMargin) input.x -= 1f;
                    else if (mousePosition.x >= Screen.width - edgeMargin) input.x += 1f;

                    if (mousePosition.y <= edgeMargin) input.y -= 1f;
                    else if (mousePosition.y >= Screen.height - edgeMargin) input.y += 1f;
                }
            }

            if (input.sqrMagnitude < 0.001f)
                return;

            input.Normalize();
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = transform.right;
            right.y = 0f;
            right.Normalize();

            transform.position +=
                (right * input.x + forward * input.y) * (panSpeed * Time.unscaledDeltaTime);
        }

        private void HandleZoom()
        {
            if (Mouse.current == null || !controlledCamera.orthographic)
                return;

            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f)
                return;

            controlledCamera.orthographicSize = Mathf.Clamp(
                controlledCamera.orthographicSize - Mathf.Sign(scroll) *
                zoomSpeed * Time.unscaledDeltaTime * 10f,
                minZoom,
                maxZoom);
        }

        private void WrapCameraX()
        {
            if (terrainWorld == null)
                return;

            Vector3 position = transform.position;
            float halfWidth = terrainWorld.WorldWidth * 0.5f;
            float wrappedX = Mathf.Repeat(position.x + halfWidth, terrainWorld.WorldWidth) - halfWidth;
            if (!Mathf.Approximately(position.x, wrappedX))
            {
                position.x = wrappedX;
                transform.position = position;
            }
        }
    }
}
