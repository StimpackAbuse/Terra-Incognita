using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TerraIncognita.UI;

namespace TerraIncognita.Unit
{
    /// <summary>
    /// 단일 선택, 드래그 다중 선택, Ctrl+좌클릭 동일 계열 화면 내 전체 선택을 총괄하는 매니저입니다.
    /// 선택 해제는 빈 공간 클릭 대신 유닛 파괴 시 자연스럽게 처리됩니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class SelectionManager : MonoBehaviour
    {
        public static SelectionManager Instance { get; private set; }

        [Header("참조 설정")]
        [Tooltip("렌더링을 담당하는 메인 카메라")]
        [SerializeField] private Camera mainCamera;

        [Tooltip("드래그 박스 UI 컴포넌트")]
        [SerializeField] private SelectionBoxUI selectionBoxUI = null;

        [Header("선택 규칙")]
        [Tooltip("드래그 판정을 위한 최소 마우스 이동 거리 (픽셀 단위)")]
        [SerializeField] private float dragThreshold = 10.0f;

        [Tooltip("유닛 감지용 레이어마스크 (기본: 전체 대상)")]
        [SerializeField] private LayerMask unitLayerMask = ~0;

        [Tooltip("빈 공간 클릭 시 선택 해제 여부 (기본: false - 사용자 기획 요구사항 반영)")]
        [SerializeField] private bool deselectOnEmptyClick = false;

        private readonly List<Unit> selectedUnits = new List<Unit>();
        public IReadOnlyList<Unit> SelectedUnits => selectedUnits;

        private Vector2 mousePressStartPosition;
        private bool isLeftMouseButtonPressed;
        private bool isDragging;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }
        }

        private void Update()
        {
            HandleSelectionInput();
            PruneDestroyedUnits();
        }

        private void HandleSelectionInput()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            // UI 요소 위를 클릭 중인 경우 선택 입력 무시
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            Vector2 currentMousePos = mouse.position.ReadValue();

            // 1. 좌클릭 시작
            if (mouse.leftButton.wasPressedThisFrame)
            {
                isLeftMouseButtonPressed = true;
                isDragging = false;
                mousePressStartPosition = currentMousePos;
            }

            // 2. 좌클릭 유지 및 드래그 갱신
            if (isLeftMouseButtonPressed && mouse.leftButton.isPressed)
            {
                float dragDistance = Vector2.Distance(mousePressStartPosition, currentMousePos);
                if (!isDragging && dragDistance >= dragThreshold)
                {
                    isDragging = true;
                }

                if (isDragging && selectionBoxUI != null)
                {
                    selectionBoxUI.Show(mousePressStartPosition, currentMousePos);
                }
            }

            // 3. 좌클릭 릴리즈
            if (mouse.leftButton.wasReleasedThisFrame && isLeftMouseButtonPressed)
            {
                isLeftMouseButtonPressed = false;

                if (isDragging)
                {
                    // 드래그 박스 다중 선택 실행
                    isDragging = false;
                    if (selectionBoxUI != null) selectionBoxUI.Hide();
                    ExecuteBoxSelection(mousePressStartPosition, currentMousePos);
                }
                else
                {
                    // 단일 클릭 또는 Ctrl+좌클릭 동일 계열 선택 실행
                    ExecuteClickSelection(currentMousePos);
                }
            }
        }

        /// <summary>
        /// 단일 클릭 처리: 일반 좌클릭(단일 유닛) 또는 Ctrl+좌클릭(화면 내 동일 계열 전체 선택)
        /// </summary>
        private void ExecuteClickSelection(Vector2 screenPosition)
        {
            if (mainCamera == null) return;

            Ray ray = mainCamera.ScreenPointToRay(screenPosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, unitLayerMask))
            {
                Unit clickedUnit = hit.collider.GetComponentInParent<Unit>();
                if (clickedUnit != null)
                {
                    Keyboard keyboard = Keyboard.current;
                    bool isCtrlPressed = keyboard != null && (keyboard.ctrlKey.isPressed || keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);

                    if (isCtrlPressed)
                    {
                        // Ctrl + 좌클릭: 현재 화면(뷰포트) 내의 동일 계열 유닛을 모두 선택
                        SelectSeriesOnScreen(clickedUnit);
                    }
                    else
                    {
                        // 일반 단일 선택
                        ClearSelection();
                        AddUnitToSelection(clickedUnit);
                    }
                    return;
                }
            }

            // 유닛이 아닌 빈 공간을 클릭했을 때의 처리
            if (deselectOnEmptyClick)
            {
                ClearSelection();
            }
        }

        /// <summary>
        /// 화면(카메라 뷰포트) 내에 존재하는 지정 유닛과 동일 계열의 모든 유닛을 선택합니다.
        /// </summary>
        private void SelectSeriesOnScreen(Unit baseUnit)
        {
            if (baseUnit == null || baseUnit.Data == null) return;

            string targetSeriesId = baseUnit.Data.SeriesId;
            ClearSelection();

            IReadOnlyList<Unit> allUnits = Unit.ActiveUnits;
            for (int i = 0; i < allUnits.Count; i++)
            {
                Unit unit = allUnits[i];
                if (unit == null || unit.Data == null) continue;

                // 동일 계열 판정
                if (unit.Data.SeriesId == targetSeriesId)
                {
                    // 카메라 뷰포트 내에 있는지 확인
                    Vector3 viewportPos = mainCamera.WorldToViewportPoint(unit.transform.position);
                    bool isInsideScreen = viewportPos.z > 0 &&
                                          viewportPos.x >= 0f && viewportPos.x <= 1f &&
                                          viewportPos.y >= 0f && viewportPos.y <= 1f;

                    if (isInsideScreen)
                    {
                        AddUnitToSelection(unit);
                    }
                }
            }
        }

        /// <summary>
        /// 마우스 드래그 사각형 영역 내에 들어온 유닛들을 선택합니다.
        /// </summary>
        private void ExecuteBoxSelection(Vector2 startPos, Vector2 endPos)
        {
            if (mainCamera == null) return;

            Vector2 min = Vector2.Min(startPos, endPos);
            Vector2 max = Vector2.Max(startPos, endPos);

            List<Unit> unitsInBox = new List<Unit>();
            IReadOnlyList<Unit> allUnits = Unit.ActiveUnits;

            for (int i = 0; i < allUnits.Count; i++)
            {
                Unit unit = allUnits[i];
                if (unit == null) continue;

                Vector3 screenPos = mainCamera.WorldToScreenPoint(unit.transform.position);
                // 카메라 전방에 있고 사각형 안에 포함되는지 판정
                if (screenPos.z > 0 &&
                    screenPos.x >= min.x && screenPos.x <= max.x &&
                    screenPos.y >= min.y && screenPos.y <= max.y)
                {
                    unitsInBox.Add(unit);
                }
            }

            if (unitsInBox.Count > 0)
            {
                ClearSelection();
                for (int i = 0; i < unitsInBox.Count; i++)
                {
                    AddUnitToSelection(unitsInBox[i]);
                }
            }
            else if (deselectOnEmptyClick)
            {
                ClearSelection();
            }
        }

        private void AddUnitToSelection(Unit unit)
        {
            if (unit == null || selectedUnits.Contains(unit)) return;

            selectedUnits.Add(unit);
            unit.SetSelected(true);
            unit.OnUnitDestroyed += HandleUnitDestroyed;
        }

        private void HandleUnitDestroyed(Unit destroyedUnit)
        {
            if (destroyedUnit != null)
            {
                destroyedUnit.OnUnitDestroyed -= HandleUnitDestroyed;
                selectedUnits.Remove(destroyedUnit);
            }
        }

        /// <summary>
        /// 선택 목록을 초기화합니다.
        /// </summary>
        public void ClearSelection()
        {
            for (int i = 0; i < selectedUnits.Count; i++)
            {
                if (selectedUnits[i] != null)
                {
                    selectedUnits[i].OnUnitDestroyed -= HandleUnitDestroyed;
                    selectedUnits[i].SetSelected(false);
                }
            }
            selectedUnits.Clear();
        }

        /// <summary>
        /// 소멸되거나 파괴된 유닛을 선택 목록에서 정리합니다.
        /// </summary>
        private void PruneDestroyedUnits()
        {
            for (int i = selectedUnits.Count - 1; i >= 0; i--)
            {
                if (selectedUnits[i] == null)
                {
                    selectedUnits.RemoveAt(i);
                }
            }
        }
    }
}
