
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class RTSGroupMovement : MonoBehaviour
{
    [Header("Formation")]
    [SerializeField] private float slotSpacing = 1.2f;
    [SerializeField] private float formationRadius = 8f;
    [SerializeField] private float sampleRadius = 0.35f;

    [Header("Navigation")]
    [SerializeField] private float destinationSampleRadius = 1f;
    [SerializeField] private int maxCandidateSlots = 400;

    private readonly List<Vector3> slots = new();
    private readonly List<Vector3> pathCorners = new();

    /// <summary>
    /// 선택한 유닛 그룹에 이동 명령을 내린다.
    /// </summary>
    public void MoveGroup(
        IReadOnlyList<NavMeshAgent> agents,
        Vector3 requestedTarget)
    {
        if (agents == null || agents.Count == 0)
            return;

        // 목표점 자체를 NavMesh 위의 유효 위치로 변환한다.
        if (!NavMesh.SamplePosition(
                requestedTarget,
                out NavMeshHit targetHit,
                destinationSampleRadius,
                NavMesh.AllAreas))
        {
            Debug.LogWarning("이동 목표 주변에 NavMesh가 없습니다.");
            return;
        }

        Vector3 target = targetHit.position;

        // 대표 Agent의 NavMesh Area 설정을 기준으로 처리한다.
        NavMeshAgent referenceAgent = null;

        foreach (NavMeshAgent agent in agents)
        {
            if (agent != null && agent.isOnNavMesh)
            {
                referenceAgent = agent;
                break;
            }
        }

        if (referenceAgent == null)
            return;

        GenerateSlots(target, referenceAgent.areaMask);

        // 유닛마다 모든 슬롯의 경로를 검사한다.
        // 경로 비용이 가장 낮은 유닛-슬롯 조합부터 배정한다.
        AssignSlots(agents);
    }

    private void GenerateSlots(Vector3 target, int areaMask)
    {
        slots.Clear();

        // 중앙부터 바깥으로 퍼지는 육각 격자 후보를 만든다.
        float rowStep = slotSpacing * 0.8660254f;

        int maxRows = Mathf.CeilToInt(
            formationRadius / rowStep);

        for (int row = -maxRows;
             row <= maxRows && slots.Count < maxCandidateSlots;
             row++)
        {
            for (int col = -maxRows;
                 col <= maxRows && slots.Count < maxCandidateSlots;
                 col++)
            {
                float x = (col + row * 0.5f) * slotSpacing;
                float z = row * rowStep;

                if (x * x + z * z >
                    formationRadius * formationRadius)
                    continue;

                Vector3 candidate =
                    target + new Vector3(x, 0f, z);

                // 작은 반경으로만 스냅해 다른 층으로의
                // 잘못된 투영 가능성을 줄인다.
                if (!NavMesh.SamplePosition(
                        candidate,
                        out NavMeshHit hit,
                        sampleRadius,
                        areaMask))
                    continue;

                // 지형의 서로 다른 높이에 있는 슬롯을
                // 무조건 같은 평면 위치로 취급하지 않는다.
                bool duplicate = false;

                foreach (Vector3 existing in slots)
                {
                    Vector2 a = new(hit.position.x,
                                    hit.position.z);
                    Vector2 b = new(existing.x, existing.z);

                    if (Vector2.Distance(a, b) <
                        slotSpacing * 0.7f)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate)
                    continue;

                // 실제 도달 가능성은 Agent별 경로 검사에서
                // 확인하므로 여기서는 후보만 저장한다.
                slots.Add(hit.position);
            }
        }
    }

    private void AssignSlots(
        IReadOnlyList<NavMeshAgent> agents)
    {
        // 유닛-슬롯 모든 조합을 평가한 후
        // 경로 비용이 낮은 조합부터 배정한다.
        List<Assignment> candidates = new();

        for (int ai = 0; ai < agents.Count; ai++)
        {
            NavMeshAgent agent = agents[ai];

            if (agent == null || !agent.isOnNavMesh)
                continue;

            NavMeshPath path = new NavMeshPath();

            for (int si = 0; si < slots.Count; si++)
            {
                if (!agent.CalculatePath(slots[si], path))
                    continue;

                if (path.status != NavMeshPathStatus.PathComplete)
                    continue;

                float cost = GetPathLength(path);

                candidates.Add(new Assignment
                {
                    agentIndex = ai,
                    slotIndex = si,
                    cost = cost
                });
            }
        }

        candidates.Sort((a, b) => a.cost.CompareTo(b.cost));

        HashSet<int> assignedAgents = new();
        HashSet<int> usedSlots = new();

        foreach (Assignment candidate in candidates)
        {
            if (assignedAgents.Contains(candidate.agentIndex) ||
                usedSlots.Contains(candidate.slotIndex))
                continue;

            NavMeshAgent agent = agents[candidate.agentIndex];

            if (agent == null || !agent.isOnNavMesh)
                continue;

            agent.SetDestination(slots[candidate.slotIndex]);

            assignedAgents.Add(candidate.agentIndex);
            usedSlots.Add(candidate.slotIndex);
        }

        // 슬롯이 부족하거나 경로가 없는 유닛은
        // 임의로 원래 목표로 보내지 않고, 추후 재시도한다.
        // 원래 목표로 보내면 군집 충돌이 재발할 수 있다.
    }

    private float GetPathLength(NavMeshPath path)
    {
        Vector3[] corners = path.corners;
        float length = 0f;

        for (int i = 1; i < corners.Length; i++)
            length += Vector3.Distance(
                corners[i - 1], corners[i]);

        return length;
    }

    private struct Assignment
    {
        public int agentIndex;
        public int slotIndex;
        public float cost;
    }
}
