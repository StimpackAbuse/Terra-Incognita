using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TerraIncognita.World
{
    [DisallowMultipleComponent]
    public sealed class PeriodicTerrainChunk : MonoBehaviour
    {
        private Mesh terrainMesh;
        private Mesh landMesh;
        private Mesh oceanMesh;
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
            EnsureComponents(createCollider);
            ReleaseMeshes();

            name = $"Terrain Chunk ({chunkX}, {chunkZ})";
            transform.position = new Vector3(chunkX * chunkSize, 0f, chunkZ * chunkSize);

            terrainMesh = CreateTerrainMesh(
                chunkX,
                chunkZ,
                chunkSize,
                resolution,
                noise);
            landMesh = CreateLandMesh(terrainMesh, seaLevel);
            terrainFilter.sharedMesh = terrainMesh;
            terrainRenderer.sharedMaterial = terrainMaterial;
            terrainRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            terrainRenderer.receiveShadows = true;

            if (terrainCollider != null)
                terrainCollider.sharedMesh = landMesh;

            oceanMesh = CreateOceanMesh(chunkSize, seaLevel + 0.02f);
            oceanFilter.sharedMesh = oceanMesh;
            oceanRenderer.sharedMaterial = oceanMaterial;
            oceanRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            oceanRenderer.receiveShadows = false;
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

        private void ReleaseMeshes()
        {
            if (terrainCollider != null)
                terrainCollider.sharedMesh = null;
            if (terrainFilter != null)
                terrainFilter.sharedMesh = null;
            if (oceanFilter != null)
                oceanFilter.sharedMesh = null;
            if (terrainMesh != null)
                DestroyMesh(terrainMesh);
            if (landMesh != null)
                DestroyMesh(landMesh);
            if (oceanMesh != null)
                DestroyMesh(oceanMesh);

            terrainMesh = null;
            landMesh = null;
            oceanMesh = null;
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (Application.isPlaying)
                Destroy(mesh);
            else
                DestroyImmediate(mesh);
        }

        private Mesh CreateTerrainMesh(
            int chunkX,
            int chunkZ,
            float chunkSize,
            int resolution,
            PeriodicTerrainNoise noise)
        {
            int side = resolution + 1;
            Vector3[] vertices = new Vector3[side * side];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[resolution * resolution * 6];
            float spacing = chunkSize / resolution;
            float derivativeStep = spacing * 0.5f;

            for (int zIndex = 0; zIndex <= resolution; zIndex++)
            {
                for (int xIndex = 0; xIndex <= resolution; xIndex++)
                {
                    int vertexIndex = zIndex * side + xIndex;
                    float localX = xIndex * spacing;
                    float localZ = zIndex * spacing;
                    float worldX = chunkX * chunkSize + localX;
                    float worldZ = chunkZ * chunkSize + localZ;

                    vertices[vertexIndex] = new Vector3(
                        localX,
                        noise.GetHeight(worldX, worldZ),
                        localZ);

                    float leftHeight = noise.GetHeight(worldX - derivativeStep, worldZ);
                    float rightHeight = noise.GetHeight(worldX + derivativeStep, worldZ);
                    float downHeight = noise.GetHeight(worldX, worldZ - derivativeStep);
                    float upHeight = noise.GetHeight(worldX, worldZ + derivativeStep);
                    normals[vertexIndex] = new Vector3(
                        leftHeight - rightHeight,
                        derivativeStep * 2f,
                        downHeight - upHeight).normalized;
                    uvs[vertexIndex] = new Vector2(
                        localX / chunkSize,
                        localZ / chunkSize);
                }
            }

            int triangleIndex = 0;
            for (int zIndex = 0; zIndex < resolution; zIndex++)
            {
                for (int xIndex = 0; xIndex < resolution; xIndex++)
                {
                    int bottomLeft = zIndex * side + xIndex;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + side;
                    int topRight = topLeft + 1;

                    triangles[triangleIndex++] = bottomLeft;
                    triangles[triangleIndex++] = topLeft;
                    triangles[triangleIndex++] = bottomRight;
                    triangles[triangleIndex++] = bottomRight;
                    triangles[triangleIndex++] = topLeft;
                    triangles[triangleIndex++] = topRight;
                }
            }

            Mesh mesh = new Mesh
            {
                name = $"Terrain Mesh ({chunkX}, {chunkZ})",
                vertices = vertices,
                normals = normals,
                uv = uvs,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh CreateOceanMesh(float chunkSize, float seaLevel)
        {
            Vector3[] vertices =
            {
                new Vector3(0f, seaLevel, 0f),
                new Vector3(0f, seaLevel, chunkSize),
                new Vector3(chunkSize, seaLevel, 0f),
                new Vector3(chunkSize, seaLevel, chunkSize)
            };
            int[] triangles = { 0, 1, 2, 2, 1, 3 };

            Mesh mesh = new Mesh
            {
                name = "Ocean Surface Mesh",
                vertices = vertices,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh CreateLandMesh(Mesh source, float seaLevel)
        {
            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector2[] sourceUvs = source.uv;
            int[] sourceTriangles = source.triangles;
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();
            TerrainVertex[] input = new TerrainVertex[8];
            TerrainVertex[] output = new TerrainVertex[8];

            for (int index = 0; index < sourceTriangles.Length; index += 3)
            {
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertexIndex = sourceTriangles[index + corner];
                    input[corner] = new TerrainVertex(
                        sourceVertices[vertexIndex],
                        sourceNormals[vertexIndex],
                        sourceUvs[vertexIndex]);
                }

                int count = ClipAboveSeaLevel(input, 3, output, seaLevel);
                if (count < 3)
                    continue;

                int firstVertex = vertices.Count;
                for (int vertexIndex = 0; vertexIndex < count; vertexIndex++)
                {
                    vertices.Add(output[vertexIndex].position);
                    normals.Add(output[vertexIndex].normal);
                    uvs.Add(output[vertexIndex].uv);
                }

                for (int vertexIndex = 1; vertexIndex < count - 1; vertexIndex++)
                {
                    triangles.Add(firstVertex);
                    triangles.Add(firstVertex + vertexIndex);
                    triangles.Add(firstVertex + vertexIndex + 1);
                }
            }

            if (vertices.Count == 0)
                return null;

            Mesh mesh = new Mesh
            {
                name = $"{name} Land Navigation Mesh"
            };
            if (vertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
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
            ReleaseMeshes();
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
