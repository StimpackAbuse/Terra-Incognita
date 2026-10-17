using UnityEngine;
using UnityEngine.UI;

namespace TerraIncognita.UI
{
    /// <summary>
    /// 마우스 드래그 다중 선택 영역(Selection Box)을 Canvas UI에 실시간 렌더링하는 컴포넌트입니다.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class SelectionBoxUI : MonoBehaviour
    {
        private Image boxImage;
        private RectTransform rectTransform;

        private void Awake()
        {
            boxImage = GetComponent<Image>();
            rectTransform = GetComponent<RectTransform>();

            // 피벗을 좌하단(0,0)으로 정렬하여 크기와 위치 계산 일원화
            rectTransform.pivot = Vector2.zero;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.zero;

            Hide();
        }

        /// <summary>
        /// 시작 위치와 현재 마우스 위치를 기반으로 박스 영역을 갱신하고 표시합니다.
        /// </summary>
        public void Show(Vector2 startScreenPos, Vector2 currentScreenPos)
        {
            if (boxImage == null || rectTransform == null) return;

            Vector2 min = Vector2.Min(startScreenPos, currentScreenPos);
            Vector2 max = Vector2.Max(startScreenPos, currentScreenPos);
            Vector2 size = max - min;

            rectTransform.position = min;
            rectTransform.sizeDelta = size;

            if (!boxImage.enabled)
            {
                boxImage.enabled = true;
            }
        }

        /// <summary>
        /// 드래그 박스를 화면에서 숨깁니다.
        /// </summary>
        public void Hide()
        {
            if (boxImage != null)
            {
                boxImage.enabled = false;
            }
        }
    }
}
