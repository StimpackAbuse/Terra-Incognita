using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.AI;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Unity.AI.Navigation;
using TerraIncognita.Data;
using TerraIncognita.Core;
using TerraIncognita.Unit;
using TerraIncognita.Input;
using TerraIncognita.UI;

namespace TerraIncognita.EditorTools
{
    /// <summary>
    /// 프로토타입 씬(RTSPrototype.unity)을 자동으로 구성하고 NavMesh를 베이크해주는 에디터 유틸리티입니다.
    /// 메뉴: Tools > Terra Incognita > Setup RTS Prototype Scene
    /// </summary>
    public static class RTSPrototypeSetup
    {
        private const string ScenePath = "Assets/Scenes/RTSPrototype.unity";
        private const string UnitDataPath = "Assets/Scripts/Data/UnitData_TestInfantry.asset";

        [MenuItem("Tools/Terra Incognita/Setup RTS Prototype Scene")]
        public static void SetupScene()
        {
            // 1. 씬 디렉토리 확인
            string sceneDir = Path.GetDirectoryName(ScenePath);
            if (!Directory.Exists(sceneDir))
            {
                Directory.CreateDirectory(sceneDir);
            }

            // 2. 새로운 빈 씬 생성
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 3. 라이트 생성
            GameObject lightObj = new GameObject("Directional Light");
            Light dirLight = lightObj.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.intensity = 1.5f;
            dirLight.color = Color.white;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 4. 지면 (Ground Plane 100x100)
            GameObject groundObj = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundObj.name = "Ground";
            groundObj.transform.position = Vector3.zero;
            groundObj.transform.localScale = new Vector3(10f, 1f, 10f); // 100m x 100m

            // URP 머티리얼 적용
            Material groundMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Ground.mat");
            if (groundMat != null)
            {
                groundObj.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
            }

            // NavMeshSurface 부착 및 베이크
            NavMeshSurface navSurface = groundObj.AddComponent<NavMeshSurface>();
            navSurface.collectObjects = CollectObjects.All;
            navSurface.BuildNavMesh();

            // 5. 탑다운 카메라 세팅
            GameObject camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            Camera cam = camObj.AddComponent<Camera>();
            camObj.AddComponent<AudioListener>();
            camObj.transform.position = new Vector3(0f, 22f, -14f);
            camObj.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

            RTSCameraController camController = camObj.AddComponent<RTSCameraController>();

            // 6. UI Canvas & SelectionBox
            GameObject canvasObj = new GameObject("[UI Canvas]");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();

            // EventSystem
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<EventSystem>();
            eventSystemObj.AddComponent<InputSystemUIInputModule>();

            // SelectionBox UI Image
            GameObject boxObj = new GameObject("SelectionBox");
            boxObj.transform.SetParent(canvasObj.transform, false);
            Image boxImage = boxObj.AddComponent<Image>();
            boxImage.color = new Color(0f, 0.8f, 0.4f, 0.25f); // 반투명 청록색
            SelectionBoxUI boxUI = boxObj.AddComponent<SelectionBoxUI>();

            // 7. RTS Controller (Selection & Command)
            GameObject controllerObj = new GameObject("[RTS Controller]");
            SelectionManager selManager = controllerObj.AddComponent<SelectionManager>();
            CommandController cmdController = controllerObj.AddComponent<CommandController>();

            // 필드 바인딩 (리플렉션 또는 직렬화 필드 설정)
            var soSel = new SerializedObject(selManager);
            soSel.FindProperty("mainCamera").objectReferenceValue = cam;
            soSel.FindProperty("selectionBoxUI").objectReferenceValue = boxUI;
            soSel.ApplyModifiedProperties();

            var soCmd = new SerializedObject(cmdController);
            soCmd.FindProperty("mainCamera").objectReferenceValue = cam;
            soCmd.ApplyModifiedProperties();

            // 8. UnitData 로드
            UnitData unitData = AssetDatabase.LoadAssetAtPath<UnitData>(UnitDataPath);

            // 9. 테스트 유닛 5기 생성
            GameObject unitsRoot = new GameObject("Units");
            Vector3[] spawnPositions = new Vector3[]
            {
                new Vector3(0f, 1f, 0f),
                new Vector3(2.5f, 1f, 0f),
                new Vector3(-2.5f, 1f, 0f),
                new Vector3(1.2f, 1f, 2.5f),
                new Vector3(-1.2f, 1f, 2.5f)
            };

            Material unitMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Unit_Infantry.mat");
            Material ringMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_SelectionIndicator.mat");

            for (int i = 0; i < spawnPositions.Length; i++)
            {
                GameObject unitObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                unitObj.name = $"Unit_Infantry_{i + 1}";
                unitObj.transform.SetParent(unitsRoot.transform);
                unitObj.transform.position = spawnPositions[i];
                unitObj.GetComponent<MeshRenderer>().sharedMaterial = unitMat;

                // NavMeshAgent
                NavMeshAgent agent = unitObj.AddComponent<NavMeshAgent>();
                agent.speed = 4.5f;
                agent.angularSpeed = 720f;
                agent.acceleration = 16f;
                agent.stoppingDistance = 0.2f;
                agent.radius = 0.5f;

                // Unit Component
                TerraIncognita.Unit.Unit unitComp = unitObj.AddComponent<TerraIncognita.Unit.Unit>();
                unitObj.AddComponent<UnitMovement>();

                // Selection Indicator (바닥 링)
                GameObject ringObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ringObj.name = "SelectionIndicator";
                ringObj.transform.SetParent(unitObj.transform);
                ringObj.transform.localPosition = new Vector3(0f, -0.95f, 0f);
                ringObj.transform.localScale = new Vector3(1.3f, 0.02f, 1.3f);
                Object.DestroyImmediate(ringObj.GetComponent<Collider>());
                ringObj.GetComponent<MeshRenderer>().sharedMaterial = ringMat;
                ringObj.SetActive(false);

                // SerializedObject로 UnitData 및 Indicator 연결
                var soUnit = new SerializedObject(unitComp);
                soUnit.FindProperty("unitData").objectReferenceValue = unitData;
                soUnit.FindProperty("selectionIndicator").objectReferenceValue = ringObj;
                soUnit.ApplyModifiedProperties();
            }

            // 10. 씬 저장
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Terra Incognita] RTS Prototype 씬이 성공적으로 생성 및 구성되었습니다: {ScenePath}");
            EditorUtility.DisplayDialog("Terra Incognita", "RTS Prototype 씬이 완벽하게 구성되고 NavMesh가 베이크되었습니다!\n\n이제 Play 버튼을 눌러 테스트할 수 있습니다.", "확인");
        }
    }
}
