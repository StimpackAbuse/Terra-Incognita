using System;
using System.Collections.Generic;
using UnityEngine;
using TerraIncognita.Data;

namespace TerraIncognita.Unit
{
    /// <summary>
    /// 인게임 런타임 유닛의 상태와 생명주기를 관리하는 핵심 컴포넌트입니다.
    /// 정적 데이터(UnitData)를 참조하며, 선택 상태 및 인디케이터 제어를 담당합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class Unit : MonoBehaviour
    {
        private static readonly List<Unit> s_ActiveUnits = new List<Unit>();
        public static IReadOnlyList<Unit> ActiveUnits => s_ActiveUnits;

        [Header("데이터 참조")]
        [Tooltip("정적 유닛 스펙 데이터")]
        [SerializeField] private UnitData unitData;

        [Header("시각적 표시")]
        [Tooltip("선택 시 활성화되는 하단 인디케이터 오브젝트")]
        [SerializeField] private GameObject selectionIndicator;

        // 선택 상태
        public bool IsSelected { get; private set; }
        public UnitData Data => unitData;

        // 유닛 파괴 이벤트 (선택 목록에서 자연스럽게 제거하기 위함)
        public event Action<Unit> OnUnitDestroyed;

        private void OnEnable()
        {
            if (!s_ActiveUnits.Contains(this))
            {
                s_ActiveUnits.Add(this);
            }
        }

        private void OnDisable()
        {
            s_ActiveUnits.Remove(this);
            if (IsSelected)
            {
                SetSelected(false);
            }
        }

        private void Start()
        {
            // 시작 시 인디케이터 초기화
            UpdateIndicator();
        }

        private void OnDestroy()
        {
            OnUnitDestroyed?.Invoke(this);
            s_ActiveUnits.Remove(this);
        }

        /// <summary>
        /// 유닛의 선택 상태를 설정합니다.
        /// </summary>
        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            UpdateIndicator();
        }

        private void UpdateIndicator()
        {
            if (selectionIndicator != null)
            {
                selectionIndicator.SetActive(IsSelected);
            }
        }

        /// <summary>
        /// 런타임 또는 에디터 세팅용 UnitData 지정
        /// </summary>
        public void SetUnitData(UnitData data)
        {
            unitData = data;
        }

        /// <summary>
        /// 인디케이터 오브젝트 지정
        /// </summary>
        public void SetSelectionIndicator(GameObject indicator)
        {
            selectionIndicator = indicator;
            UpdateIndicator();
        }
    }
}
