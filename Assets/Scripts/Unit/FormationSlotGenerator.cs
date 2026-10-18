
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public static class FormationSlotGenerator
{
    private class UnitData
    {
        public NavMeshAgent agent;
        public FormationFootprint footprint;
        public Vector3 desiredPosition;
        public float yaw;

        public UnitData(
            NavMeshAgent agent,
            FormationFootprint footprint,
            Vector3 desiredPosition)
        {
            this.agent = agent;
            this.footprint = footprint;
            this.desiredPosition = desiredPosition;
            yaw = agent.transform.eulerAngles.y;
        }
    }

    private class SlotData
    {
        public Vector3 position;
        public float width;
        public float depth;
        public float clearance;
        public float yaw;

        public SlotData(
            Vector3 position,
            FormationFootprint footprint,
            float yaw)
        {
            this.position = position;
            width = footprint.width;
            depth = footprint.depth;
            clearance = footprint.clearance;
            this.yaw = yaw;
        }
    }

    public static Dictionary<NavMeshAgent, Vector3>
        GenerateAndAssignSlots(
            Vector3 center,
            List<NavMeshAgent> agents,
            float candidateStep = 0.5f,
            float sampleRadius = 0.5f,
            int maxRings = 100,
            float relativeSpacingScale = 1f)
    {
        var assignments =
            new Dictionary<NavMeshAgent, Vector3>();

        if (agents == null || agents.Count == 0)
            return assignments;

        candidateStep = Mathf.Max(0.1f, candidateStep);
        sampleRadius = Mathf.Max(0.05f, sampleRadius);
        maxRings = Mathf.Max(0, maxRings);
        relativeSpacingScale = Mathf.Clamp01(relativeSpacingScale);

        // 유효한 유닛만 수집한다.
        var validAgents = agents
            .Where(a =>
                a != null &&
                a.enabled &&
                a.isOnNavMesh &&
                a.GetComponent<FormationFootprint>() != null)
            .Distinct()
            .ToList();

        if (validAgents.Count == 0)
            return assignments;

        // 1. 현재 선택 부대의 중심을 계산한다.
        // XZ 평면 기준으로 상대 배치를 보존한다.
        Vector3 formationCenter = Vector3.zero;

        foreach (var agent in validAgents)
        {
            formationCenter += agent.transform.position;
        }

        formationCenter /= validAgents.Count;

        // 2. 각 유닛의 기존 상대 위치를 목적지에 평행 이동한다.
        var units = new List<UnitData>();

        foreach (var agent in validAgents)
        {
            var footprint =
                agent.GetComponent<FormationFootprint>();

            Vector3 offset =
                agent.transform.position - formationCenter;

            Vector3 desiredPosition = center + new Vector3(
                offset.x * relativeSpacingScale,
                0f,
                offset.z * relativeSpacingScale);

            units.Add(new UnitData(
                agent,
                footprint,
                desiredPosition));
        }

        // 3. 슬롯 후보를 목적지 주변에 생성한다.
        // 후보를 유닛별로 생성하지 않아 목적지 체계가 일관된다.
        var candidates = GenerateCandidates(
            center,
            validAgents.Count,
            candidateStep,
            sampleRadius,
            maxRings);

        // 4. 상대 위치를 최대한 보존하도록 배정한다.
        // 큰 유닛은 먼저 배정하여 공간을 확보하되,
        // 배정 우선순위는 해당 유닛의 이상적인 위치를 기준으로 한다.
        units = units
            .OrderByDescending(u => u.footprint.Area)
            .ToList();

        var placedSlots = new List<SlotData>();
        var usedPositions = new List<Vector3>();

        foreach (var unit in units)
        {
            // 이상적인 위치가 NavMesh 위에 있고 유효하면 우선 시도한다.
            if (TryAssignPosition(
                    unit,
                    unit.desiredPosition,
                    sampleRadius,
                    placedSlots,
                    usedPositions,
                    out Vector3 assignedPosition))
            {
                assignments.Add(unit.agent, assignedPosition);
                continue;
            }

            // 이상적인 위치가 유효하지 않을 때만
            // 해당 위치에서 가까운 후보 슬롯을 탐색한다.
            var orderedCandidates = candidates
                .OrderBy(p =>
                    HorizontalDistanceSqr(
                        p, unit.desiredPosition))
                .ToList();

            bool assigned = false;

            foreach (Vector3 candidate in orderedCandidates)
            {
                if (TryAssignPosition(
                        unit,
                        candidate,
                        sampleRadius,
                        placedSlots,
                        usedPositions,
                        out assignedPosition))
                {
                    assignments.Add(
                        unit.agent,
                        assignedPosition);

                    assigned = true;
                    break;
                }
            }

            // 유효한 슬롯이 없으면 해당 유닛은 배정하지 않는다.
        }

        return assignments;
    }

    private static bool TryAssignPosition(
        UnitData unit,
        Vector3 candidate,
        float sampleRadius,
        List<SlotData> placedSlots,
        List<Vector3> usedPositions,
        out Vector3 assignedPosition)
    {
        assignedPosition = Vector3.zero;

        // 다른 NavMesh 영역으로 잘못 투영되는 것을 방지하기 위해
        // 해당 유닛의 areaMask만 사용한다.
        if (!NavMesh.SamplePosition(
                candidate,
                out NavMeshHit hit,
                sampleRadius,
                unit.agent.areaMask))
        {
            return false;
        }

        Vector3 position = hit.position;

        if (IsPositionAlreadyUsed(position, usedPositions))
            return false;

        if (!IsFootprintOnNavMesh(
                position,
                unit.footprint,
                unit.yaw,
                unit.agent.areaMask))
        {
            return false;
        }

        if (OverlapsExistingSlots(
                position,
                unit.footprint,
                unit.yaw,
                placedSlots))
        {
            return false;
        }

        if (!HasCompletePath(unit.agent, position))
            return false;

        placedSlots.Add(new SlotData(
            position,
            unit.footprint,
            unit.yaw));

        usedPositions.Add(position);
        assignedPosition = position;

        return true;
    }

    private static List<Vector3> GenerateCandidates(
        Vector3 center,
        int unitCount,
        float spacing,
        float sampleRadius,
        int maxRings)
    {
        var candidates = new List<Vector3>();

        int maxCandidates = Mathf.Max(256, unitCount * 32);

        for (int ring = 0;
             ring <= maxRings &&
             candidates.Count < maxCandidates;
             ring++)
        {
            int countInRing = ring == 0 ? 1 : 6 * ring;
            float radius = ring * spacing;

            for (int i = 0;
                 i < countInRing &&
                 candidates.Count < maxCandidates;
                 i++)
            {
                float angle = ring == 0
                    ? 0f
                    : i * (360f / countInRing)
                        * Mathf.Deg2Rad;

                Vector3 candidate = center + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0f,
                    Mathf.Sin(angle) * radius);

                if (!NavMesh.SamplePosition(
                        candidate,
                        out NavMeshHit hit,
                        sampleRadius,
                        NavMesh.AllAreas))
                {
                    continue;
                }

                if (IsPositionAlreadyUsed(
                        hit.position, candidates))
                {
                    continue;
                }

                candidates.Add(hit.position);
            }
        }

        return candidates;
    }

    private static float HorizontalDistanceSqr(
        Vector3 a,
        Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;

        return dx * dx + dz * dz;
    }

    private static bool IsPositionAlreadyUsed(
        Vector3 position,
        List<Vector3> positions)
    {
        const float minDistanceSqr = 0.01f;

        foreach (Vector3 existing in positions)
        {
            if (HorizontalDistanceSqr(position, existing)
                < minDistanceSqr)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCompletePath(
        NavMeshAgent agent,
        Vector3 destination)
    {
        var path = new NavMeshPath();

        return agent.CalculatePath(destination, path)
            && path.status == NavMeshPathStatus.PathComplete;
    }

    private static bool IsFootprintOnNavMesh(
        Vector3 center,
        FormationFootprint footprint,
        float yaw,
        int areaMask)
    {
        float halfW = footprint.width * 0.5f;
        float halfD = footprint.depth * 0.5f;

        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 right = rotation * Vector3.right;
        Vector3 forward = rotation * Vector3.forward;

        Vector3[] points =
        {
            Vector3.zero,
            right * halfW + forward * halfD,
            right * halfW - forward * halfD,
            -right * halfW + forward * halfD,
            -right * halfW - forward * halfD,
            right * halfW,
            -right * halfW,
            forward * halfD,
            -forward * halfD
        };

        foreach (Vector3 offset in points)
        {
            Vector3 point = center + offset;

            if (!NavMesh.SamplePosition(
                    point,
                    out NavMeshHit hit,
                    footprint.sampleRadius,
                    areaMask))
            {
                return false;
            }

            if (Mathf.Abs(hit.position.y - center.y)
                > footprint.maxHeightDifference)
            {
                return false;
            }

            if (NavMesh.Raycast(
                    center,
                    hit.position,
                    out NavMeshHit edgeHit,
                    areaMask))
            {
                return false;
            }

            if (Vector3.Distance(point, hit.position)
                > footprint.sampleRadius)
            {
                return false;
            }
        }

        return true;
    }

    private static bool OverlapsExistingSlots(
        Vector3 position,
        FormationFootprint footprint,
        float yaw,
        List<SlotData> existingSlots)
    {
        foreach (var slot in existingSlots)
        {
            if (RectanglesOverlap(
                    position,
                    footprint.width,
                    footprint.depth,
                    yaw,
                    footprint.clearance,
                    slot.position,
                    slot.width,
                    slot.depth,
                    slot.yaw,
                    slot.clearance))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RectanglesOverlap(
        Vector3 posA,
        float widthA,
        float depthA,
        float yawA,
        float clearanceA,
        Vector3 posB,
        float widthB,
        float depthB,
        float yawB,
        float clearanceB)
    {
        float halfWA = widthA * 0.5f + clearanceA * 0.5f;
        float halfDA = depthA * 0.5f + clearanceA * 0.5f;
        float halfWB = widthB * 0.5f + clearanceB * 0.5f;
        float halfDB = depthB * 0.5f + clearanceB * 0.5f;

        Quaternion rotA = Quaternion.Euler(0f, yawA, 0f);
        Quaternion rotB = Quaternion.Euler(0f, yawB, 0f);

        Vector3 rightA = rotA * Vector3.right;
        Vector3 forwardA = rotA * Vector3.forward;
        Vector3 rightB = rotB * Vector3.right;
        Vector3 forwardB = rotB * Vector3.forward;

        Vector3 delta = posB - posA;

        Vector3[] axes =
        {
            rightA, forwardA, rightB, forwardB
        };

        foreach (Vector3 axis in axes)
        {
            float distance = Mathf.Abs(Vector3.Dot(delta, axis));

            float radiusA =
                halfWA * Mathf.Abs(Vector3.Dot(rightA, axis)) +
                halfDA * Mathf.Abs(Vector3.Dot(forwardA, axis));

            float radiusB =
                halfWB * Mathf.Abs(Vector3.Dot(rightB, axis)) +
                halfDB * Mathf.Abs(Vector3.Dot(forwardB, axis));

            if (distance >= radiusA + radiusB)
                return false;
        }

        return true;
    }
}
