using System;
using System.Collections.Generic;
using System.Reflection;
using TerraIncognita.Core;
using TerraIncognita.Data;
using TerraIncognita.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace TerraIncognita.EditorTools
{
    public static class PeriodicTerrainPrototypeSetup
    {
        private const string ScenePath = "Assets/Scenes/PeriodicTerrainPrototype.unity";
        private const string OceanMaterialPath = "Assets/Materials/M_PeriodicTerrain_Ocean.mat";
        private const string TestUnitDataPath = "Assets/Scripts/Data/UnitData_TestInfantry.asset";

        [MenuItem("Tools/Terra Incognita/Open Periodic Terrain Prototype")]
        public static void OpenPrototypeScene()
        {
            if (!System.IO.File.Exists(ScenePath))
            {
                throw new InvalidOperationException(
                    $"The prototype scene does not exist: {ScenePath}");
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Tools/Terra Incognita/Create Periodic Terrain Prototype")]
        public static void CreateScene()
        {
            if (System.IO.File.Exists(ScenePath))
            {
                Debug.LogError(
                    $"[PeriodicTerrainPrototypeSetup] Scene already exists and was not overwritten: {ScenePath}");
                return;
            }

            ValidateNoiseSamples();

            Material terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/M_Ground.mat");
            if (terrainMaterial == null)
                throw new InvalidOperationException("Could not load Assets/Materials/M_Ground.mat.");

            Material oceanMaterial = LoadOrCreateOceanMaterial();
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            CreateLighting();
            Camera camera = CreateCamera();
            PeriodicTerrainWorld world = CreateTerrainWorld(camera, terrainMaterial, oceanMaterial);
            ConfigureMovementPrototype(world, terrainMaterial);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"[PeriodicTerrainPrototypeSetup] Created prototype scene: {ScenePath}");
        }

        [MenuItem("Tools/Terra Incognita/Validate Periodic Terrain Noise")]
        public static void ValidatePeriodicTerrainNoise()
        {
            ValidateNoiseSamples();
            Debug.Log("[PeriodicTerrainPrototypeSetup] Periodic noise validation passed.");
        }

        [MenuItem("Tools/Terra Incognita/Setup Periodic Terrain Movement Prototype")]
        public static void SetupMovementPrototypeScene()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            PeriodicTerrainWorld world =
                UnityEngine.Object.FindFirstObjectByType<PeriodicTerrainWorld>();
            if (world == null)
                throw new InvalidOperationException("The prototype scene has no terrain world.");

            Material terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/M_Ground.mat");
            if (terrainMaterial == null)
                throw new InvalidOperationException("Could not load Assets/Materials/M_Ground.mat.");

            ConfigureMovementPrototype(world, terrainMaterial);
            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            EditorSceneManager.SaveScene(world.gameObject.scene);
            Debug.Log(
                "[PeriodicTerrainPrototypeSetup] Added the NavMesh and unit-movement prototype.");
        }

        [MenuItem("Tools/Terra Incognita/Validate Periodic Terrain Scene")]
        public static void ValidatePrototypeScene()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            PeriodicTerrainWorld world = UnityEngine.Object.FindFirstObjectByType<PeriodicTerrainWorld>();
            Camera camera = Camera.main;
            if (world == null || camera == null)
                throw new InvalidOperationException("The prototype scene is missing its world or main camera.");

            PeriodicTerrainCameraController cameraController =
                camera.GetComponent<PeriodicTerrainCameraController>();
            if (cameraController == null)
                throw new InvalidOperationException("The prototype camera controller is missing.");

            Vector3 originalCameraPosition = camera.transform.position;
            InvokePrivate(world, "Awake");
            SetPrivateField(world, "maxChunksPerFrame", int.MaxValue);
            InvokePrivate(world, "LateUpdate");

            if (world.ActiveChunkCount == 0)
                throw new InvalidOperationException("The terrain world did not generate visible chunks.");

            Transform chunkRoot = world.transform.Find("Generated Terrain Chunks");
            if (chunkRoot == null)
                throw new InvalidOperationException("The generated terrain chunk root was not created.");

            int landColliderCount = 0;
            for (int i = 0; i < chunkRoot.childCount; i++)
            {
                PeriodicTerrainChunk chunk =
                    chunkRoot.GetChild(i).GetComponent<PeriodicTerrainChunk>();
                if (chunk == null)
                    continue;

                MeshFilter meshFilter = chunk.GetComponent<MeshFilter>();
                MeshCollider meshCollider = chunk.GetComponent<MeshCollider>();
                if (meshFilter == null || meshFilter.sharedMesh == null ||
                    meshCollider == null)
                {
                    throw new InvalidOperationException(
                        $"Terrain mesh or collider is missing on {chunk.name}.");
                }

                if (meshCollider.sharedMesh == null)
                    continue;
                if (meshCollider.sharedMesh == meshFilter.sharedMesh)
                    throw new InvalidOperationException(
                        $"The full terrain mesh is still walkable under the ocean on {chunk.name}.");

                foreach (Vector3 vertex in meshCollider.sharedMesh.vertices)
                {
                    if (vertex.y < world.SeaLevel - 0.001f)
                        throw new InvalidOperationException(
                            $"The land collider extends below sea level on {chunk.name}.");
                }

                landColliderCount++;
            }

            if (landColliderCount == 0)
                throw new InvalidOperationException("No active chunk contains a land-only collider.");

            ValidateWrappedNavigationCoordinates(world);
            ValidateNavMeshExcludesOcean(world);
            ValidateChunkSeams(chunkRoot, world.ChunkSize);
            var originalChunkIds = new HashSet<int>();
            for (int i = 0; i < chunkRoot.childCount; i++)
            {
                PeriodicTerrainChunk chunk =
                    chunkRoot.GetChild(i).GetComponent<PeriodicTerrainChunk>();
                if (chunk != null && chunk.gameObject.activeSelf)
                    originalChunkIds.Add(chunk.gameObject.GetInstanceID());
            }

            camera.transform.position = new Vector3(
                world.WorldWidth * 0.5f + 10f,
                originalCameraPosition.y,
                originalCameraPosition.z);
            InvokePrivate(cameraController, "WrapCameraX");
            float expectedWrappedX = -world.WorldWidth * 0.5f + 10f;
            if (Mathf.Abs(camera.transform.position.x - expectedWrappedX) > 0.001f)
                throw new InvalidOperationException("The camera did not wrap across the world boundary.");

            InvokePrivate(world, "LateUpdate");
            int reusedChunkCount = 0;
            for (int i = 0; i < chunkRoot.childCount; i++)
            {
                PeriodicTerrainChunk chunk =
                    chunkRoot.GetChild(i).GetComponent<PeriodicTerrainChunk>();
                if (chunk != null && chunk.gameObject.activeSelf &&
                    originalChunkIds.Contains(chunk.gameObject.GetInstanceID()))
                {
                    reusedChunkCount++;
                }
            }

            if (world.ActiveChunkCount == 0 || reusedChunkCount == 0)
                throw new InvalidOperationException("Terrain chunks were not retained and reused after camera wrapping.");

            UnityEngine.Object.DestroyImmediate(world.gameObject);
            camera.transform.position = originalCameraPosition;
            Debug.Log(
                $"[PeriodicTerrainPrototypeSetup] Scene validation passed: " +
                $"{landColliderCount} land colliders, {reusedChunkCount} chunks reused across the seam.");
        }

        private static void ValidateChunkSeams(Transform chunkRoot, float chunkSize)
        {
            float maximumHeightError = 0f;
            float maximumNormalError = 0f;
            int matchedEdges = 0;

            for (int i = 0; i < chunkRoot.childCount; i++)
            {
                Transform leftChunk = chunkRoot.GetChild(i);
                PeriodicTerrainChunk leftComponent =
                    leftChunk.GetComponent<PeriodicTerrainChunk>();
                if (leftComponent == null || !leftChunk.gameObject.activeSelf)
                    continue;

                for (int j = 0; j < chunkRoot.childCount; j++)
                {
                    Transform rightChunk = chunkRoot.GetChild(j);
                    if (!rightChunk.gameObject.activeSelf ||
                        Mathf.Abs(leftChunk.position.x + chunkSize - rightChunk.position.x) > 0.001f ||
                        Mathf.Abs(leftChunk.position.z - rightChunk.position.z) > 0.001f)
                    {
                        continue;
                    }

                    Mesh leftMesh = leftChunk.GetComponent<MeshFilter>().sharedMesh;
                    Mesh rightMesh = rightChunk.GetComponent<MeshFilter>().sharedMesh;
                    Vector3[] leftVertices = leftMesh.vertices;
                    Vector3[] rightVertices = rightMesh.vertices;
                    Vector3[] leftNormals = leftMesh.normals;
                    Vector3[] rightNormals = rightMesh.normals;
                    int side = Mathf.RoundToInt(Mathf.Sqrt(leftVertices.Length));
                    if (side * side != leftVertices.Length ||
                        rightVertices.Length != leftVertices.Length)
                    {
                        throw new InvalidOperationException("A terrain chunk has an unexpected vertex layout.");
                    }

                    for (int row = 0; row < side; row++)
                    {
                        int leftIndex = row * side + side - 1;
                        int rightIndex = row * side;
                        maximumHeightError = Mathf.Max(
                            maximumHeightError,
                            Mathf.Abs(leftVertices[leftIndex].y - rightVertices[rightIndex].y));
                        maximumNormalError = Mathf.Max(
                            maximumNormalError,
                            Vector3.Distance(leftNormals[leftIndex], rightNormals[rightIndex]));
                    }

                    matchedEdges++;
                }
            }

            if (matchedEdges == 0 || maximumHeightError > 0.0001f || maximumNormalError > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"Terrain chunk seam validation failed: height={maximumHeightError}, " +
                    $"normal={maximumNormalError}, edges={matchedEdges}.");
            }
        }

        private static void ValidateNavMeshExcludesOcean(PeriodicTerrainWorld world)
        {
            NavMeshSurface surface = world.gameObject.AddComponent<NavMeshSurface>();
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~(1 << 2);
            surface.BuildNavMesh();

            bool foundWalkableLand = false;
            bool foundUnwalkableOcean = false;
            Vector3 cameraPosition = Camera.main.transform.position;
            for (int zIndex = -40; zIndex <= 40; zIndex++)
            {
                for (int xIndex = -40; xIndex <= 40; xIndex++)
                {
                    float x = cameraPosition.x + xIndex * 2f;
                    float z = cameraPosition.z + zIndex * 2f;
                    float height = world.GetHeight(x, z);

                    if (!foundWalkableLand &&
                        height > world.SeaLevel + 2f &&
                        NavMesh.SamplePosition(
                            new Vector3(x, height + 1f, z),
                            out _,
                            2f,
                            NavMesh.AllAreas))
                    {
                        foundWalkableLand = true;
                    }

                    if (!foundUnwalkableOcean &&
                        height < world.SeaLevel - 3f &&
                        !NavMesh.SamplePosition(
                            new Vector3(x, world.SeaLevel, z),
                            out _,
                            0.25f,
                            NavMesh.AllAreas))
                    {
                        foundUnwalkableOcean = true;
                    }

                    if (foundWalkableLand && foundUnwalkableOcean)
                        return;
                }
            }

            throw new InvalidOperationException(
                $"Land/ocean NavMesh validation failed: land={foundWalkableLand}, " +
                $"oceanExcluded={foundUnwalkableOcean}.");
        }

        private static void ValidateWrappedNavigationCoordinates(PeriodicTerrainWorld world)
        {
            float halfWidth = world.WorldWidth * 0.5f;
            ValidateNearestWrappedImage(
                world,
                new Vector3(halfWidth - 2f, 0f, 0f),
                new Vector3(-halfWidth + 3f, 0f, 0f),
                halfWidth + 3f);
            ValidateNearestWrappedImage(
                world,
                new Vector3(-halfWidth + 2f, 0f, 0f),
                new Vector3(halfWidth - 3f, 0f, 0f),
                -halfWidth - 3f);

            Vector3 wrapped = new Vector3(halfWidth + 2f, 0f, 0f);
            if (!world.TryWrapPosition(ref wrapped) ||
                Mathf.Abs(wrapped.x - (-halfWidth + 2f)) > 0.001f)
            {
                throw new InvalidOperationException(
                    "The unit position did not wrap to the matching point across the world seam.");
            }
        }

        private static void ValidateNearestWrappedImage(
            PeriodicTerrainWorld world,
            Vector3 cameraPosition,
            Vector3 unitPosition,
            float expectedX)
        {
            Vector3 nearest = world.GetNearestWrappedPosition(cameraPosition, unitPosition);
            if (Mathf.Abs(nearest.x - expectedX) > 0.001f ||
                Mathf.Abs(nearest.x - cameraPosition.x) > world.WorldWidth * 0.5f)
            {
                throw new InvalidOperationException(
                    "The unit visual did not choose its nearest image across the world seam.");
            }
        }

        private static void InvokePrivate(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
                throw new InvalidOperationException($"Could not find {methodName} on {target.GetType().Name}.");

            method.Invoke(target, null);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new InvalidOperationException(
                    $"Could not find {fieldName} on {target.GetType().Name}.");

            field.SetValue(target, value);
        }

        private static void ValidateNoiseSamples()
        {
            const float worldWidth = 256f;
            const float derivativeStep = 0.1f;
            const float heightTolerance = 0.0001f;
            const float derivativeTolerance = 0.001f;
            var noise = new PeriodicTerrainNoise(
                13579,
                worldWidth,
                3,
                4,
                0.5f,
                4f,
                18f,
                0f);
            var repeatedNoise = new PeriodicTerrainNoise(
                13579,
                worldWidth,
                3,
                4,
                0.5f,
                4f,
                18f,
                0f);
            float maximumHeightError = 0f;
            float maximumDerivativeError = 0f;
            bool foundLand = false;
            bool foundOcean = false;

            for (int row = -16; row <= 16; row++)
            {
                float z = row * 7.25f;
                float westHeight = noise.GetHeight(-worldWidth * 0.5f, z);
                float eastHeight = noise.GetHeight(worldWidth * 0.5f, z);
                float westSlope = (
                    noise.GetHeight(-worldWidth * 0.5f + derivativeStep, z) -
                    noise.GetHeight(-worldWidth * 0.5f - derivativeStep, z)) /
                    (derivativeStep * 2f);
                float eastSlope = (
                    noise.GetHeight(worldWidth * 0.5f + derivativeStep, z) -
                    noise.GetHeight(worldWidth * 0.5f - derivativeStep, z)) /
                    (derivativeStep * 2f);

                maximumHeightError = Mathf.Max(
                    maximumHeightError,
                    Mathf.Abs(westHeight - eastHeight));
                maximumDerivativeError = Mathf.Max(
                    maximumDerivativeError,
                    Mathf.Abs(westSlope - eastSlope));

                float sampleX = row * 11.5f;
                float sampleZ = row * 9.75f;
                float sampleHeight = noise.GetHeight(sampleX, sampleZ);
                if (Mathf.Abs(sampleHeight - repeatedNoise.GetHeight(sampleX, sampleZ)) >
                    heightTolerance)
                {
                    throw new InvalidOperationException(
                        "Terrain noise did not reproduce for an identical seed and coordinate.");
                }
            }

            for (int zIndex = 0; zIndex < 32; zIndex++)
            {
                for (int xIndex = 0; xIndex < 64; xIndex++)
                {
                    float x = -worldWidth * 0.5f + xIndex * (worldWidth / 64f);
                    float z = (zIndex - 16) * 8f;
                    float height = noise.GetHeight(x, z);
                    foundOcean |= height <= 0f;
                    foundLand |= height > 0f;
                }
            }

            if (maximumHeightError > heightTolerance)
            {
                throw new InvalidOperationException(
                    $"Terrain height seam exceeded tolerance: {maximumHeightError}.");
            }

            if (maximumDerivativeError > derivativeTolerance)
            {
                throw new InvalidOperationException(
                    $"Terrain slope seam exceeded tolerance: {maximumDerivativeError}.");
            }

            if (!foundLand || !foundOcean)
            {
                throw new InvalidOperationException(
                    "Terrain sample did not contain both land and ocean at the configured sea level.");
            }
        }

        private static Material LoadOrCreateOceanMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(OceanMaterialPath);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("Could not find a supported lit shader.");

            Material material = new Material(shader)
            {
                name = "M_PeriodicTerrain_Ocean"
            };
            Color oceanColor = new Color(0.055f, 0.28f, 0.42f, 1f);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", oceanColor);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", oceanColor);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.72f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0.05f);

            AssetDatabase.CreateAsset(material, OceanMaterialPath);
            return material;
        }

        private static void CreateLighting()
        {
            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.color = Color.white;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static Camera CreateCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 60f, -28f);
            cameraObject.transform.rotation = Quaternion.Euler(65f, 0f, 0f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 38f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 500f;
            camera.backgroundColor = new Color(0.08f, 0.13f, 0.18f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<PeriodicTerrainCameraController>();
            return camera;
        }

        private static PeriodicTerrainWorld CreateTerrainWorld(
            Camera camera,
            Material terrainMaterial,
            Material oceanMaterial)
        {
            GameObject worldObject = new GameObject("Periodic Terrain World");
            PeriodicTerrainWorld world = worldObject.AddComponent<PeriodicTerrainWorld>();
            SerializedObject serializedWorld = new SerializedObject(world);
            serializedWorld.FindProperty("targetCamera").objectReferenceValue = camera;
            serializedWorld.FindProperty("terrainMaterial").objectReferenceValue = terrainMaterial;
            serializedWorld.FindProperty("oceanMaterial").objectReferenceValue = oceanMaterial;
            serializedWorld.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject serializedCamera = new SerializedObject(
                camera.GetComponent<PeriodicTerrainCameraController>());
            serializedCamera.FindProperty("terrainWorld").objectReferenceValue = world;
            serializedCamera.ApplyModifiedPropertiesWithoutUndo();
            return world;
        }

        private static void ConfigureMovementPrototype(
            PeriodicTerrainWorld world,
            Material unitMaterial)
        {
            UnitData testUnitData = AssetDatabase.LoadAssetAtPath<UnitData>(TestUnitDataPath);
            if (testUnitData == null)
            {
                throw new InvalidOperationException(
                    $"Could not load the existing test unit data: {TestUnitDataPath}.");
            }

            PeriodicTerrainMovementPrototype prototype =
                world.GetComponent<PeriodicTerrainMovementPrototype>();
            if (prototype == null)
                prototype = world.gameObject.AddComponent<PeriodicTerrainMovementPrototype>();

            SerializedObject serializedPrototype = new SerializedObject(prototype);
            serializedPrototype.FindProperty("terrainWorld").objectReferenceValue = world;
            serializedPrototype.FindProperty("prototypeCamera").objectReferenceValue = Camera.main;
            serializedPrototype.FindProperty("testUnitData").objectReferenceValue = testUnitData;
            serializedPrototype.FindProperty("testUnitMaterial").objectReferenceValue = unitMaterial;
            serializedPrototype.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
