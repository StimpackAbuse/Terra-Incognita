using UnityEngine;
using Unity.AI.Navigation;

namespace TerraIncognita.Navigation
{
    /// <summary>
    /// 씬 시작 시 지면의 NavMesh를 자동으로 빌드하여 에디터 수동 베이크 없이도 유닛 이동을 즉시 가능하게 합니다.
    /// </summary>
    [RequireComponent(typeof(NavMeshSurface))]
    [DisallowMultipleComponent]
    public class NavMeshRuntimeBaker : MonoBehaviour
    {
        private NavMeshSurface navMeshSurface;

        private void Awake()
        {
            navMeshSurface = GetComponent<NavMeshSurface>();
            if (navMeshSurface != null)
            {
                navMeshSurface.collectObjects = CollectObjects.All;
                navMeshSurface.BuildNavMesh();
                Debug.Log("[NavMeshRuntimeBaker] 런타임 NavMesh가 성공적으로 빌드되었습니다.");
            }
        }
    }
}
