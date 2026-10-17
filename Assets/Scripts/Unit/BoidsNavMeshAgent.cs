
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

[RequireComponent(typeof(NavMeshAgent))]
public class BoidsNavMeshAgent : MonoBehaviour
{
    [Header("Group")]
    public List<BoidsNavMeshAgent> neighbors;

    [Header("Neighborhood")]
    public float neighborRadius = 3f;
    public float separationRadius = 1.2f;

    [Header("Steering Weights")]
    [Range(0f, 10f)] public float separationWeight = 1.8f;
    [Range(0f, 2f)] public float alignmentWeight = 0.25f;
    [Range(0f, 2f)] public float cohesionWeight = 0.1f;

    [Header("Steering")]
    public float steeringStrength = 2f;
    public float minGoalSpeed = 0.5f;

    private NavMeshAgent agent;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    private void FixedUpdate()
    {
        UpdateNeighbors();
    }

    private void Update()
    {
        if (!agent.isOnNavMesh ||
            agent.pathPending ||
            !agent.hasPath ||
            agent.isStopped)
        {
            return;
        }

        Vector3 goalVelocity = agent.desiredVelocity;

        // 이미 도착한 경우 군집 힘으로 목표에서 밀려나지 않게 한다.
        if (agent.remainingDistance <=
            agent.stoppingDistance + 0.1f)
        {
            return;
        }

        Vector3 separation = Vector3.zero;
        Vector3 alignment = Vector3.zero;
        Vector3 center = Vector3.zero;

        int neighborCount = 0;
        int separationCount = 0;

        if (neighbors != null)
        {
            foreach (BoidsNavMeshAgent other in neighbors)
            {
                if (other == null || other == this)
                    continue;

                Vector3 offset =
                    transform.position - other.transform.position;

                // 수직으로 다른 층에 있는 유닛은 제외한다.
                if (Mathf.Abs(offset.y) > 1.5f)
                    continue;

                float distance = offset.magnitude;

                if (distance < 0.001f ||
                    distance > neighborRadius)
                    continue;

                neighborCount++;

                // 가까운 유닛으로부터 멀어지는 방향
                if (distance < separationRadius)
                {
                    float strength =
                        1f - distance / separationRadius;

                    separation +=
                        offset.normalized * strength;

                    separationCount++;
                }

                // 주변 유닛의 이동 방향
                if (other.agent != null &&
                    other.agent.isOnNavMesh)
                {
                    alignment += other.agent.velocity;
                }

                // 주변 유닛의 평균 위치
                center += other.transform.position;
            }
        }

        if (separationCount > 0)
            separation /= separationCount;

        if (neighborCount > 0)
        {
            alignment /= neighborCount;
            center /= neighborCount;
        }

        // 목표 경로 방향은 유지하면서 Boids 힘을 더한다.
        Vector3 steering = goalVelocity;

        steering += separation * separationWeight;
        steering += alignment.normalized * alignmentWeight;

        if (neighborCount > 0)
        {
            Vector3 cohesion =
                center - transform.position;

            cohesion.y = 0f;

            steering +=
                cohesion.normalized * cohesionWeight;
        }

        // 목적지 방향이 지나치게 약해지지 않도록 한다.
        if (goalVelocity.sqrMagnitude > 0.01f &&
            Vector3.Dot(steering, goalVelocity) < 0f)
        {
            steering += goalVelocity.normalized *
                        minGoalSpeed;
        }

        float maxSpeed = agent.speed;

        if (steering.sqrMagnitude > maxSpeed * maxSpeed)
            steering = steering.normalized * maxSpeed;

        // Agent의 기본 목적지 기반 이동을 직접 제어한다.
        agent.velocity = steering;
    }

    private void UpdateNeighbors()
    {
        neighbors.Clear();
        var colliders = Physics.OverlapSphere(transform.position, neighborRadius);
        foreach (var collider in colliders)
        {
            if (collider.gameObject == gameObject)
                continue;

            BoidsNavMeshAgent other = collider.GetComponent<BoidsNavMeshAgent>();
            if (other != null && !neighbors.Contains(other))
            {
                neighbors.Add(other);
            }
        }
    }
}
