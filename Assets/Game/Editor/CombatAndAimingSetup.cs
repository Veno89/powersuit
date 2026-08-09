#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Powersuit.Editor
{
    public static class CombatAndAimingSetup
    {
        private const string ProjectilePrefabFolder = "Assets/Game/Prefab/Combat";
        private const string ProjectilePrefabPath = ProjectilePrefabFolder + "/PlayerProjectile.prefab";
        private const string AnimatorControllerPath = "Assets/Game/Animation/PowerSuitAnimator.controller";
        private const string EnemyPrefabFolder = "Assets/Game/Prefab/Enemies";
        private const string PlayerPrefabFolder = "Assets/Game/Prefab/Player";
        private const string EnemyPrefabPath = EnemyPrefabFolder + "/EnemyPrototype.prefab";

        [MenuItem("Tools/Powered Suit/Set Up Combat And Aiming")]
        [MenuItem("Tools/Powersuit/Set Up Combat And Aiming")]
        public static void SetUpCombatAndAiming()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Cannot run setup during Play mode.");
                return;
            }

            Debug.Log("=== Starting Powersuit Combat & Aiming Setup ===");

            try
            {
                EnsureDirectories();
                PlayerProjectile projectilePrefab = EnsurePlayerProjectilePrefab();
                EnsureAnimatorParameters();
                SetUpPrefabs(projectilePrefab);
                SetUpSceneObjects(projectilePrefab);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("=== Powersuit Combat & Aiming Setup Completed Successfully ===");
            }
            catch (Exception ex)
            {
                Debug.LogError($"=== Powersuit Combat & Aiming Setup FAILED: {ex.Message} ===\n{ex.StackTrace}");
            }
        }

        private static void EnsureDirectories()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Game/Prefab"))
            {
                AssetDatabase.CreateFolder("Assets/Game", "Prefab");
            }

            if (!AssetDatabase.IsValidFolder(ProjectilePrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets/Game/Prefab", "Combat");
            }
        }

        private static PlayerProjectile EnsurePlayerProjectilePrefab()
        {
            PlayerProjectile existingPrefab = AssetDatabase.LoadAssetAtPath<PlayerProjectile>(ProjectilePrefabPath);
            if (existingPrefab != null)
            {
                return existingPrefab;
            }

            GameObject projObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projObject.name = "PlayerProjectile";
            projObject.transform.localScale = Vector3.one * 0.3f;

            SphereCollider sphereCollider = projObject.GetComponent<SphereCollider>();
            if (sphereCollider != null)
            {
                sphereCollider.isTrigger = true;
            }

            Renderer renderer = projObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.name = "PlayerProjectileMaterial";
                Color bulletColor = new Color(0.2f, 0.85f, 1f, 1f);
                mat.color = bulletColor;
                if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", bulletColor);
                }

                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", bulletColor * 2.5f);
                }

                string matPath = "Assets/Game/Content/Materials/PlayerProjectile.mat";
                if (!AssetDatabase.IsValidFolder("Assets/Game/Content/Materials"))
                {
                    AssetDatabase.CreateFolder("Assets/Game/Content", "Materials");
                }

                AssetDatabase.CreateAsset(mat, matPath);
                renderer.sharedMaterial = mat;
            }

            PlayerProjectile projComp = projObject.AddComponent<PlayerProjectile>();

            GameObject createdPrefab = PrefabUtility.SaveAsPrefabAsset(projObject, ProjectilePrefabPath);
            UnityEngine.Object.DestroyImmediate(projObject);

            return createdPrefab.GetComponent<PlayerProjectile>();
        }

        private static void EnsureAnimatorParameters()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorControllerPath);
            if (controller == null)
            {
                return;
            }

            bool hasIsAiming = false;
            foreach (AnimatorControllerParameter param in controller.parameters)
            {
                if (param.name == "IsAiming")
                {
                    hasIsAiming = true;
                    break;
                }
            }

            if (!hasIsAiming)
            {
                controller.AddParameter("IsAiming", AnimatorControllerParameterType.Bool);
                EditorUtility.SetDirty(controller);
            }
        }

        private static void SetUpSceneObjects(PlayerProjectile projPrefab)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                return;
            }

            // 1. Ensure DamageNumberManager in scene
            DamageNumberManager dmgManager = UnityEngine.Object.FindAnyObjectByType<DamageNumberManager>();
            if (dmgManager == null)
            {
                GameObject managerObj = new GameObject("DamageNumberManager");
                dmgManager = managerObj.AddComponent<DamageNumberManager>();
                Undo.RegisterCreatedObjectUndo(managerObj, "Create DamageNumberManager");
            }

            // 2. Configure Player object in scene
            GameObject playerObj = FindPlayerInScene(activeScene);
            if (playerObj != null)
            {
                ConfigurePlayerGameObject(playerObj, projPrefab);
            }

            // 3. Ensure Test Enemies in scene
            EnsureTestEnemies(activeScene);

            // 4. Clean rebuild all enemy health bars in scene
            DamageableTarget[] targets = UnityEngine.Object.FindObjectsByType<DamageableTarget>(FindObjectsInactive.Include);
            foreach (DamageableTarget target in targets)
            {
                if (target.GetComponent<PowerSuitController>() == null)
                {
                    RebuildEnemyHealthBar(target.gameObject);
                }
            }

            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
        }

        private static GameObject FindPlayerInScene(Scene scene)
        {
            PowerSuitController controller = UnityEngine.Object.FindAnyObjectByType<PowerSuitController>(FindObjectsInactive.Include);
            if (controller != null)
            {
                return controller.gameObject;
            }

            GameObject placeholder = GameObject.Find("Player Placeholder");
            if (placeholder != null)
            {
                return placeholder;
            }

            GameObject player = GameObject.Find("Player");
            if (player != null)
            {
                return player;
            }

            return null;
        }

        private static void EnsureTestEnemies(Scene scene)
        {
            GameObject enemiesRoot = FindRootGameObject(scene, "Test Enemies");
            if (enemiesRoot == null)
            {
                enemiesRoot = new GameObject("Test Enemies");
                if (scene.IsValid() && scene.isLoaded)
                {
                    SceneManager.MoveGameObjectToScene(enemiesRoot, scene);
                }
                Undo.RegisterCreatedObjectUndo(enemiesRoot, "Create Test Enemies Group");
            }

            SimpleEnemy[] existingEnemies = UnityEngine.Object.FindObjectsByType<SimpleEnemy>(FindObjectsInactive.Include);
            if (existingEnemies == null || existingEnemies.Length < 3)
            {
                GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
                Vector3[] spawnPositions = new Vector3[]
                {
                    new Vector3(-6f, 0.25f, 18f),
                    new Vector3(0f, 0.25f, 22f),
                    new Vector3(6f, 0.25f, 20f)
                };

                int needed = 3 - (existingEnemies != null ? existingEnemies.Length : 0);
                int startIndex = existingEnemies != null ? existingEnemies.Length : 0;

                for (int i = 0; i < needed; i++)
                {
                    int idx = startIndex + i;
                    Vector3 spawnPos = spawnPositions[idx % spawnPositions.Length];

                    GameObject enemyInstance;
                    if (enemyPrefab != null)
                    {
                        enemyInstance = PrefabUtility.InstantiatePrefab(enemyPrefab, scene) as GameObject;
                    }
                    else
                    {
                        enemyInstance = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    }

                    enemyInstance.name = $"Test Enemy {idx + 1}";
                    enemyInstance.transform.SetParent(enemiesRoot.transform, false);
                    enemyInstance.transform.position = spawnPos;
                    enemyInstance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

                    RebuildEnemyHealthBar(enemyInstance);
                    Undo.RegisterCreatedObjectUndo(enemyInstance, "Create Test Enemy");
                }
            }

            SimpleEnemy[] allEnemies = UnityEngine.Object.FindObjectsByType<SimpleEnemy>(FindObjectsInactive.Include);
            foreach (SimpleEnemy enemy in allEnemies)
            {
                RebuildEnemyHealthBar(enemy.gameObject);
            }
        }

        private static GameObject FindRootGameObject(Scene scene, string name)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            foreach (GameObject root in roots)
            {
                if (root.name == name)
                {
                    return root;
                }
            }

            return null;
        }

        private static void SetUpPrefabs(PlayerProjectile projPrefab)
        {
            string[] playerPrefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PlayerPrefabFolder });
            foreach (string guid in playerPrefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefabObj = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefabObj != null && prefabObj.GetComponent<PowerSuitController>() != null)
                {
                    GameObject instance = PrefabUtility.InstantiatePrefab(prefabObj) as GameObject;
                    if (instance != null)
                    {
                        ConfigurePlayerGameObject(instance, projPrefab);
                        PrefabUtility.SaveAsPrefabAsset(instance, path);
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }
            }

            string[] enemyPrefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { EnemyPrefabFolder });
            foreach (string guid in enemyPrefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefabObj = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefabObj != null && (prefabObj.GetComponent<SimpleEnemy>() != null || prefabObj.GetComponent<DamageableTarget>() != null))
                {
                    GameObject instance = PrefabUtility.InstantiatePrefab(prefabObj) as GameObject;
                    if (instance != null)
                    {
                        RebuildEnemyHealthBar(instance);
                        PrefabUtility.SaveAsPrefabAsset(instance, path);
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }
            }
        }

        [MenuItem("Tools/Powered Suit/Clean Up Missing Scripts")]
        [MenuItem("Tools/Powersuit/Clean Up Missing Scripts")]
        public static void CleanUpMissingScripts()
        {
            int removedCount = 0;
            GameObject[] sceneObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
            foreach (GameObject go in sceneObjects)
            {
                removedCount += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            }

            Debug.Log($"[Powersuit] Cleaned up {removedCount} missing script component(s) in active scene.");
        }

        private static void ConfigurePlayerGameObject(GameObject playerObj, PlayerProjectile projPrefab)
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(playerObj);

            CharacterController charController = playerObj.GetComponent<CharacterController>();
            if (charController == null)
            {
                charController = Undo.AddComponent<CharacterController>(playerObj);
                charController.height = 2f;
                charController.center = new Vector3(0f, 1f, 0f);
            }

            PowerSuitController controller = playerObj.GetComponent<PowerSuitController>();
            if (controller == null)
            {
                controller = Undo.AddComponent<PowerSuitController>(playerObj);
            }

            PowerSuitWeapon weapon = playerObj.GetComponent<PowerSuitWeapon>();
            if (weapon == null)
            {
                weapon = Undo.AddComponent<PowerSuitWeapon>(playerObj);
            }

            PlayerHealth health = playerObj.GetComponent<PlayerHealth>();
            if (health == null)
            {
                health = Undo.AddComponent<PlayerHealth>(playerObj);
            }

            PowerSuitAnimationDriver animDriver = playerObj.GetComponent<PowerSuitAnimationDriver>();
            if (animDriver == null)
            {
                animDriver = Undo.AddComponent<PowerSuitAnimationDriver>(playerObj);
            }

            ApplyAimingDefaults(controller);

            if (weapon != null)
            {
                if (weapon.ProjectilePrefab == null && projPrefab != null)
                {
                    weapon.ProjectilePrefab = projPrefab;
                }

                if (weapon.MuzzleTransform == null)
                {
                    Transform muzzle = FindOrCreateMuzzleTransform(playerObj.transform);
                    weapon.MuzzleTransform = muzzle;
                }
            }

            EditorUtility.SetDirty(playerObj);
        }

        private static void ApplyAimingDefaults(PowerSuitController controller)
        {
            if (controller == null) return;

            SerializedObject so = new SerializedObject(controller);
            SerializedProperty normalDistProp = so.FindProperty("cameraDistance");
            SerializedProperty normalHeightProp = so.FindProperty("cameraHeight");
            SerializedProperty normalFovProp = so.FindProperty("defaultFieldOfView");
            SerializedProperty collisionPaddingProp = so.FindProperty("cameraCollisionPadding");
            SerializedProperty collisionReleaseProp = so.FindProperty("cameraCollisionReleaseSharpness");
            SerializedProperty lookSharpnessProp = so.FindProperty("cameraLookSharpness");
            SerializedProperty distProp = so.FindProperty("aimCameraDistance");
            SerializedProperty heightProp = so.FindProperty("aimCameraHeight");
            SerializedProperty offsetProp = so.FindProperty("aimShoulderOffset");
            SerializedProperty fovProp = so.FindProperty("aimFieldOfView");
            SerializedProperty speedProp = so.FindProperty("aimTransitionSpeed");

            if (normalDistProp != null) normalDistProp.floatValue = 6f;
            if (normalHeightProp != null) normalHeightProp.floatValue = 1.55f;
            if (normalFovProp != null) normalFovProp.floatValue = 65f;
            if (collisionPaddingProp != null) collisionPaddingProp.floatValue = 0.05f;
            if (collisionReleaseProp != null) collisionReleaseProp.floatValue = 14f;
            if (lookSharpnessProp != null) lookSharpnessProp.floatValue = 28f;
            if (distProp != null) distProp.floatValue = 3.4f;
            if (heightProp != null) heightProp.floatValue = 1.5f;
            if (offsetProp != null) offsetProp.vector3Value = new Vector3(-1.6f, 0.3f, 0f);
            if (fovProp != null) fovProp.floatValue = 58f;
            if (speedProp != null) speedProp.floatValue = 12f;

            so.ApplyModifiedProperties();
        }

        private static Transform FindOrCreateMuzzleTransform(Transform playerTransform)
        {
            Transform existingMuzzle = playerTransform.Find("WeaponMuzzle");
            if (existingMuzzle != null)
            {
                return existingMuzzle;
            }

            Transform rifle = FindChildRecursive(playerTransform, "powersuit_rifle");
            if (rifle != null)
            {
                Transform rifleMuzzle = rifle.Find("WeaponMuzzle");
                if (rifleMuzzle != null)
                {
                    return rifleMuzzle;
                }

                GameObject muzzleObj = new GameObject("WeaponMuzzle");
                muzzleObj.transform.SetParent(rifle, false);
                muzzleObj.transform.localPosition = new Vector3(0f, 0.15f, 0.7f);
                return muzzleObj.transform;
            }

            GameObject defaultMuzzle = new GameObject("WeaponMuzzle");
            defaultMuzzle.transform.SetParent(playerTransform, false);
            defaultMuzzle.transform.localPosition = new Vector3(0.25f, 1.35f, 0.6f);
            return defaultMuzzle.transform;
        }

        private static void RebuildEnemyHealthBar(GameObject enemyObj)
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(enemyObj);

            DamageableTarget target = enemyObj.GetComponent<DamageableTarget>();
            if (target == null)
            {
                target = Undo.AddComponent<DamageableTarget>(enemyObj);
            }

            SimpleEnemy enemyComp = enemyObj.GetComponent<SimpleEnemy>();
            if (enemyComp == null)
            {
                enemyComp = Undo.AddComponent<SimpleEnemy>(enemyObj);
            }

            // Remove any existing EnemyHealthBar components from enemy
            EnemyHealthBar[] existingBars = enemyObj.GetComponentsInChildren<EnemyHealthBar>(true);
            foreach (EnemyHealthBar bar in existingBars)
            {
                UnityEngine.Object.DestroyImmediate(bar.gameObject == enemyObj ? bar : bar.gameObject);
            }

            // Remove any old/orphaned HealthBarUI children
            Transform oldBarChild = enemyObj.transform.Find("HealthBarUI");
            while (oldBarChild != null)
            {
                UnityEngine.Object.DestroyImmediate(oldBarChild.gameObject);
                oldBarChild = enemyObj.transform.Find("HealthBarUI");
            }

            // Create clean HealthBarUI child GameObject WITH RectTransform, Canvas, CanvasScaler, GraphicRaycaster from construction!
            GameObject barObj = new GameObject(
                "HealthBarUI",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster)
            );
            barObj.transform.SetParent(enemyObj.transform, false);

            RectTransform canvasRect = barObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(160f, 18f);
            canvasRect.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            Canvas canvas = barObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 50;

            CanvasGroup group = barObj.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = false;

            // Create Background child WITH RectTransform, CanvasRenderer, Image from construction!
            GameObject bgObj = new GameObject(
                "Background",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)
            );
            bgObj.transform.SetParent(barObj.transform, false);

            RectTransform bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            Image bgImage = bgObj.GetComponent<Image>();
            bgImage.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            bgImage.raycastTarget = false;

            // Create Fill child WITH RectTransform, CanvasRenderer, Image from construction!
            GameObject fillObj = new GameObject(
                "Fill",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)
            );
            fillObj.transform.SetParent(bgObj.transform, false);

            RectTransform fillRect = fillObj.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(1f, 1f);
            fillRect.offsetMax = new Vector2(-1f, -1f);

            Image fillImage = fillObj.GetComponent<Image>();
            fillImage.color = new Color(0.95f, 0.2f, 0.2f, 1f);
            fillImage.raycastTarget = false;

            // Add EnemyHealthBar component and explicitly serialize references
            EnemyHealthBar healthBar = barObj.AddComponent<EnemyHealthBar>();

            SerializedObject so = new SerializedObject(healthBar);
            so.FindProperty("target").objectReferenceValue = target;
            so.FindProperty("healthBarRoot").objectReferenceValue = canvasRect;
            so.FindProperty("backgroundImage").objectReferenceValue = bgImage;
            so.FindProperty("fillImage").objectReferenceValue = fillImage;
            so.FindProperty("fillRectTransform").objectReferenceValue = fillRect;
            so.FindProperty("canvas").objectReferenceValue = canvas;
            so.FindProperty("offset").vector3Value = new Vector3(0f, 2.5f, 0f);
            so.FindProperty("barSize").vector2Value = new Vector2(160f, 18f);
            so.FindProperty("canvasScale").vector3Value = new Vector3(0.01f, 0.01f, 0.01f);
            so.FindProperty("maxDisplayDistance").floatValue = 50f;
            so.ApplyModifiedProperties();

            healthBar.AssignReferences(target, canvasRect, bgImage, fillImage, fillRect, canvas);

            EditorUtility.SetDirty(enemyObj);
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }

                Transform result = FindChildRecursive(child, childName);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
#endif
