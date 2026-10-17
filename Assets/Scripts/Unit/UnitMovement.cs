using UnityEngine;
using UnityEngine.AI;
using TerraIncognita.Data;

namespace TerraIncognita.Unit
{
    /// <summary>
    /// NavMeshAgent를 기반으로 실시간 이동 및 회전을 제어합니다.
    /// NavMesh가 준비되지 않은 환경에서도 동작을 보장하는 부드러운 실시간 폴백(Fallback) 이동을 내장합니다.
    /// </summary>
    [RequireComponent(typeof(Unit))]
    [DisallowMultipleComponent]
    public class UnitMovement : MonoBehaviour
    {
        [SerializeField] private int navPriority = 50;
        [SerializeField] private int navPriorityDuringStop = 100;
        private NavMeshAgent agent;
        private Unit unit;

        // 폴백 이동 제어 변수
        private bool isFallbackMoving;
        private Vector3 fallbackTargetPosition;

        public bool IsMoving
        {
            get
            {
                if (agent != null && agent.isOnNavMesh && agent.hasPath)
                {
                    return agent.remainingDistance > agent.stoppingDistance;
                }
                return isFallbackMoving;
            }
        }

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            unit = GetComponent<Unit>();
        }

        private void Start()
        {
            ApplyUnitDataSettings();
        }

        private void Update()
        {
            // NavMeshAgent가 사용 불가능할 때 동작하는 실시간 직접 이동 폴백
            if (isFallbackMoving)
            {
                UpdateFallbackMovement();
            }
            if (agent != null && agent.isOnNavMesh && agent.hasPath)
            {
                if (agent.remainingDistance <= agent.stoppingDistance)
                {
                    Debug.Log("Unit has reached destination.");
                    agent.avoidancePriority = navPriorityDuringStop;
                }
                else
                {
                    agent.avoidancePriority = navPriority;
                }
            }
        }

        /// <summary>
        /// UnitData의 설정값을 NavMeshAgent 및 이동 파라미터에 적용합니다.
        /// </summary>
        public void ApplyUnitDataSettings()
        {
            if (unit == null || unit.Data == null) return;

            UnitData data = unit.Data;
            if (agent != null)
            {
                agent.speed = data.MoveSpeed;
                agent.angularSpeed = data.AngularSpeed;
                agent.acceleration = data.Acceleration;
                agent.stoppingDistance = data.StoppingDistance;
                //agent.radius = data.Radius;
                agent.autoBraking = true;
                agent.updateRotation = true;
            }
        }

        /// <summary>
        /// 유닛에게 목표 지점으로의 이동 명령을 하달합니다.
        /// </summary>
        public bool MoveTo(Vector3 destination)
        {
            float speed = unit != null && unit.Data != null ? unit.Data.MoveSpeed : 4.5f;

            // 1. NavMeshAgent가 NavMesh 위에 유효하게 배치되어 있는지 확인
            if (agent != null)
            {
                if (!agent.isOnNavMesh)
                {
                    if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
                    {
                        agent.Warp(hit.position);
                    }
                }

                if (agent.isOnNavMesh)
                {
                    Vector3 targetPos = destination;
                    if (NavMesh.SamplePosition(destination, out NavMeshHit destHit, 3.0f, NavMesh.AllAreas))
                    {
                        targetPos = destHit.position;
                    }

                    agent.isStopped = false;
                    bool success = agent.SetDestination(targetPos);
                    if (success)
                    {
                        agent.avoidancePriority = navPriority;
                        isFallbackMoving = false;
                        return true;
                    }
                }
            }

            // 2. NavMesh가 없는 환경에서의 안전한 직접 이동 폴백
            fallbackTargetPosition = destination;
            fallbackTargetPosition.y = transform.position.y; // 동일 지면 높이 유지
            isFallbackMoving = true;
            return true;
        }

        private void UpdateFallbackMovement()
        {
            float speed = unit != null && unit.Data != null ? unit.Data.MoveSpeed : 4.5f;
            float angularSpeed = unit != null && unit.Data != null ? unit.Data.AngularSpeed : 720f;
            float stopDist = unit != null && unit.Data != null ? unit.Data.StoppingDistance : 0.2f;

            Vector3 currentPos = transform.position;
            Vector3 diff = fallbackTargetPosition - currentPos;
            diff.y = 0f; // 평면 이동

            float distance = diff.magnitude;
            if (distance <= stopDist)
            {
                isFallbackMoving = false;
                agent.avoidancePriority = navPriorityDuringStop;
                return;
            }

            // 진행 방향 회전
            Vector3 moveDir = diff / distance;
            Quaternion targetRotation = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, angularSpeed * Time.deltaTime);

            // 목적지로 전진
            float step = speed * Time.deltaTime;
            if (step > distance) step = distance;
            //transform.position += moveDir * step;
            agent.Move(moveDir * step);
        }

        /// <summary>
        /// 유닛의 현재 이동을 즉시 정지합니다.
        /// </summary>
        public void Stop()
        {
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            isFallbackMoving = false;
            agent.avoidancePriority = navPriorityDuringStop;
        }

        protected void OnCollisionStay(Collision collision)
        {
            if (isFallbackMoving)
            {
                collision.gameObject.TryGetComponent<Unit>(out Unit otherUnit);
                if (otherUnit != null)
                {
                    // 충돌 중인 유닛이 이동 중이면 서로 밀어내기
                    Vector3 pushDir = (transform.position - otherUnit.transform.position).normalized;
                    float pushStrength = 0.5f; // 밀어내는 힘 조절
                    transform.position += pushDir * pushStrength * Time.deltaTime;
                }
            }
        }
    }
}
