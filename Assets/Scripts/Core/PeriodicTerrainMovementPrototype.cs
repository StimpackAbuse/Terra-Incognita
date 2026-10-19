using TerraIncognita.Data;
using TerraIncognita.Unit;
using TerraIncognita.World;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using RTSUnit = TerraIncognita.Unit.Unit;

namespace TerraIncognita.Core
{
    [DisallowMultipleComponent]
    public sealed class PeriodicTerrainMovementPrototype : MonoBehaviour
    {
        [SerializeField] private PeriodicTerrainWorld terrainWorld;
        [SerializeField] private Camera prototypeCamera;
        [SerializeField] private UnitData testUnitData;
        [SerializeField] private Material testUnitMaterial;

        private NavMeshSurface navMeshSurface;
        private UnitMovement testUnitMovement;
        private Transform testUnitTransform;
        private NavMeshAgent testAgent;
        private AsyncOperation navMeshUpdate;
        private int bakedChunkVersion = -1;
        private string status = "Starting prototype...";

        private void Start()
        {
            if (terrainWorld == null)
                terrainWorld = FindFirstObjectByType<PeriodicTerrainWorld>();
            if (prototypeCamera == null)
                prototypeCamera = Camera.main;

            if (terrainWorld == null || prototypeCamera == null || testUnitData == null)
            {
                Debug.LogError(
                    "[PeriodicTerrainMovementPrototype] Assign the terrain world, camera, and test UnitData.",
                    this);
                enabled = false;
                return;
            }

            terrainWorld.RefreshChunks();
            Vector3 spawnPosition = FindSpawnPosition();
            CreateTestUnit(spawnPosition);
            terrainWorld.RegisterNavigationInterest(testUnitTransform);
            terrainWorld.RefreshChunks();

            navMeshSurface = terrainWorld.GetComponent<NavMeshSurface>();
            if (navMeshSurface == null)
                navMeshSurface = terrainWorld.gameObject.AddComponent<NavMeshSurface>();

            navMeshSurface.agentTypeID = testAgent.agentTypeID;
            navMeshSurface.collectObjects = CollectObjects.All;
            navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            navMeshSurface.layerMask = ~(1 << 2);
            navMeshSurface.ignoreNavMeshAgent = true;
            navMeshSurface.BuildNavMesh();
            bakedChunkVersion = terrainWorld.ChunkVersion;

            if (!NavMesh.SamplePosition(
                    spawnPosition + Vector3.up,
                    out NavMeshHit hit,
                    3f,
                    testAgent.areaMask))
            {
                Debug.LogError(
                    "[PeriodicTerrainMovementPrototype] Could not place the test unit on the land NavMesh.",
                    this);
                status = "NavMesh spawn failed; check the terrain and navigation console.";
                enabled = false;
                return;
            }

            if (!testAgent.Warp(hit.position))
            {
                Debug.LogError(
                    "[PeriodicTerrainMovementPrototype] NavMeshAgent could not warp to its spawn point.",
                    testAgent);
                status = "Unit placement failed; check the navigation console.";
                enabled = false;
                return;
            }

            status = "Right-click land to move. Press F to focus the unit.";
        }

        private void Update()
        {
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
                IssueMoveCommand();

            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
                FocusCameraOnUnit();

            if (navMeshUpdate == null || navMeshUpdate.isDone)
            {
                navMeshUpdate = null;
                if (terrainWorld.ChunkVersion != bakedChunkVersion)
                {
                    navMeshUpdate = navMeshSurface.UpdateNavMesh(navMeshSurface.navMeshData);
                    bakedChunkVersion = terrainWorld.ChunkVersion;
                }
            }
        }

        private Vector3 FindSpawnPosition()
        {
            Vector3 cameraPosition = prototypeCamera.transform.position;
            float bestDistance = float.MaxValue;
            Vector3 bestPosition = cameraPosition;
            bool foundLand = false;

            for (int z = -24; z <= 24; z += 2)
            {
                for (int x = -24; x <= 24; x += 2)
                {
                    float worldX = cameraPosition.x + x;
                    float worldZ = cameraPosition.z + z;
                    float height = terrainWorld.GetHeight(worldX, worldZ);
                    if (height <= terrainWorld.SeaLevel + 1f)
                        continue;

                    float distance = (x * x) + (z * z);
                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    bestPosition = new Vector3(worldX, height, worldZ);
                    foundLand = true;
                }
            }

            if (!foundLand)
                throw new System.InvalidOperationException(
                    "[PeriodicTerrainMovementPrototype] No suitable land found near the camera.");

            return bestPosition;
        }

        private void CreateTestUnit(Vector3 spawnPosition)
        {
            GameObject unitObject = new GameObject("Periodic Terrain Test Unit")
            {
                layer = 2
            };
            unitObject.transform.position = spawnPosition;

            testAgent = unitObject.AddComponent<NavMeshAgent>();
            testAgent.agentTypeID = 0;
            testAgent.radius = 0.45f;
            testAgent.height = 1.8f;
            testAgent.baseOffset = 0f;
            testAgent.autoRepath = true;

            RTSUnit unit = unitObject.AddComponent<RTSUnit>();
            unit.SetUnitData(testUnitData);
            testUnitMovement = unitObject.AddComponent<UnitMovement>();
            testUnitTransform = unitObject.transform;

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Test Unit Visual";
            visual.layer = 2;
            visual.transform.SetParent(unitObject.transform, false);
            visual.transform.localPosition = Vector3.up;
            visual.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
            Collider visualCollider = visual.GetComponent<Collider>();
            if (visualCollider != null)
                visualCollider.enabled = false;

            if (testUnitMaterial != null)
                visual.GetComponent<MeshRenderer>().sharedMaterial = testUnitMaterial;
        }

        private void IssueMoveCommand()
        {
            Ray ray = prototypeCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            bool hasLandHit = Physics.Raycast(
                ray,
                out RaycastHit hit,
                1000f,
                ~(1 << 2));
            float oceanDistance = ray.direction.y < -0.0001f
                ? (terrainWorld.SeaLevel - ray.origin.y) / ray.direction.y
                : -1f;
            if (!hasLandHit || (oceanDistance >= 0f && oceanDistance < hit.distance))
            {
                status = "No land clicked. Ocean is not walkable.";
                return;
            }

            if (!NavMesh.SamplePosition(
                    hit.point,
                    out NavMeshHit navHit,
                    1.5f,
                    testAgent.areaMask))
            {
                status = "No nearby land NavMesh. Ocean is not walkable.";
                return;
            }

            testUnitMovement.MoveTo(navHit.position);
            status = $"Moving to ({navHit.position.x:F1}, {navHit.position.z:F1}).";
        }

        private void FocusCameraOnUnit()
        {
            Vector3 cameraPosition = prototypeCamera.transform.position;
            cameraPosition.x = testUnitTransform.position.x;
            cameraPosition.z = testUnitTransform.position.z - 28f;
            prototypeCamera.transform.position = cameraPosition;
            status = "Camera focused on the test unit.";
        }

        private void LateUpdate()
        {
            if (testAgent != null && testAgent.isOnNavMesh && testAgent.hasPath)
            {
                status = $"Unit {testAgent.pathStatus}; remaining {testAgent.remainingDistance:F1} m.";
            }
        }

        private void OnGUI()
        {
            GUI.Label(
                new Rect(12f, 12f, 720f, 24f),
                $"Periodic terrain movement | Right-click: move | F: focus | {status}");
        }
    }
}
