
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public static class FormationSlotGenerator
{
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
            int maxRings = 100)
    {
        var assignments =
            new Dictionary<NavMeshAgent, Vector3>();

        if (agents == null || agents.Count == 0)
            return assignments;

        candidateStep = Mathf.Max(0.1f, candidateStep);
        sampleRadius = Mathf.Max(0.05f, sampleRadius);

        var units = agents
            .Where(a =>
                a != null &&
                a.enabled &&
                a.isOnNavMesh &&
                a.GetComponent<FormationFootprint>() != null)
            .Select(a => new
            {
                agent = a,
                footprint = a.GetComponent<FormationFootprint>()
            })
            .OrderByDescending(x => x.footprint.Area)
            .ToList();

        var placedSlots = new List<SlotData>();

        foreach (var unit in units)
        {
            bool assigned = false;

            // 슬롯 방향은 우선 유닛의 현재 방향을 유지한다.
            float yaw = unit.agent.transform.eulerAngles.y;

            for (int ring = 0;
                 ring <= maxRings && !assigned;
                 ring++)
            {
                int candidatesInRing =
                    ring == 0 ? 1 : 6 * ring;

                float radius = ring * candidateStep;

                for (int i = 0;
                     i < candidatesInRing;
                     i++)
                {
                    float angle = ring == 0
                        ? 0f
                        : i * (360f / candidatesInRing)
                            * Mathf.Deg2Rad;

                    Vector3 candidate =
                        center + new Vector3(
                            Mathf.Cos(angle) * radius,
                            0f,
                            Mathf.Sin(angle) * radius);

                    if (!NavMesh.SamplePosition(
                            candidate,
                            out NavMeshHit centerHit,
                            sampleRadius,
                            unit.agent.areaMask))
                    {
                        continue;
                    }

                    Vector3 position = centerHit.position;

                    // 전체 점유 영역이 NavMesh에 놓일 수 있는지 검사
                    if (!IsFootprintOnNavMesh(
                            position,
                            unit.footprint,
                            yaw,
                            unit.agent.areaMask))
                    {
                        continue;
                    }

                    // 기존 슬롯과 회전된 직사각형이 겹치는지 검사
                    if (OverlapsExistingSlots(
                            position,
                            unit.footprint,
                            yaw,
                            placedSlots))
                    {
                        continue;
                    }

                    if (!HasCompletePath(
                            unit.agent,
                            position))
                    {
                        continue;
                    }

                    placedSlots.Add(new SlotData(
                        position,
                        unit.footprint,
                        yaw));

                    assignments.Add(unit.agent, position);
                    assigned = true;
                    break;
                }
            }
        }

        return assignments;
    }

    private static bool HasCompletePath(
        NavMeshAgent agent,
        Vector3 destination)
    {
        var path = new NavMeshPath();

        return agent.CalculatePath(destination, path)
            && path.status == NavMeshPathStatus.PathComplete;
    }

    // 중심, 네 모서리, 네 변의 중점을 검사한다.
    private static bool IsFootprintOnNavMesh(
        Vector3 center,
        FormationFootprint footprint,
        float yaw,
        int areaMask)
    {
        float halfW = footprint.width * 0.5f;
        float halfD = footprint.depth * 0.5f;

        Vector3 right =
            Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

        Vector3 forward =
            Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        Vector3[] localPoints =
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

        foreach (Vector3 local in localPoints)
        {
            Vector3 point = center + local;

            if (!NavMesh.SamplePosition(
                    point,
                    out NavMeshHit sampleHit,
                    footprint.sampleRadius,
                    areaMask))
            {
                return false;
            }

            // 다른 층이나 높은 경사면의 NavMesh를 잘못 선택하는 것을 완화
            if (Mathf.Abs(sampleHit.position.y - center.y)
                > footprint.maxHeightDifference)
            {
                return false;
            }

            // 중심에서 샘플 위치까지 NavMesh 경계에 막히는지 검사
            if (NavMesh.Raycast(
                    center,
                    sampleHit.position,
                    out NavMeshHit edgeHit,
                    areaMask))
            {
                return false;
            }

            // SamplePosition이 원래 지점에서 지나치게 먼 위치를
            // 반환하는 상황도 방지한다.
            if (Vector3.Distance(point, sampleHit.position)
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

    // Separating Axis Theorem (SAT)
    // 지면 XZ 평면에서 회전된 직사각형 두 개를 검사한다.
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
        // clearance를 각 직사각형의 반폭/반깊이에 반영
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
            rightA, forwardA,
            rightB, forwardB
        };

        foreach (Vector3 axis in axes)
        {
            float distance = Mathf.Abs(
                Vector3.Dot(delta, axis));

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
