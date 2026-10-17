using UnityEngine;

namespace TerraIncognita.Data
{
    /// <summary>
    /// 유닛의 정적 설정 데이터를 정의하는 ScriptableObject입니다.
    /// GDD 28장의 '데이터와 런타임 상태 분리' 원칙을 준수합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewUnitData", menuName = "Terra Incognita/Unit Data")]
    public class UnitData : ScriptableObject
    {
        [Header("기본 정보")]
        [Tooltip("고유 유닛 식별자")]
        [SerializeField] private string unitId = "unit_infantry";

        [Tooltip("인게임 표시 이름")]
        [SerializeField] private string unitName = "보병";

        [Tooltip("동일 계열 판정 ID (Ctrl+좌클릭으로 동일 계열 선택 시 사용)")]
        [SerializeField] private string seriesId = "infantry_series";

        [Header("이동 및 네비게이션 스펙")]
        [Tooltip("초당 이동 속도 (m/s)")]
        [SerializeField] private float moveSpeed = 4.5f;

        [Tooltip("초당 회전 속도 (deg/s)")]
        [SerializeField] private float angularSpeed = 720.0f;

        [Tooltip("이동 가속도")]
        [SerializeField] private float acceleration = 16.0f;

        [Tooltip("목적지 도달 판정 거리")]
        [SerializeField] private float stoppingDistance = 0.2f;

        [Tooltip("유닛 충돌 및 점유 반경")]
        [SerializeField] private float radius = 0.5f;

        // 속성 접근자 (Read-only)
        public string UnitId => unitId;
        public string UnitName => unitName;
        public string SeriesId => seriesId;
        public float MoveSpeed => moveSpeed;
        public float AngularSpeed => angularSpeed;
        public float Acceleration => acceleration;
        public float StoppingDistance => stoppingDistance;
        public float Radius => radius;
    }
}
