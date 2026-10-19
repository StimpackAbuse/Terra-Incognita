using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace TerraIncognita.World
{
    [DisallowMultipleComponent]
    public sealed class PeriodicTerrainWorld : MonoBehaviour
    {
        [Header("World")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private float worldWidth = 256f;
        [SerializeField] private int seed = 13579;
        [SerializeField] private float seaLevel = 0f;
        [SerializeField] private float heightOffset = 4f;
        [SerializeField] private float heightAmplitude = 18f;

        [Header("Periodic Perlin Noise")]
        [SerializeField] private int baseCycles = 3;
        [SerializeField, Range(1, 8)] private int octaveCount = 4;
        [SerializeField, Range(0f, 1f)] private float persistence = 0.5f;

        [Header("Terrain Chunks")]
        [SerializeField] private float chunkSize = 32f;
        [SerializeField, Range(8, 128)] private int chunkResolution = 32;
        [SerializeField, Min(0)] private int extraChunkMargin = 1;
        [SerializeField] private bool createTerrainColliders = true;
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private Material oceanMaterial;

        private readonly Dictionary<Vector2Int, PeriodicTerrainChunk> chunks = new();
        private readonly HashSet<Vector2Int> requiredChunks = new();
        private readonly Stack<PeriodicTerrainChunk> chunkPool = new();
        private PeriodicTerrainNoise noise;
        private Transform chunkRoot;
        private Transform navigationInterest;
        private int chunkVersion;
        private float nextWrapDiagnosticTime;

        public float WorldWidth => worldWidth;
        public float SeaLevel => seaLevel;
        public float ChunkSize => chunkSize;
        public int ActiveChunkCount => chunks.Count;
        public int PooledChunkCount => chunkPool.Count;
        public int ChunkVersion => chunkVersion;

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            if (targetCamera == null)
            {
                Debug.LogError(
                    "[PeriodicTerrainWorld] Assign a camera or tag one as MainCamera.",
                    this);
                enabled = false;
                return;
            }

            if (terrainMaterial == null || oceanMaterial == null)
            {
                Debug.LogError(
                    "[PeriodicTerrainWorld] Terrain and ocean materials must be assigned.",
                    this);
                enabled = false;
                return;
            }

            worldWidth = Mathf.Max(1f, worldWidth);
            chunkSize = Mathf.Max(1f, chunkSize);
            chunkResolution = Mathf.Max(2, chunkResolution);
            baseCycles = Mathf.Max(1, baseCycles);
            octaveCount = Mathf.Clamp(octaveCount, 1, 8);
            noise = new PeriodicTerrainNoise(
                seed,
                worldWidth,
                baseCycles,
                octaveCount,
                persistence,
                heightOffset,
                heightAmplitude,
                seaLevel);

            GameObject root = new GameObject("Generated Terrain Chunks");
            chunkRoot = root.transform;
            chunkRoot.SetParent(transform, false);
        }

        private void LateUpdate()
        {
            if (targetCamera != null)
                UpdateVisibleChunks();
        }

        public void RegisterNavigationInterest(Transform interest)
        {
            navigationInterest = interest;
        }

        public void RefreshChunks()
        {
            if (targetCamera != null)
                UpdateVisibleChunks();
        }

        public float GetHeight(float worldX, float worldZ)
        {
            EnsureNoiseInitialized();
            return noise.GetHeight(worldX, worldZ);
        }

        public bool IsOcean(float worldX, float worldZ)
        {
            return GetHeight(worldX, worldZ) <= seaLevel;
        }

        public Vector3 GetNearestWrappedPosition(Vector3 origin, Vector3 destination)
        {
            float halfWidth = worldWidth * 0.5f;
            float deltaX = Mathf.Repeat(
                destination.x - origin.x + halfWidth,
                worldWidth) - halfWidth;
            destination.x = origin.x + deltaX;
            return destination;
        }

        public bool TryWrapPosition(ref Vector3 position)
        {
            float halfWidth = worldWidth * 0.5f;
            if (position.x >= halfWidth)
                position.x -= worldWidth;
            else if (position.x < -halfWidth)
                position.x += worldWidth;
            else
                return false;

            return true;
        }

        public bool TryWrapAgent(NavMeshAgent agent)
        {
            if (agent == null || !agent.enabled)
                return false;

            Vector3 wrappedPosition = agent.transform.position;
            if (!TryWrapPosition(ref wrappedPosition))
                return false;

            if (!NavMesh.SamplePosition(
                    wrappedPosition,
                    out NavMeshHit hit,
                    Mathf.Max(2f, agent.radius * 2f),
                    agent.areaMask))
            {
                LogWrapDiagnostic(
                    $"Could not wrap {agent.name}: no nearby land NavMesh.",
                    agent);
                return false;
            }

            Vector3 agentPosition = hit.position + Vector3.up * agent.baseOffset;
            if (!agent.Warp(agentPosition))
            {
                LogWrapDiagnostic($"NavMeshAgent.Warp failed for {agent.name}.", agent);
                return false;
            }

            return true;
        }

        private void LogWrapDiagnostic(string message, NavMeshAgent agent)
        {
            if (Time.time < nextWrapDiagnosticTime)
                return;

            nextWrapDiagnosticTime = Time.time + 1f;
            Debug.LogWarning($"[PeriodicTerrainWorld] {message}", agent);
        }

        private void UpdateVisibleChunks()
        {
            float viewDown = Mathf.Abs(Vector3.Dot(targetCamera.transform.forward, Vector3.down));
            float halfHeight = targetCamera.orthographic
                ? targetCamera.orthographicSize
                : targetCamera.transform.position.y * 0.5f;
            float halfDepth = halfHeight / Mathf.Max(0.25f, viewDown);
            float halfWidth = halfHeight * targetCamera.aspect + halfDepth *
                Mathf.Abs(targetCamera.transform.forward.x);
            Vector3 cameraPosition = targetCamera.transform.position;

            requiredChunks.Clear();
            AddCameraChunks(cameraPosition.x, cameraPosition.z, halfWidth, halfDepth);
            AddCameraChunks(cameraPosition.x - worldWidth, cameraPosition.z, halfWidth, halfDepth);
            AddCameraChunks(cameraPosition.x + worldWidth, cameraPosition.z, halfWidth, halfDepth);

            if (navigationInterest != null)
            {
                float interestRadius = chunkSize * (extraChunkMargin + 2);
                AddInterestChunks(navigationInterest.position, interestRadius);
                AddInterestChunks(
                    navigationInterest.position + Vector3.left * worldWidth,
                    interestRadius);
                AddInterestChunks(
                    navigationInterest.position + Vector3.right * worldWidth,
                    interestRadius);
            }

            bool chunksChanged = false;
            foreach (Vector2Int key in requiredChunks)
            {
                if (chunks.ContainsKey(key))
                    continue;

                CreateChunk(key);
                chunksChanged = true;
            }

            List<Vector2Int> chunksToRemove = null;
            foreach (KeyValuePair<Vector2Int, PeriodicTerrainChunk> entry in chunks)
            {
                if (requiredChunks.Contains(entry.Key))
                    continue;

                if (chunksToRemove == null)
                    chunksToRemove = new List<Vector2Int>();
                chunksToRemove.Add(entry.Key);
            }

            if (chunksToRemove == null)
                return;

            foreach (Vector2Int key in chunksToRemove)
            {
                PeriodicTerrainChunk chunk = chunks[key];
                chunk.gameObject.SetActive(false);
                chunkPool.Push(chunk);
                chunks.Remove(key);
                chunksChanged = true;
            }

            if (chunksChanged)
                chunkVersion++;
        }

        private void AddCameraChunks(
            float cameraX,
            float cameraZ,
            float halfWidth,
            float halfDepth)
        {
            float margin = chunkSize * (extraChunkMargin + 1);
            AddChunksInBounds(
                cameraX - halfWidth - margin,
                cameraX + halfWidth + margin,
                cameraZ - halfDepth - margin,
                cameraZ + halfDepth + margin);
        }

        private void AddInterestChunks(Vector3 position, float radius)
        {
            AddChunksInBounds(
                position.x - radius,
                position.x + radius,
                position.z - radius,
                position.z + radius);
        }

        private void AddChunksInBounds(
            float minX,
            float maxX,
            float minZ,
            float maxZ)
        {
            int minChunkX = Mathf.FloorToInt(minX / chunkSize);
            int maxChunkX = Mathf.FloorToInt(maxX / chunkSize);
            int minChunkZ = Mathf.FloorToInt(minZ / chunkSize);
            int maxChunkZ = Mathf.FloorToInt(maxZ / chunkSize);

            for (int z = minChunkZ; z <= maxChunkZ; z++)
            {
                for (int x = minChunkX; x <= maxChunkX; x++)
                    requiredChunks.Add(new Vector2Int(x, z));
            }
        }

        private void CreateChunk(Vector2Int key)
        {
            PeriodicTerrainChunk chunk;
            if (chunkPool.Count > 0)
            {
                chunk = chunkPool.Pop();
                chunk.gameObject.SetActive(true);
            }
            else
            {
                GameObject chunkObject = new GameObject();
                chunkObject.transform.SetParent(chunkRoot, false);
                chunk = chunkObject.AddComponent<PeriodicTerrainChunk>();
            }

            chunk.Build(
                key.x,
                key.y,
                chunkSize,
                chunkResolution,
                seaLevel,
                noise,
                terrainMaterial,
                oceanMaterial,
                createTerrainColliders);
            chunks.Add(key, chunk);
            chunkVersion++;
        }

        private void EnsureNoiseInitialized()
        {
            if (noise != null)
                return;

            noise = new PeriodicTerrainNoise(
                seed,
                worldWidth,
                baseCycles,
                octaveCount,
                persistence,
                heightOffset,
                heightAmplitude,
                seaLevel);
        }

        private void OnValidate()
        {
            chunkSize = Mathf.Max(1f, chunkSize);
            worldWidth = Mathf.Max(chunkSize, worldWidth);
            worldWidth = Mathf.Max(
                chunkSize,
                Mathf.Round(worldWidth / chunkSize) * chunkSize);
            chunkResolution = Mathf.Clamp(chunkResolution, 8, 128);
            baseCycles = Mathf.Max(1, baseCycles);
            octaveCount = Mathf.Clamp(octaveCount, 1, 8);
            persistence = Mathf.Clamp01(persistence);
            heightAmplitude = Mathf.Max(0f, heightAmplitude);
            extraChunkMargin = Mathf.Max(0, extraChunkMargin);
        }
    }
}
