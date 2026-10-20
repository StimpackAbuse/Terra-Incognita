using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace TerraIncognita.World
{
    [DisallowMultipleComponent]
    public sealed class PeriodicTerrainChunk : MonoBehaviour
    {
        private static readonly ProfilerMarker BuildMarker =
            new ProfilerMarker("PeriodicTerrainChunk.Build");

        private static readonly int[] OceanTriangles = { 0, 1, 2, 2, 1, 3 };
        private static readonly List<Vector3> LandVertices = new List<Vector3>();
        private static readonly List<Vector3> LandNormals = new List<Vector3>();
        private static readonly List<Vector2> LandUvs = new List<Vector2>();
        private static readonly List<int> LandTriangles = new List<int>();

        private Mesh terrainMesh;
        private Mesh landMesh;
        private Mesh oceanMesh;
        private Vector3[] terrainVertices;
        private Vector3[] terrainNormals;
        private Vector2[] terrainUvs;
        private int[] terrainTriangles;
        private float[] sampledHeights;
        private Vector3[] oceanVertices = new Vector3[4];
        private readonly TerrainVertex[] clipInput = new TerrainVertex[8];
        private readonly TerrainVertex[] clipOutput = new TerrainVertex[8];
        private int bufferResolution = -1;
        private MeshFilter terrainFilter;
        private MeshRenderer terrainRenderer;
        private MeshCollider terrainCollider;
        private Transform oceanSurface;
        private MeshFilter oceanFilter;
        private MeshRenderer oceanRenderer;

        public void Build(
            int chunkX,
            int chunkZ,
            float chunkSize,
            int resolution,
            float seaLevel,
            PeriodicTerrainNoise noise,
            Material terrainMaterial,
            Material oceanMaterial,
            bool createCollider)
        {
            using (BuildMarker.Auto())
            {
                EnsureComponents(createCollider);
                DetachMeshes();
                EnsureBuffers(resolution);

                name = $"Terrain Chunk ({chunkX}, {chunkZ})";
                transform.position = new Vector3(chunkX * chunkSize, 0f, chunkZ * chunkSize);

                CreateTerrainMesh(chunkX, chunkZ, chunkSize, resolution, noise);
                CreateLandMesh(seaLevel);
                terrainFilter.sharedMesh = terrainMesh;
                terrainRenderer.sharedMaterial = terrainMaterial;
                terrainRenderer.shadowCastingMode = ShadowCastingMode.On;
                terrainRenderer.receiveShadows = true;

                if (terrainCollider != null)
                    terrainCollider.sharedMesh = landMesh;

                CreateOceanMesh(chunkSize, seaLevel + 0.02f);
                oceanFilter.sharedMesh = oceanMesh;
                oceanRenderer.sharedMaterial = oceanMaterial;
                oceanRenderer.shadowCastingMode = ShadowCastingMode.Off;
                oceanRenderer.receiveShadows = false;
            }
        }

        private void EnsureComponents(bool createCollider)
        {
            if (terrainFilter == null)
                terrainFilter = gameObject.AddComponent<MeshFilter>();
            if (terrainRenderer == null)
                terrainRenderer = gameObject.AddComponent<MeshRenderer>();
            if (createCollider && terrainCollider == null)
                terrainCollider = gameObject.AddComponent<MeshCollider>();

            if (oceanSurface == null)
            {
                GameObject oceanObject = new GameObject("Ocean Surface");
                oceanSurface = oceanObject.transform;
                oceanSurface.SetParent(transform, false);
                oceanFilter = oceanObject.AddComponent<MeshFilter>();
                oceanRenderer = oceanObject.AddComponent<MeshRenderer>();
            }
        }

        private void DetachMeshes()
        {
            if (terrainCollider != null)
                terrainCollider.sharedMesh = null;
            if (terrainFilter != null)
                terrainFilter.sharedMesh = null;
            if (oceanFilter != null)
                oceanFilter.sharedMesh = null;
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (Application.isPlaying)
                Destroy(mesh);
            else
                DestroyImmediate(mesh);
        }

        private void CreateTerrainMesh(
            int chunkX,
            int chunkZ,
            float chunkSize,
            int resolution,
            PeriodicTerrainNoise noise)
        {
            int side = resolution + 1;
            float spacing = chunkSize / resolution;
            float derivativeStep = spacing * 0.5f;
            int sampleSide = resolution * 2 + 3;
            float worldBaseX = chunkX * chunkSize;
            float worldBaseZ = chunkZ * chunkSize;

            for (int zIndex = 0; zIndex < sampleSide; zIndex++)
            {
                float worldZ = worldBaseZ + (zIndex - 1) * derivativeStep;
                int rowStart = zIndex * sampleSide;
                for (int xIndex = 0; xIndex < sampleSide; xIndex++)
                {
                    float worldX = worldBaseX + (xIndex - 1) * derivativeStep;
                    sampledHeights[rowStart + xIndex] = noise.GetHeight(worldX, worldZ);
                }
            }

            for (int zIndex = 0; zIndex <= resolution; zIndex++)
            {
                for (int xIndex = 0; xIndex <= resolution; xIndex++)
                {
                    int vertexIndex = zIndex * side + xIndex;
                    float localX = xIndex * spacing;
                    float localZ = zIndex * spacing;
                    int sampleIndex = (zIndex * 2 + 1) * sampleSide + xIndex * 2 + 1;
                    terrainVertices[vertexIndex] = new Vector3(
                        localX,
                        sampledHeights[sampleIndex],
                        localZ);

                    terrainNormals[vertexIndex] = new Vector3(
                        sampledHeights[sampleIndex - 1] - sampledHeights[sampleIndex + 1],
                        derivativeStep * 2f,
                        sampledHeights[sampleIndex - sampleSide] -
                        sampledHeights[sampleIndex + sampleSide]).normalized;
                    terrainUvs[vertexIndex] = new Vector2(
                        localX / chunkSize,
                        localZ / chunkSize);
                }
            }

            terrainMesh.Clear(true);
            terrainMesh.vertices = terrainVertices;
            terrainMesh.normals = terrainNormals;
            terrainMesh.uv = terrainUvs;
            terrainMesh.triangles = terrainTriangles;
            terrainMesh.RecalculateBounds();
        }

        private void CreateOceanMesh(float chunkSize, float seaLevel)
        {
            oceanVertices[0] = new Vector3(0f, seaLevel, 0f);
            oceanVertices[1] = new Vector3(0f, seaLevel, chunkSize);
            oceanVertices[2] = new Vector3(chunkSize, seaLevel, 0f);
            oceanVertices[3] = new Vector3(chunkSize, seaLevel, chunkSize);

            oceanMesh.Clear(true);
            oceanMesh.vertices = oceanVertices;
            oceanMesh.triangles = OceanTriangles;
            oceanMesh.RecalculateNormals();
            oceanMesh.RecalculateBounds();
        }

        private void CreateLandMesh(float seaLevel)
        {
            LandVertices.Clear();
            LandNormals.Clear();
            LandUvs.Clear();
            LandTriangles.Clear();

            for (int index = 0; index < terrainTriangles.Length; index += 3)
            {
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertexIndex = terrainTriangles[index + corner];
                    clipInput[corner] = new TerrainVertex(
                        terrainVertices[vertexIndex],
                        terrainNormals[vertexIndex],
                        terrainUvs[vertexIndex]);
                }

                int count = ClipAboveSeaLevel(clipInput, 3, clipOutput, seaLevel);
                if (count < 3)
                    continue;

                int firstVertex = LandVertices.Count;
                for (int vertexIndex = 0; vertexIndex < count; vertexIndex++)
                {
                    LandVertices.Add(clipOutput[vertexIndex].position);
                    LandNormals.Add(clipOutput[vertexIndex].normal);
                    LandUvs.Add(clipOutput[vertexIndex].uv);
                }

                for (int vertexIndex = 1; vertexIndex < count - 1; vertexIndex++)
                {
                    LandTriangles.Add(firstVertex);
                    LandTriangles.Add(firstVertex + vertexIndex);
                    LandTriangles.Add(firstVertex + vertexIndex + 1);
                }
            }

            landMesh.Clear(true);
            if (LandVertices.Count == 0)
                return;

            landMesh.indexFormat = LandVertices.Count > 65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            landMesh.SetVertices(LandVertices);
            landMesh.SetNormals(LandNormals);
            landMesh.SetUVs(0, LandUvs);
            landMesh.SetTriangles(LandTriangles, 0, false);
            landMesh.RecalculateBounds();
        }

        private static int ClipAboveSeaLevel(
            TerrainVertex[] input,
            int inputCount,
            TerrainVertex[] output,
            float seaLevel)
        {
            int outputCount = 0;
            TerrainVertex previous = input[inputCount - 1];
            bool previousIsLand = previous.position.y > seaLevel;

            for (int index = 0; index < inputCount; index++)
            {
                TerrainVertex current = input[index];
                bool currentIsLand = current.position.y > seaLevel;

                if (currentIsLand != previousIsLand)
                {
                    float interpolation = Mathf.Clamp01(
                        (seaLevel - previous.position.y) /
                        (current.position.y - previous.position.y));
                    output[outputCount++] = TerrainVertex.Lerp(
                        previous,
                        current,
                        interpolation);
                }

                if (currentIsLand)
                    output[outputCount++] = current;

                previous = current;
                previousIsLand = currentIsLand;
            }

            return outputCount;
        }

        private void OnDestroy()
        {
            DetachMeshes();
            if (terrainMesh != null)
                DestroyMesh(terrainMesh);
            if (landMesh != null)
                DestroyMesh(landMesh);
            if (oceanMesh != null)
                DestroyMesh(oceanMesh);
        }

        private void EnsureBuffers(int resolution)
        {
            if (terrainMesh == null)
            {
                terrainMesh = new Mesh();
                terrainMesh.MarkDynamic();
            }
            if (landMesh == null)
            {
                landMesh = new Mesh();
                landMesh.MarkDynamic();
            }
            if (oceanMesh == null)
            {
                oceanMesh = new Mesh();
                oceanMesh.MarkDynamic();
            }

            if (bufferResolution == resolution)
                return;

            int side = resolution + 1;
            terrainVertices = new Vector3[side * side];
            terrainNormals = new Vector3[terrainVertices.Length];
            terrainUvs = new Vector2[terrainVertices.Length];
            terrainTriangles = new int[resolution * resolution * 6];
            sampledHeights = new float[(resolution * 2 + 3) * (resolution * 2 + 3)];

            int triangleIndex = 0;
            for (int zIndex = 0; zIndex < resolution; zIndex++)
            {
                for (int xIndex = 0; xIndex < resolution; xIndex++)
                {
                    int bottomLeft = zIndex * side + xIndex;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + side;
                    int topRight = topLeft + 1;

                    terrainTriangles[triangleIndex++] = bottomLeft;
                    terrainTriangles[triangleIndex++] = topLeft;
                    terrainTriangles[triangleIndex++] = bottomRight;
                    terrainTriangles[triangleIndex++] = bottomRight;
                    terrainTriangles[triangleIndex++] = topLeft;
                    terrainTriangles[triangleIndex++] = topRight;
                }
            }

            LandVertices.Capacity = Mathf.Max(
                LandVertices.Capacity,
                resolution * resolution * 8);
            LandNormals.Capacity = Mathf.Max(LandNormals.Capacity, resolution * resolution * 8);
            LandUvs.Capacity = Mathf.Max(LandUvs.Capacity, resolution * resolution * 8);
            LandTriangles.Capacity = Mathf.Max(
                LandTriangles.Capacity,
                resolution * resolution * 12);
            bufferResolution = resolution;
        }

        private readonly struct TerrainVertex
        {
            public readonly Vector3 position;
            public readonly Vector3 normal;
            public readonly Vector2 uv;

            public TerrainVertex(Vector3 position, Vector3 normal, Vector2 uv)
            {
                this.position = position;
                this.normal = normal;
                this.uv = uv;
            }

            public static TerrainVertex Lerp(
                TerrainVertex start,
                TerrainVertex end,
                float interpolation)
            {
                return new TerrainVertex(
                    Vector3.Lerp(start.position, end.position, interpolation),
                    Vector3.Slerp(start.normal, end.normal, interpolation).normalized,
                    Vector2.Lerp(start.uv, end.uv, interpolation));
            }
        }
    }
}
