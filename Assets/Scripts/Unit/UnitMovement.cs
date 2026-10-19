using UnityEngine;
using UnityEngine.AI;
using TerraIncognita.Data;

namespace TerraIncognita.Unit
{
    public enum UnitCommand
    {
        Move,
        AttackMove,
        Attack,
        Stop,
        HoldPosition,
        Patrol
    }

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
        private Unit unit, lastTarget, currentTarget;

        // 폴백 이동 제어 변수
        private bool isFallbackMoving, patrolMovingToEnd;
        private Vector3 fallbackTargetPosition, lastDestination, commandDestination, patrolStart;
        private float nextPatrolPathRetryTime;
        private float nextPatrolDiagnosticTime;
        private UnitCommand currentCommand = UnitCommand.Stop;

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
            if (unit == null || unit.Data == null)
                return;

            // NavMeshAgent가 사용 불가능할 때 동작하는 실시간 직접 이동 폴백
            if (isFallbackMoving)
            {
                UpdateFallbackMovement();
            }
            if (agent != null && agent.isOnNavMesh)
            {
                SetAvoidancePriority();
            }
            if (currentTarget != null)
            {
                if (!IsTargetTrackable(currentTarget))
                {
                    LoseCurrentTarget();
                }
                else
                {
                    TrackCurrentTarget();
                    return;
                }
            }

            switch (currentCommand)
            {
                case UnitCommand.AttackMove:
                case UnitCommand.Stop:
                case UnitCommand.Patrol:
                    TryAcquireTarget(unit.Data.SenseRange);
                    break;
                case UnitCommand.HoldPosition:
                    AttackNearestTargetInRange();
                    break;
            }

