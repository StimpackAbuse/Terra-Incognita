using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using TerraIncognita.Unit;
using System.Linq;

namespace TerraIncognita.Input
{
    /// <summary>
    /// 마우스 우클릭을 감지하여 지면 판정 후, 선택된 유닛들에게 목적지 분산 오프셋을 적용한 실시간 이동 명령을 발행합니다.
    /// 사용자가 선호하는 동심원(Concentric Rings) 분산 방식을 적용하며, 수학적 지면 교차 안전장치를 내장합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class CommandController : MonoBehaviour
    {
        [Header("참조 설정")]
        [Tooltip("렌더링을 담당하는 메인 카메라")]
        [SerializeField] private Camera mainCamera;

        [Header("지면 판정")]
        [Tooltip("이동 명령을 받을 수 있는 지면 레이어마스크")]
        [SerializeField] private LayerMask groundLayerMask = ~0;

        [Header("동심원 목적지 분산 설정")]
        [Tooltip("동심원 간격 및 유닛 간 최소 분산 거리 (m)")]
        [SerializeField] private float slotSpacing = 1.6f;
        [SerializeField] private GameObject destObj = null;

        [Header("이동 대형")]
        [Tooltip("이동 명령 시 기존 유닛 간 상대 간격에 적용할 비율")]
        [Range(0.5f, 1f)]
        [SerializeField] private float formationSpacingScale = 0.75f;

        [Header("대형 변형 감지")]
        [SerializeField] private float formationDeviationThreshold = 1.0f;
        [SerializeField] private float deformationCheckInterval = 0.25f;
        [SerializeField] private float deformationConfirmDuration = 0.75f;
        [SerializeField] private float reassignmentCooldown = 1.5f;

        private readonly List<NavMeshAgent> monitoredAgents = new();
        private readonly Dictionary<NavMeshAgent, Vector3> formationBaseline = new();

        private Vector3 monitoredDestination;
        private float nextFormationCheckTime;
        private float deformationStartTime = -1f;
        private float nextReassignmentTime;
        private bool monitoringFormation;

        private static readonly Plane s_GroundPlane = new Plane(Vector3.up, Vector3.zero);

        private void Awake()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }
        }

        private void Update()
        {
            HandleCommandInput();
            MonitorFormationDeformation();
        }

        private void BeginFormationMonitoring(List<NavMeshAgent> agents, Vector3 destination)
        {
            monitoredAgents.Clear();
            formationBaseline.Clear();

            foreach (var agent in agents)
            {
                if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                    continue;

                monitoredAgents.Add(agent);
                formationBaseline[agent] = agent.transform.position;
            }

            monitoredDestination = destination;
            deformationStartTime = -1f;
            nextFormationCheckTime = Time.time + deformationCheckInterval;
            nextReassignmentTime = Time.time + reassignmentCooldown;
            monitoringFormation = monitoredAgents.Count > 1;
        }

        private void MonitorFormationDeformation()
        {
            if (!monitoringFormation)
                return;

            if (Time.time < nextFormationCheckTime)
                return;

            nextFormationCheckTime =
                Time.time + deformationCheckInterval;

            monitoredAgents.RemoveAll(agent =>
                agent == null ||
                !agent.enabled ||
                !agent.isOnNavMesh);

            if (monitoredAgents.Count <= 1)
            {
                monitoringFormation = false;
                return;
            }

            Vector3 currentCenter = Vector3.zero;
            Vector3 baselineCenter = Vector3.zero;
            int count = 0;

            foreach (var agent in monitoredAgents)
            {
                if (!formationBaseline.TryGetValue(agent, out Vector3 baseline))
                    continue;

                currentCenter += agent.transform.position;
                baselineCenter += baseline;
                count++;
            }

            if (count <= 1)
                return;

            currentCenter /= count;
            baselineCenter /= count;

            Vector3 groupDisplacement = currentCenter - baselineCenter;

            float maxDeviation = 0f;

            foreach (var agent in monitoredAgents)
            {
                if (!formationBaseline.TryGetValue(agent, out Vector3 baseline))
                    continue;

                Vector3 expectedPosition = baseline + groupDisplacement;
                Vector3 actualPosition = agent.transform.position;

                float deviation = Vector3.Distance(
                    new Vector3(actualPosition.x, 0f, actualPosition.z),
                    new Vector3(expectedPosition.x, 0f, expectedPosition.z));

                maxDeviation = Mathf.Max(maxDeviation, deviation);
            }

            if (maxDeviation >= formationDeviationThreshold)
            {
                if (deformationStartTime < 0f)
                    deformationStartTime = Time.time;

                if (Time.time - deformationStartTime >=
                    deformationConfirmDuration &&
                    Time.time >= nextReassignmentTime)
                {
                    ReassignFormationFromCurrentPositions();
                }
            }
            else
            {
                deformationStartTime = -1f;
            }
        }

        private void ReassignFormationFromCurrentPositions()
        {
            var activeAgents = monitoredAgents
                .Where(agent =>
                    agent != null &&
                    agent.enabled &&
                    agent.isOnNavMesh)
                .ToList();

            if (activeAgents.Count <= 1)
            {
                monitoringFormation = false;
                return;
            }

            // FormationSlotGenerator는 현재 유닛 위치에서 대형 중심과
            // 각 유닛의 상대 오프셋을 다시 계산한다.
            var newSlots = FormationSlotGenerator.GenerateAndAssignSlots(
                monitoredDestination,
                activeAgents,
                candidateStep: 0.5f,
                sampleRadius: 0.5f,
                maxRings: 100);

            foreach (var pair in newSlots)
            {
                if (pair.Key == null || !pair.Key.isOnNavMesh)
                    continue;

                pair.Key.SetDestination(pair.Value);
            }

            // 새 대형을 기준으로 다시 감시한다.
            formationBaseline.Clear();

            foreach (var agent in activeAgents)
            {
                formationBaseline[agent] = agent.transform.position;
            }

            deformationStartTime = -1f;
            nextReassignmentTime = Time.time + reassignmentCooldown;
        }

        private void HandleCommandInput()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null && mouse == null) return;

            if (keyboard != null && keyboard.sKey.wasPressedThisFrame)
            {
                IssueImmediateCommand(UnitCommand.Stop);
            }
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame)
            {
                IssueImmediateCommand(UnitCommand.HoldPosition);
            }

            if (mouse == null || !mouse.rightButton.wasPressedThisFrame)
                return;

            UnitCommand command = keyboard != null && keyboard.aKey.isPressed
                ? UnitCommand.AttackMove
                : keyboard != null && keyboard.pKey.isPressed
                    ? UnitCommand.Patrol
                    : UnitCommand.Move;
            int selectedCount = SelectionManager.Instance != null
                ? SelectionManager.Instance.SelectedUnits.Count
                : 0;
            Debug.Log(
                $"[CommandController] Right-click command={command}; " +
                $"position={mouse.position.ReadValue()}, selectedUnits={selectedCount}, " +
                $"frame={Time.frameCount}.",
                this);

            TerraIncognita.Unit.Unit target = command == UnitCommand.Move
                ? GetEnemyAtScreenPosition(mouse.position.ReadValue())
                : null;
            if (target != null)
            {
                IssueAttackCommand(target);
                return;
            }

            ExecuteMoveCommand(mouse.position.ReadValue(), command);
        }

        private void IssueImmediateCommand(UnitCommand command)
        {
            if (SelectionManager.Instance == null)
                return;

            foreach (var unit in SelectionManager.Instance.SelectedUnits)
            {
                if (unit == null)
                    continue;

                UnitMovement movement = unit.GetComponent<UnitMovement>();
                if (movement != null)
                    movement.IssueCommand(command);
            }
        }

        private TerraIncognita.Unit.Unit GetEnemyAtScreenPosition(Vector2 screenPosition)
        {
            if (mainCamera == null)
                mainCamera = Camera.main;
            if (mainCamera == null)
                return null;

            Ray ray = mainCamera.ScreenPointToRay(screenPosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 1500f);
            TerraIncognita.Unit.Unit closestEnemy = null;
            float closestHitDistance = float.MaxValue;
            foreach (RaycastHit hit in hits)
            {
                TerraIncognita.Unit.Unit hitUnit =
                    hit.collider.GetComponentInParent<TerraIncognita.Unit.Unit>();
                if (hitUnit == null || !hitUnit.CompareTag("Enemy"))
                    continue;

                if (hit.distance < closestHitDistance)
                {
                    closestHitDistance = hit.distance;
                    closestEnemy = hitUnit;
                }
            }

            return closestEnemy;
        }

        private void IssueAttackCommand(TerraIncognita.Unit.Unit target)
        {
            if (SelectionManager.Instance == null)
                return;

            foreach (var unit in SelectionManager.Instance.SelectedUnits)
            {
                if (unit == null)
                    continue;

                UnitMovement movement = unit.GetComponent<UnitMovement>();
                if (movement != null)
                    movement.IssueAttackCommand(target);
            }
        }

        /// <summary>
        /// 화면 클릭 지점을 월드 지면 좌표로 변환하고 선택된 유닛들에게 분산 이동 명령을 전달합니다.
        /// </summary>
        private void ExecuteMoveCommand(Vector2 screenPosition, UnitCommand command)
        {
            if (SelectionManager.Instance == null) return;

            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera == null) return;
            }

            IReadOnlyList<TerraIncognita.Unit.Unit> selectedUnits = SelectionManager.Instance.SelectedUnits;
            if (selectedUnits == null || selectedUnits.Count == 0)
            {
                return;
            }

            Ray ray = mainCamera.ScreenPointToRay(screenPosition);
            Vector3 centerDestination = Vector3.zero;
            bool hitFound = false;

            // 1. RaycastAll을 통해 유닛 콜라이더를 뚫고 지면 콜라이더 탐색
            RaycastHit[] hits = Physics.RaycastAll(ray, 1500f, groundLayerMask);
            for (int i = 0; i < hits.Length; i++)
            {
                // 유닛 콜라이더가 아닌 지면 콜라이더 우선 채택
                if (hits[i].collider.GetComponentInParent<TerraIncognita.Unit.Unit>() == null)
                {
                    centerDestination = hits[i].point;
                    hitFound = true;
                    break;
                }
            }

            // 2. 콜라이더가 없거나 감지되지 않을 경우의 수학적 무한 평면(Y=0) 교차 안전장치
            if (!hitFound)
            {
                if (s_GroundPlane.Raycast(ray, out float enter))
                {
                    centerDestination = ray.GetPoint(enter);
                    hitFound = true;
                }
            }

            if (hitFound)
            {
                IssueDispersedMoveCommand(selectedUnits, centerDestination, command);
            }
        }

        private List<NavMeshAgent> GetSelectedAgents()
        {
            var agents = new List<NavMeshAgent>();

            if (SelectionManager.Instance == null) return agents;

            IReadOnlyList<TerraIncognita.Unit.Unit> selectedUnits = SelectionManager.Instance.SelectedUnits;
            if (selectedUnits == null || selectedUnits.Count == 0) return agents;

            foreach (var unit in selectedUnits)
            {
                if (unit == null) continue;

                NavMeshAgent agent = unit.GetComponent<NavMeshAgent>();
                if (agent != null && agent.isOnNavMesh)
                {
                    agents.Add(agent);
                }
            }
            return agents;
        }

        /// <summary>
        /// 동심원 슬롯 배치 알고리즘을 사용하여 유닛들에게 겹치지 않는 목적지를 할당합니다.
        /// </summary>
        private void IssueDispersedMoveCommand(
            IReadOnlyList<TerraIncognita.Unit.Unit> units,
            Vector3 center,
            UnitCommand command)
        {
            int unitCount = units.Count;
            if (unitCount == 0) return;

            if (unitCount == 1)
            {
                // 단일 유닛: 중심점으로 즉시 이동
                UnitMovement movement = units[0].GetComponent<UnitMovement>();
                if (movement != null)
                {
                    movement.IssueCommand(command, center);
                }
                return;
            }

            // 동심원 슬롯 좌표 목록 생성
            //List<Vector3> slots = GenerateConcentricSlots(center, unitCount, slotSpacing);
            List<NavMeshAgent> selectedAgents = GetSelectedAgents();

            Dictionary<NavMeshAgent, Vector3> slots =
                FormationSlotGenerator.GenerateAndAssignSlots(
                    center,
                    selectedAgents,
                    candidateStep: 0.5f,
                    sampleRadius: 0.5f,
                    maxRings: 100,
                    relativeSpacingScale: formationSpacingScale
                );

            foreach (var unit in units)
            {
                if (unit == null)
                    continue;

                UnitMovement movement = unit.GetComponent<UnitMovement>();
                if (movement == null)
                    continue;

                NavMeshAgent agent = unit.GetComponent<NavMeshAgent>();
                Vector3 destination = agent != null && slots.TryGetValue(agent, out Vector3 slot)
                    ? slot
                    : center;
                movement.IssueCommand(command, destination);
            }
        }

        /// <summary>
        /// 중심점을 기준으로 동심원 형태로 겹치지 않는 슬롯 위치들을 계산합니다.
        /// </summary>
        public static List<Vector3> GenerateConcentricSlots(Vector3 center, int count, float spacing)
        {
            List<Vector3> slots = new List<Vector3>(count);

            if (count <= 0 || spacing <= 0f)
                return slots;

            // NavMesh 위의 유효한 위치를 찾는 최대 반경
            const float sampleRadius = 1.0f;

            // 슬롯 후보 탐색 제한
            const int maxRings = 100;

            // 0번째 슬롯도 검증
            if (TryGetValidNavMeshPosition(
                center, sampleRadius, out Vector3 validCenter))
            {
                slots.Add(validCenter);
            }

            if (slots.Count >= count)
                return slots;

            for (int currentRing = 1;
                 currentRing <= maxRings && slots.Count < count;
                 currentRing++)
            {
                float currentRadius = currentRing * spacing;
                int slotsInRing = 6 * currentRing;
                float angleStep = 360f / slotsInRing;

                for (int i = 0;
                     i < slotsInRing && slots.Count < count;
                     i++)
                {
                    float angleRad = i * angleStep * Mathf.Deg2Rad;

                    Vector3 candidate = center + new Vector3(
                        Mathf.Cos(angleRad) * currentRadius,
                        0f,
                        Mathf.Sin(angleRad) * currentRadius
                    );

                    if (TryGetValidNavMeshPosition(
                        candidate, sampleRadius, out Vector3 validPosition))
                    {
                        slots.Add(validPosition);
                    }
                }
            }

            return slots;
        }

        private static bool TryGetValidNavMeshPosition(
            Vector3 candidate,
            float sampleRadius,
            out Vector3 validPosition)
        {
            if (NavMesh.SamplePosition(
                candidate,
                out NavMeshHit hit,
                sampleRadius,
                NavMesh.AllAreas))
            {
                validPosition = hit.position;
                return true;
            }

            validPosition = Vector3.zero;
            return false;
        }
    }
}
