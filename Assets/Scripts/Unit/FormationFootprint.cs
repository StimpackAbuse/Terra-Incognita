
using UnityEngine;

public class FormationFootprint : MonoBehaviour
{
    [Header("Footprint")]
    [Min(0.1f)]
    public float width = 1f;

    [Min(0.1f)]
    public float depth = 1f;

    [Min(0f)]
    public float clearance = 0.2f;

    [Header("NavMesh Validation")]
    [Tooltip("NavMesh 표면을 확인할 때 허용하는 수평 오차")]
    [Min(0.01f)]
    public float sampleRadius = 0.15f;

    [Tooltip("샘플 지점과 NavMesh 사이에 허용하는 높이 차이")]
    [Min(0.01f)]
    public float maxHeightDifference = 0.3f;

    public Vector2 Size => new Vector2(width, depth);

    public float Area => width * depth;
}