            if (currentCommand == UnitCommand.Patrol)
                UpdatePatrol();
        }

        private void TryAcquireTarget(float range)
        {
            Unit closestEnemy = null;
            float closestDistance = range;
            var objects = Physics.OverlapSphere(transform.position, range);
            foreach (var obj in objects)
            {
                Unit otherUnit = obj.GetComponentInParent<Unit>();
                if (!IsEnemy(otherUnit))
                    continue;

                float distance = Vector3.Distance(transform.position, otherUnit.transform.position);
                if (distance <= closestDistance)
                {
                    closestDistance = distance;
                    closestEnemy = otherUnit;
                }
            }

            if (closestEnemy != currentTarget)
            {
                currentTarget = closestEnemy;
                if (currentTarget != null)
                {
                    Debug.Log(
                        $"[UnitMovement:{name}] Acquired {currentTarget.name} while {currentCommand}; " +
                        $"distance={closestDistance:F2}, senseRange={range:F2}.",
                        this);
                }
            }
        }

        private void AttackNearestTargetInRange()
        {
            Unit closestEnemy = null;
            float closestDistance = float.MaxValue;
            var objects = Physics.OverlapSphere(transform.position, unit.Data.AttackRange);
            foreach (var obj in objects)
            {
                Unit otherUnit = obj.GetComponentInParent<Unit>();
                if (!IsEnemy(otherUnit))
                    continue;

                float distance = Vector3.Distance(transform.position, otherUnit.transform.position);
                if (distance <= unit.Data.AttackRange && distance < closestDistance)
                {
                    closestDistance = distance;
                    closestEnemy = otherUnit;
                }
            }

            if (closestEnemy != null)
                AttackTarget(closestEnemy);
            else
                lastTarget = null;
        }

        private bool IsEnemy(Unit target)
        {
            return target != null && target != unit && target.isActiveAndEnabled &&
                   target.CompareTag("Enemy");
        }

        private bool IsTargetTrackable(Unit target)
        {
            return IsEnemy(target) &&
                   Vector3.Distance(transform.position, target.transform.position) <= unit.Data.TrackingRange;
        }

        private void TrackCurrentTarget()
        {
            float distance = Vector3.Distance(transform.position, currentTarget.transform.position);
            if (distance <= unit.Data.AttackRange)
            {
                if (IsMoving)
                    StopMovement();
                AttackTarget(currentTarget);
                return;
            }

            lastTarget = null;
            SetMovementDestination(currentTarget.transform.position);
        }

        private void LoseCurrentTarget()
        {
            Debug.Log(
                $"[UnitMovement:{name}] Lost target {currentTarget.name} at distance " +
                $"{Vector3.Distance(transform.position, currentTarget.transform.position):F2}; " +
                $"command={currentCommand}.",
                this);
            currentTarget = null;
            lastTarget = null;
            switch (currentCommand)
            {
                case UnitCommand.Attack:
                case UnitCommand.Stop:
                    StopMovement();
                    break;
                case UnitCommand.AttackMove:
                    SetMovementDestination(commandDestination, true);
                    break;
                case UnitCommand.Patrol:
                    SetMovementDestination(patrolMovingToEnd ? commandDestination : patrolStart, true);
                    break;
            }
        }

        private void UpdatePatrol()
        {
            if (isFallbackMoving)
            {
                LogPatrolState("using fallback movement");
                return;
            }

            Vector3 currentWaypoint = patrolMovingToEnd ? commandDestination : patrolStart;
            if (agent != null && agent.isOnNavMesh)
            {
                if (agent.pathPending)
                {
                    LogPatrolState($"path pending to {currentWaypoint}");
                    return;
                }

                float arrivalDistance = agent.stoppingDistance + 0.1f;
                if (agent.hasPath)
                {
                    if (agent.remainingDistance > arrivalDistance ||
                        agent.velocity.sqrMagnitude > 0.01f)
                    {
                        LogPatrolState($"following path to {currentWaypoint}");
                        return;
                    }
                }
                else if (HorizontalDistance(transform.position, currentWaypoint) > arrivalDistance)
                {
                    if (Time.time >= nextPatrolPathRetryTime)
                    {
                        LogPatrolState($"no path; retrying waypoint {currentWaypoint}");
                        SetMovementDestination(currentWaypoint, true);
                        nextPatrolPathRetryTime = Time.time + 1f;
                    }
                    return;
                }
            }
            else if (HorizontalDistance(transform.position, currentWaypoint) >
                     Mathf.Max(unit.Data.StoppingDistance, 0.2f))
            {
                LogPatrolState($"no NavMeshAgent; waiting for fallback arrival at {currentWaypoint}");
                return;
            }

            Vector3 previousWaypoint = currentWaypoint;
            patrolMovingToEnd = !patrolMovingToEnd;
            currentWaypoint = patrolMovingToEnd ? commandDestination : patrolStart;
            Debug.Log(
                $"[UnitMovement:{name}] Patrol waypoint reached at {transform.position}; " +
                $"switching {previousWaypoint} -> {currentWaypoint}.",
                this);
            SetMovementDestination(currentWaypoint, true);
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }

        private void LogPatrolState(string state)
        {
            if (Time.time < nextPatrolDiagnosticTime)
                return;

            nextPatrolDiagnosticTime = Time.time + 1f;
            if (agent != null)
            {
                Debug.Log(
                    $"[UnitMovement:{name}] Patrol {state}; position={transform.position}, " +
                    $"destination={agent.destination}, onNavMesh={agent.isOnNavMesh}, " +
                    $"isStopped={agent.isStopped}, hasPath={agent.hasPath}, " +
                    $"pathPending={agent.pathPending}, pathStatus={agent.pathStatus}, " +
                    $"remainingDistance={agent.remainingDistance:F2}, " +
                    $"stoppingDistance={agent.stoppingDistance:F2}, velocity={agent.velocity}.",
                    this);
            }
            else
            {
                Debug.Log(
                    $"[UnitMovement:{name}] Patrol {state}; position={transform.position}, " +
                    $"fallbackMoving={isFallbackMoving}, fallbackDestination={fallbackTargetPosition}.",
                    this);
            }
        }

        protected void SetAvoidancePriority()
        {
            if (agent.remainingDistance <= agent.stoppingDistance)
            {
                agent.avoidancePriority = navPriorityDuringStop;
            }
            else
            {
                agent.avoidancePriority = navPriority;
            }
        }

        protected virtual void AttackTarget(Unit target)
        {
            if (target == null || lastTarget == target)
            {
                return;
            }

            lastTarget = target;
            // 공격 로직 구현
            if (unit != null && unit.Data != null && target.Data != null)
                Debug.Log($"{unit.Data.UnitName} is attacking {target.Data.UnitName}");
        }

        public void IssueAttackCommand(Unit target)
        {
            if (!IsEnemy(target))
            {
                Stop();
                return;
            }

            currentCommand = UnitCommand.Attack;
            currentTarget = target;
            lastTarget = null;
            TrackCurrentTarget();
        }

        public void IssueCommand(UnitCommand command, Vector3 destination = default)
        {
            Debug.Log(
                $"[UnitMovement:{name}] Command {command}; from={transform.position}, " +
                $"destination={destination}, agentOnNavMesh={agent != null && agent.isOnNavMesh}.",
                this);

            currentCommand = command;
            currentTarget = null;
            lastTarget = null;
            commandDestination = destination;

            switch (command)
            {
                case UnitCommand.Move:
                case UnitCommand.AttackMove:
                    SetMovementDestination(destination, true);
                    break;
                case UnitCommand.Patrol:
                    patrolStart = transform.position;
                    patrolMovingToEnd = true;
                    nextPatrolPathRetryTime = Time.time + 1f;
                    nextPatrolDiagnosticTime = Time.time;
                    Debug.Log(
                        $"[UnitMovement:{name}] Patrol initialized; start={patrolStart}, end={destination}.",
                        this);
                    SetMovementDestination(destination, true);
                    break;
                case UnitCommand.Stop:
                case UnitCommand.HoldPosition:
                case UnitCommand.Attack:
                    StopMovement();
                    break;
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
            IssueCommand(UnitCommand.Move, destination);
            return true;
        }

        private void SetMovementDestination(Vector3 destination, bool force = false)
        {
            if (!force && Vector3.Distance(lastDestination, destination) <= 0.5f && IsMoving)
                return;

            lastDestination = destination;
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
                    bool sampledDestination = NavMesh.SamplePosition(
                        destination, out NavMeshHit destHit, 3.0f, NavMesh.AllAreas);
                    if (sampledDestination)
                    {
                        targetPos = destHit.position;
                    }

                    agent.isStopped = false;
                    bool success = agent.SetDestination(targetPos);
                    Debug.Log(
                        $"[UnitMovement:{name}] SetDestination requested={destination}, " +
                        $"sampled={sampledDestination}, actual={targetPos}, success={success}, " +
                        $"isStopped={agent.isStopped}, hasPath={agent.hasPath}, " +
                        $"pathPending={agent.pathPending}, pathStatus={agent.pathStatus}.",
                        this);
                    if (success)
                    {
                        agent.avoidancePriority = navPriority;
                        isFallbackMoving = false;
                        return;
                    }
                }
                else
                {
                    Debug.LogWarning(
                        $"[UnitMovement:{name}] Cannot set destination: NavMeshAgent is not on a NavMesh.",
                        this);
                }
            }

            // 2. NavMesh가 없는 환경에서의 안전한 직접 이동 폴백
            fallbackTargetPosition = destination;
            fallbackTargetPosition.y = transform.position.y; // 동일 지면 높이 유지
            isFallbackMoving = true;
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
                if (agent != null && agent.isOnNavMesh)
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
            if (agent != null && agent.isOnNavMesh)
                agent.Move(moveDir * step);
            else
                transform.position += moveDir * step;
        }

        /// <summary>
        /// 유닛의 현재 이동을 즉시 정지합니다.
        /// </summary>
        public void Stop()
        {
            currentCommand = UnitCommand.Stop;
            currentTarget = null;
            lastTarget = null;
            StopMovement();
        }

        private void StopMovement()
        {
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            isFallbackMoving = false;
            if (agent != null && agent.isOnNavMesh)
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
