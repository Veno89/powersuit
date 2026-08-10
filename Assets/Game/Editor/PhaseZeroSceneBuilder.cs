#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Powersuit.Editor
{
    public static class PhaseZeroSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/FlightPrototype.unity";
        public const string GreyboxRootName = "Phase 0 Greybox";
        public const string PlayerRootName = "Player Placeholder";

        private const string MaterialsFolder = "Assets/Game/Content/Materials";

        private static readonly string[] GameFolders =
        {
            "Core",
            "Player",
            "Camera",
            "Combat",
            "Enemies",
            "Progression",
            "World",
            "UI",
            "Content",
            "Editor",
            "Tests",
            "Documentation"
        };

        [MenuItem("Tools/Powersuit/Phase 0/Create or Repair Foundation")]
        public static void EnsurePhaseZeroFoundation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            EnsureFolders();
            ConfigureProjectDefaults();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                throw new InvalidOperationException($"Required scene not found: {ScenePath}");
            }

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool closeWhenFinished = !scene.IsValid() || !scene.isLoaded;
            if (closeWhenFinished)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }

            try
            {
                if (FindRoot(scene, GreyboxRootName) == null)
                {
                    BuildGreybox(scene);
                    EditorSceneManager.SaveScene(scene, ScenePath);
                    Debug.Log("Phase 0 foundation and FlightPrototype greybox created.");
                }
            }
            finally
            {
                if (closeWhenFinished && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Powersuit/Phase 0/Build Windows Development Player")]
        public static void BuildWindowsDevelopmentPlayer()
        {
            EnsurePhaseZeroFoundation();

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the project root.");
            string outputPath = Path.Combine(projectRoot, "Build", "Windows", "Powersuit.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            BuildReport report = BuildPipeline.BuildPlayer(
                CreateDevelopmentBuildOptions(outputPath)
            );

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Windows development build failed: {report.summary.result}");
            }

            Debug.Log($"Windows development build created at {outputPath}");
        }

        public static BuildPlayerOptions CreateDevelopmentBuildOptions(
            string outputPath
        )
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException(
                    "A development-build output path is required.",
                    nameof(outputPath)
                );
            }

            return new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/Game");
            foreach (string folder in GameFolders)
            {
                EnsureFolder($"Assets/Game/{folder}");
            }

            EnsureFolder(MaterialsFolder);
            EnsureFolder("Assets/Game/Tests/EditMode");
            EnsureFolder("Assets/Game/Tests/PlayMode");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException($"Unable to create folder: {path}");
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void ConfigureProjectDefaults()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
        }

        private static void BuildGreybox(Scene scene)
        {
            Scene previousActiveScene = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);

            try
            {
                Material groundMaterial = GetOrCreateMaterial(
                    "Greybox Ground", new Color(0.27f, 0.31f, 0.34f));
                Material structureMaterial = GetOrCreateMaterial(
                    "Greybox Structure", new Color(0.52f, 0.57f, 0.61f));
                Material accentMaterial = GetOrCreateMaterial(
                    "Greybox Accent", new Color(0.12f, 0.48f, 0.72f));
                Material startMaterial = GetOrCreateMaterial(
                    "Start Zone", new Color(0.12f, 0.82f, 0.42f), true);
                Material playerMaterial = GetOrCreateMaterial(
                    "Player Placeholder", new Color(0.92f, 0.32f, 0.12f));
                Material markerMaterial = GetOrCreateMaterial(
                    "Hazard Marker", new Color(0.95f, 0.72f, 0.08f));

                GameObject root = new GameObject(GreyboxRootName);
                Transform ground = CreateGroup("Ground Areas", root.transform);
                Transform walls = CreateGroup("Walls", root.transform);
                Transform pillars = CreateGroup("Tall Pillars", root.transform);
                Transform ramps = CreateGroup("Ramps", root.transform);
                Transform platforms = CreateGroup("Elevated Platforms", root.transform);
                Transform startArea = CreateGroup("Marked Start Area", root.transform);

                CreatePrimitive("Start Field", PrimitiveType.Cube, ground, new Vector3(0f, -1f, 10f),
                    new Vector3(70f, 2f, 70f), Quaternion.identity, groundMaterial);
                CreatePrimitive("Mid Field", PrimitiveType.Cube, ground, new Vector3(0f, -1f, 105f),
                    new Vector3(100f, 2f, 70f), Quaternion.identity, groundMaterial);
                CreatePrimitive("Far Field", PrimitiveType.Cube, ground, new Vector3(0f, -1f, 225f),
                    new Vector3(130f, 2f, 90f), Quaternion.identity, groundMaterial);

                CreatePrimitive("Start Left Wall", PrimitiveType.Cube, walls, new Vector3(-35f, 5f, 10f),
                    new Vector3(2f, 12f, 70f), Quaternion.identity, structureMaterial);
                CreatePrimitive("Start Right Wall", PrimitiveType.Cube, walls, new Vector3(35f, 5f, 10f),
                    new Vector3(2f, 12f, 70f), Quaternion.identity, structureMaterial);
                CreatePrimitive("Start Rear Wall", PrimitiveType.Cube, walls, new Vector3(0f, 5f, -25f),
                    new Vector3(70f, 12f, 2f), Quaternion.identity, structureMaterial);
                CreatePrimitive("Mid Left Wall", PrimitiveType.Cube, walls, new Vector3(-50f, 7f, 105f),
                    new Vector3(2f, 16f, 70f), Quaternion.identity, structureMaterial);
                CreatePrimitive("Mid Right Wall", PrimitiveType.Cube, walls, new Vector3(50f, 7f, 105f),
                    new Vector3(2f, 16f, 70f), Quaternion.identity, structureMaterial);
                CreatePrimitive("Far End Wall", PrimitiveType.Cube, walls, new Vector3(0f, 10f, 270f),
                    new Vector3(130f, 22f, 2f), Quaternion.identity, structureMaterial);

                CreatePillar(pillars, "Pillar 01", new Vector3(-22f, 9f, 28f), 18f, structureMaterial, accentMaterial);
                CreatePillar(pillars, "Pillar 02", new Vector3(20f, 14f, 42f), 28f, structureMaterial, accentMaterial);
                CreatePillar(pillars, "Pillar 03", new Vector3(-32f, 19f, 88f), 38f, structureMaterial, accentMaterial);
                CreatePillar(pillars, "Pillar 04", new Vector3(26f, 25f, 108f), 50f, structureMaterial, accentMaterial);
                CreatePillar(pillars, "Pillar 05", new Vector3(-15f, 32f, 130f), 64f, structureMaterial, accentMaterial);
                CreatePillar(pillars, "Pillar 06", new Vector3(38f, 22f, 196f), 44f, structureMaterial, accentMaterial);
                CreatePillar(pillars, "Pillar 07", new Vector3(-40f, 30f, 225f), 60f, structureMaterial, accentMaterial);
                CreatePillar(pillars, "Pillar 08", new Vector3(10f, 40f, 250f), 80f, structureMaterial, accentMaterial);

                CreatePrimitive("Start Ramp", PrimitiveType.Cube, ramps, new Vector3(0f, 2.3f, 30f),
                    new Vector3(14f, 1f, 24f), Quaternion.Euler(-16f, 0f, 0f), accentMaterial);
                CreatePrimitive("Mid Ramp Left", PrimitiveType.Cube, ramps, new Vector3(-28f, 3.2f, 82f),
                    new Vector3(12f, 1f, 28f), Quaternion.Euler(-20f, 15f, 0f), accentMaterial);
                CreatePrimitive("Mid Ramp Right", PrimitiveType.Cube, ramps, new Vector3(25f, 4.4f, 126f),
                    new Vector3(14f, 1f, 32f), Quaternion.Euler(24f, -20f, 0f), accentMaterial);
                CreatePrimitive("Far Ramp", PrimitiveType.Cube, ramps, new Vector3(0f, 5.1f, 205f),
                    new Vector3(18f, 1f, 38f), Quaternion.Euler(-24f, 0f, 0f), accentMaterial);

                CreatePrimitive("Low Platform", PrimitiveType.Cube, platforms, new Vector3(0f, 8f, 58f),
                    new Vector3(24f, 2f, 18f), Quaternion.identity, structureMaterial);
                CreatePrimitive("Mid Platform", PrimitiveType.Cube, platforms, new Vector3(-18f, 16f, 112f),
                    new Vector3(28f, 2f, 22f), Quaternion.identity, structureMaterial);
                CreatePrimitive("High Platform", PrimitiveType.Cube, platforms, new Vector3(25f, 26f, 155f),
                    new Vector3(30f, 2f, 24f), Quaternion.identity, structureMaterial);
                CreatePrimitive("Gap Platform", PrimitiveType.Cube, platforms, new Vector3(0f, 18f, 165f),
                    new Vector3(20f, 2f, 20f), Quaternion.identity, markerMaterial);
                CreatePrimitive("Far High Platform", PrimitiveType.Cube, platforms, new Vector3(-20f, 38f, 238f),
                    new Vector3(32f, 2f, 26f), Quaternion.identity, structureMaterial);

                CreatePrimitive("Start Pad", PrimitiveType.Cylinder, startArea, new Vector3(0f, 0.25f, 0f),
                    new Vector3(8f, 0.25f, 8f), Quaternion.identity, startMaterial, false);
                CreatePrimitive("Start Arch Left", PrimitiveType.Cube, startArea, new Vector3(-6f, 4f, 4f),
                    new Vector3(1f, 8f, 1f), Quaternion.identity, startMaterial);
                CreatePrimitive("Start Arch Right", PrimitiveType.Cube, startArea, new Vector3(6f, 4f, 4f),
                    new Vector3(1f, 8f, 1f), Quaternion.identity, startMaterial);
                CreatePrimitive("Start Arch Top", PrimitiveType.Cube, startArea, new Vector3(0f, 8f, 4f),
                    new Vector3(13f, 1f, 1f), Quaternion.identity, startMaterial);
                CreatePrimitive("Start Direction Marker", PrimitiveType.Cube, startArea, new Vector3(0f, 0.4f, 9f),
                    new Vector3(3f, 0.35f, 6f), Quaternion.Euler(0f, 45f, 0f), markerMaterial, false);

                CreatePlaceholderPlayer(root.transform, playerMaterial, accentMaterial);
                ConfigureCamera(scene);
                ConfigureLighting(scene);
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }
            }
        }

        private static void CreatePillar(
            Transform parent,
            string name,
            Vector3 position,
            float height,
            Material structureMaterial,
            Material accentMaterial)
        {
            CreatePrimitive(name, PrimitiveType.Cylinder, parent, position,
                new Vector3(4f, height * 0.5f, 4f), Quaternion.identity, structureMaterial);
            CreatePrimitive($"{name} Beacon", PrimitiveType.Sphere, parent,
                position + Vector3.up * (height + 1.5f), new Vector3(3f, 3f, 3f),
                Quaternion.identity, accentMaterial);
        }

        private static void CreatePlaceholderPlayer(
            Transform parent,
            Material playerMaterial,
            Material accentMaterial)
        {
            Transform player = CreateGroup(PlayerRootName, parent);
            player.localPosition = new Vector3(0f, 0.25f, 0f);

            CreatePrimitive("Torso", PrimitiveType.Capsule, player, new Vector3(0f, 2.1f, 0f),
                new Vector3(0.9f, 1.15f, 0.65f), Quaternion.identity, playerMaterial, false);
            CreatePrimitive("Helmet", PrimitiveType.Sphere, player, new Vector3(0f, 3.75f, 0f),
                new Vector3(0.75f, 0.75f, 0.75f), Quaternion.identity, accentMaterial, false);
            CreatePrimitive("Left Arm", PrimitiveType.Cube, player, new Vector3(-1f, 2.15f, 0f),
                new Vector3(0.38f, 1.65f, 0.38f), Quaternion.Euler(0f, 0f, -8f), playerMaterial, false);
            CreatePrimitive("Right Arm", PrimitiveType.Cube, player, new Vector3(1f, 2.15f, 0f),
                new Vector3(0.38f, 1.65f, 0.38f), Quaternion.Euler(0f, 0f, 8f), playerMaterial, false);
            CreatePrimitive("Left Leg", PrimitiveType.Cube, player, new Vector3(-0.38f, 0.75f, 0f),
                new Vector3(0.5f, 1.5f, 0.55f), Quaternion.identity, playerMaterial, false);
            CreatePrimitive("Right Leg", PrimitiveType.Cube, player, new Vector3(0.38f, 0.75f, 0f),
                new Vector3(0.5f, 1.5f, 0.55f), Quaternion.identity, playerMaterial, false);
            CreatePrimitive("Power Pack", PrimitiveType.Cube, player, new Vector3(0f, 2.2f, -0.72f),
                new Vector3(1.15f, 1.5f, 0.4f), Quaternion.identity, accentMaterial, false);
        }

        private static void ConfigureCamera(Scene scene)
        {
            GameObject cameraObject = FindRoot(scene, "Main Camera") ?? new GameObject("Main Camera");
            if (cameraObject.scene != scene)
            {
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
            }

            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>() ?? cameraObject.AddComponent<Camera>();
            if (cameraObject.GetComponent<AudioListener>() == null)
            {
                cameraObject.AddComponent<AudioListener>();
            }

            cameraObject.transform.position = new Vector3(28f, 22f, -32f);
            cameraObject.transform.rotation = Quaternion.LookRotation(
                new Vector3(0f, 9f, 70f) - cameraObject.transform.position);
            camera.fieldOfView = 62f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000f;
        }

        private static void ConfigureLighting(Scene scene)
        {
            GameObject lightObject = FindRoot(scene, "Directional Light") ?? new GameObject("Directional Light");
            if (lightObject.scene != scene)
            {
                SceneManager.MoveGameObjectToScene(lightObject, scene);
            }

            Light light = lightObject.GetComponent<Light>() ?? lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            RenderSettings.sun = light;
            RenderSettings.ambientIntensity = 1.1f;
            RenderSettings.fog = false;
        }

        private static Transform CreateGroup(string name, Transform parent)
        {
            GameObject group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        private static GameObject CreatePrimitive(
            string name,
            PrimitiveType primitiveType,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material,
            bool isStatic = true)
        {
            GameObject instance = GameObject.CreatePrimitive(primitiveType);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = localScale;
            instance.isStatic = isStatic;

            Renderer renderer = instance.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }

            return instance;
        }

        private static Material GetOrCreateMaterial(string name, Color color, bool emissive = false)
        {
            string assetPath = $"{MaterialsFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null)
                {
                    throw new InvalidOperationException("No compatible lit shader is available.");
                }

                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, assetPath);
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (emissive && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.5f);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }

            return null;
        }
    }
}
#endif
