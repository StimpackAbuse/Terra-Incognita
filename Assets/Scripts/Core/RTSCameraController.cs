using UnityEngine;
using UnityEngine.InputSystem;

namespace TerraIncognita.Core
{
    /// <summary>
    /// 스타크래프트 방식의 화면 경계 마우스 스크롤 및 화살표 키 이동, 휠 줌을 지원하는 탑다운 RTS 카메라입니다.
    /// WASD 키 입력을 배제하여 향후 단축키 시스템과의 충돌을 방지합니다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class RTSCameraController : MonoBehaviour
    {
        [Header("이동 속도 설정")]
        [Tooltip("키보드/마우스 경계 스크롤 이동 속도 (m/s)")]
        [SerializeField] private float panSpeed = 20.0f;

        [Tooltip("화면 경계 마우스 스크롤 감지 여백 (픽셀 단위)")]
        [SerializeField] private float edgeMargin = 15.0f;

        [Tooltip("화면 경계 스크롤 활성화 여부")]
        [SerializeField] private bool enableEdgeScroll = true;

        [Header("줌 설정")]
        [Tooltip("마우스 휠 줌 속도")]
        [SerializeField] private float zoomSpeed = 8.0f;

        [Tooltip("최소 카메라 높이 (줌 인 한계)")]
        [SerializeField] private float minZoomHeight = 8.0f;

        [Tooltip("최대 카메라 높이 (줌 아웃 한계)")]
        [SerializeField] private float maxZoomHeight = 35.0f;

        [Header("이동 제한 영역 (World Bounds)")]
        [SerializeField] private Vector2 xBounds = new Vector2(-50.0f, 50.0f);
        [SerializeField] private Vector2 zBounds = new Vector2(-50.0f, 50.0f);

        private Camera cam;

        private void Awake()
        {
            cam = GetComponent<Camera>();
        }

        private void Update()
        {
            HandlePanMovement();
            HandleZoom();
        }

        private void HandlePanMovement()
        {
            Vector3 moveDirection = Vector3.zero;

            // 1. 화살표 키 입력 (WASD 배제)
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.isPressed) moveDirection.z += 1.0f;
                if (keyboard.downArrowKey.isPressed) moveDirection.z -= 1.0f;
                if (keyboard.leftArrowKey.isPressed) moveDirection.x -= 1.0f;
                if (keyboard.rightArrowKey.isPressed) moveDirection.x += 1.0f;
            }

            // 2. 스타크래프트 방식 화면 경계 마우스 커서 스크롤
            if (enableEdgeScroll)
            {
                Mouse mouse = Mouse.current;
                if (mouse != null)
                {
                    Vector2 mousePos = mouse.position.ReadValue();
                    int screenWidth = Screen.width;
                    int screenHeight = Screen.height;

                    // 게임 창 내부 유효 영역 검사
                    if (mousePos.x >= 0 && mousePos.x <= screenWidth && mousePos.y >= 0 && mousePos.y <= screenHeight)
                    {
                        if (mousePos.x <= edgeMargin) moveDirection.x -= 1.0f;
                        else if (mousePos.x >= screenWidth - edgeMargin) moveDirection.x += 1.0f;

                        if (mousePos.y <= edgeMargin) moveDirection.z -= 1.0f;
                        else if (mousePos.y >= screenHeight - edgeMargin) moveDirection.z += 1.0f;
                    }
                }
            }

            if (moveDirection.sqrMagnitude > 0.001f)
            {
                moveDirection.Normalize();

                // 카메라가 기울어진 각도와 무관하게 지면 평면(XZ) 기준으로 이동
                Vector3 forward = transform.forward;
                forward.y = 0;
                forward.Normalize();

                Vector3 right = transform.right;
                right.y = 0;
                right.Normalize();

                Vector3 translation = (right * moveDirection.x + forward * moveDirection.z) * (panSpeed * Time.unscaledDeltaTime);
                Vector3 newPos = transform.position + translation;

                // 이동 경계 Clamp
                newPos.x = Mathf.Clamp(newPos.x, xBounds.x, xBounds.y);
                newPos.z = Mathf.Clamp(newPos.z, zBounds.x, zBounds.y);

                transform.position = newPos;
            }
        }

        private void HandleZoom()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            float scrollValue = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollValue) > 0.01f)
            {
                float zoomDelta = -Mathf.Sign(scrollValue) * zoomSpeed * Time.unscaledDeltaTime * 10f;
                Vector3 newPos = transform.position + transform.forward * zoomDelta;

                // 높이(Y축) 기준으로 줌 제한
                if (newPos.y >= minZoomHeight && newPos.y <= maxZoomHeight)
                {
                    transform.position = newPos;
                }
            }
        }
    }
}
