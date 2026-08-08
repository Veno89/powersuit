#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Powersuit.Editor
{
    public static class CombatFeedbackSetup
    {
        private const string CombatPrefabFolder = "Assets/Game/Prefab/Combat";
        private const string MaterialsFolder = "Assets/Game/Content/Materials";
        private const string EnemyPrefabFolder = "Assets/Game/Prefab/Enemies";
        private const string PlayerPrefabFolder = "Assets/Game/Prefab/Player";
        private const string PlayerProjectilePrefabPath = CombatPrefabFolder + "/PlayerProjectile.prefab";

        [MenuItem("Tools/Powered Suit/Set Up Combat Feedback")]
        [MenuItem("Tools/Powersuit/Set Up Combat Feedback")]
        public static void SetUpCombatFeedback()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Powersuit] Cannot run combat feedback setup during Play mode.");
                return;
            }

            Debug.Log("=== Starting Powersuit Combat Feedback Setup ===");

            try
            {
                EnsureFolders();
                Material enemyImpactMat = EnsureMaterial("EnemyImpact.mat", new Color(1f, 0.45f, 0.1f, 1f), true);
                Material envImpactMat = EnsureMaterial("EnvironmentImpact.mat", new Color(0.9f, 0.85f, 0.6f, 1f), true);
                Material muzzleFlashMat = EnsureMaterial("MuzzleFlash.mat", new Color(0.2f, 0.85f, 1f, 1f), true);

                GameObject enemyImpactPrefab = EnsureParticleEffectPrefab("EnemyImpactEffect.prefab", enemyImpactMat, 25, 0.25f, 6f);
                GameObject envImpactPrefab = EnsureParticleEffectPrefab("EnvironmentImpactEffect.prefab", envImpactMat, 15, 0.2f, 4f);
                GameObject muzzleFlashPrefab = EnsureParticleEffectPrefab("MuzzleFlashEffect.prefab", muzzleFlashMat, 18, 0.1f, 2f);

                PlayerProjectile projPrefab = EnsurePlayerProjectilePrefab(enemyImpactPrefab, envImpactPrefab);
                SetUpPlayerPrefabsAndScene(projPrefab, muzzleFlashPrefab);
                SetUpEnemyPrefabsAndScene();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("=== Powersuit Combat Feedback Setup Completed Successfully ===");
            }
            catch (Exception ex)
            {
                Debug.LogError($"=== Powersuit Combat Feedback Setup FAILED: {ex.Message} ===\n{ex.StackTrace}");
            }
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Game/Prefab"))
            {
                AssetDatabase.CreateFolder("Assets/Game", "Prefab");
            }
            if (!AssetDatabase.IsValidFolder(CombatPrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets/Game/Prefab", "Combat");
            }
            if (!AssetDatabase.IsValidFolder("Assets/Game/Content"))
            {
                AssetDatabase.CreateFolder("Assets/Game", "Content");
            }
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder("Assets/Game/Content", "Materials");
            }
        }

        private static Material EnsureMaterial(string filename, Color color, bool isEmissive)
        {
            string path = $"{MaterialsFolder}/{filename}";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
            {
                return mat;
            }

            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            mat.name = filename.Replace(".mat", "");
            mat.color = color;

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }

            if (isEmissive && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 2.5f);
            }

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static GameObject EnsureParticleEffectPrefab(string filename, Material mat, int burstCount, float lifetime, float speed)
        {
            string path = $"{CombatPrefabFolder}/{filename}";
            GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existingPrefab != null)
            {
                return existingPrefab;
            }

            GameObject effectObj = new GameObject(filename.Replace(".prefab", ""));
            ParticleSystem ps = effectObj.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startColor = mat.color;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)burstCount) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;

            ParticleSystemRenderer psRend = effectObj.GetComponent<ParticleSystemRenderer>();
            psRend.sharedMaterial = mat;

            AutoRecycleEffect autoRecycle = effectObj.AddComponent<AutoRecycleEffect>();
            autoRecycle.SetDuration(lifetime + 0.1f);

            GameObject created = PrefabUtility.SaveAsPrefabAsset(effectObj, path);
            UnityEngine.Object.DestroyImmediate(effectObj);
            return created;
        }

        private static PlayerProjectile EnsurePlayerProjectilePrefab(GameObject enemyImpactPrefab, GameObject envImpactPrefab)
        {
            PlayerProjectile proj = AssetDatabase.LoadAssetAtPath<PlayerProjectile>(PlayerProjectilePrefabPath);
            if (proj != null)
            {
                SerializedObject so = new SerializedObject(proj);
                SerializedProperty enemyImpProp = so.FindProperty("enemyImpactPrefab");
                SerializedProperty envImpProp = so.FindProperty("environmentImpactPrefab");

                if (enemyImpProp != null) enemyImpProp.objectReferenceValue = enemyImpactPrefab;
                if (envImpProp != null) envImpProp.objectReferenceValue = envImpactPrefab;

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(proj);
                return proj;
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
                Material mat = EnsureMaterial("PlayerProjectile.mat", new Color(0.2f, 0.85f, 1f, 1f), true);
                renderer.sharedMaterial = mat;
            }

            proj = projObject.AddComponent<PlayerProjectile>();
            SerializedObject projSO = new SerializedObject(proj);
            projSO.FindProperty("enemyImpactPrefab").objectReferenceValue = enemyImpactPrefab;
            projSO.FindProperty("environmentImpactPrefab").objectReferenceValue = envImpactPrefab;
            projSO.ApplyModifiedProperties();

            GameObject createdPrefab = PrefabUtility.SaveAsPrefabAsset(projObject, PlayerProjectilePrefabPath);
            UnityEngine.Object.DestroyImmediate(projObject);
            return createdPrefab.GetComponent<PlayerProjectile>();
        }

        private static void SetUpPlayerPrefabsAndScene(PlayerProjectile projPrefab, GameObject muzzleFlashPrefab)
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
                        ConfigurePlayerComponents(instance, projPrefab, muzzleFlashPrefab);
                        PrefabUtility.SaveAsPrefabAsset(instance, path);
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isLoaded)
            {
                PowerSuitController[] sceneControllers = UnityEngine.Object.FindObjectsByType<PowerSuitController>(FindObjectsInactive.Include);
                foreach (PowerSuitController ctrl in sceneControllers)
                {
                    ConfigurePlayerComponents(ctrl.gameObject, projPrefab, muzzleFlashPrefab);
                }

                EditorSceneManager.MarkSceneDirty(activeScene);
                EditorSceneManager.SaveScene(activeScene);
            }
        }

        private static void ConfigurePlayerComponents(GameObject playerObj, PlayerProjectile projPrefab, GameObject muzzleFlashPrefab)
        {
            PowerSuitWeapon weapon = playerObj.GetComponent<PowerSuitWeapon>();
            if (weapon != null)
            {
                SerializedObject so = new SerializedObject(weapon);
                so.FindProperty("projectilePrefab").objectReferenceValue = projPrefab;
                so.FindProperty("muzzleFlashPrefab").objectReferenceValue = muzzleFlashPrefab;
                so.ApplyModifiedProperties();
            }

            ReticleHitMarker marker = playerObj.GetComponent<ReticleHitMarker>();
            if (marker == null)
            {
                Undo.AddComponent<ReticleHitMarker>(playerObj);
            }

            EditorUtility.SetDirty(playerObj);
        }

        private static void SetUpEnemyPrefabsAndScene()
        {
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
                        ConfigureEnemyComponents(instance);
                        PrefabUtility.SaveAsPrefabAsset(instance, path);
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isLoaded)
            {
                DamageableTarget[] targets = UnityEngine.Object.FindObjectsByType<DamageableTarget>(FindObjectsInactive.Include);
                foreach (DamageableTarget target in targets)
                {
                    if (target.GetComponent<PowerSuitController>() == null)
                    {
                        ConfigureEnemyComponents(target.gameObject);
                    }
                }

                EditorSceneManager.MarkSceneDirty(activeScene);
                EditorSceneManager.SaveScene(activeScene);
            }
        }

        private static void ConfigureEnemyComponents(GameObject enemyObj)
        {
            EnsureEnemyVisualRoot(enemyObj);

            EnemyHitReaction reaction = enemyObj.GetComponent<EnemyHitReaction>();
            if (reaction == null)
            {
                Undo.AddComponent<EnemyHitReaction>(enemyObj);
            }

            EditorUtility.SetDirty(enemyObj);
        }

        private static Transform EnsureEnemyVisualRoot(GameObject enemyObj)
        {
            Transform existing = enemyObj.transform.Find("VisualRoot");
            if (existing != null)
            {
                return existing;
            }

            MeshFilter rootFilter = enemyObj.GetComponent<MeshFilter>();
            MeshRenderer rootRenderer = enemyObj.GetComponent<MeshRenderer>();
            if (rootFilter == null || rootRenderer == null)
            {
                return null;
            }

            GameObject visualObject = new GameObject("VisualRoot");
            visualObject.transform.SetParent(enemyObj.transform, false);

            MeshFilter visualFilter = visualObject.AddComponent<MeshFilter>();
            visualFilter.sharedMesh = rootFilter.sharedMesh;

            MeshRenderer visualRenderer = visualObject.AddComponent<MeshRenderer>();
            visualRenderer.sharedMaterials = rootRenderer.sharedMaterials;
            visualRenderer.shadowCastingMode = rootRenderer.shadowCastingMode;
            visualRenderer.receiveShadows = rootRenderer.receiveShadows;
            visualRenderer.lightProbeUsage = rootRenderer.lightProbeUsage;
            visualRenderer.reflectionProbeUsage = rootRenderer.reflectionProbeUsage;
            visualRenderer.renderingLayerMask = rootRenderer.renderingLayerMask;

            UnityEngine.Object.DestroyImmediate(rootRenderer);
            UnityEngine.Object.DestroyImmediate(rootFilter);
            return visualObject.transform;
        }
    }
}
#endif
