using System.Collections.Generic;
using UnityEngine;

public sealed class CombatFeedbackPool : MonoBehaviour
{
    public readonly struct Statistics
    {
        public Statistics(
            int prefabPoolCount,
            int activeCount,
            int inactiveCount,
            int peakActiveCount,
            int activeProjectileCount,
            int peakActiveProjectileCount,
            long spawnCount,
            long reusedSpawnCount,
            long runtimeInstantiationCount,
            long prewarmedInstantiationCount,
            long recycleCount,
            long destroyedUntrackedCount
        )
        {
            PrefabPoolCount = prefabPoolCount;
            ActiveCount = activeCount;
            InactiveCount = inactiveCount;
            PeakActiveCount = peakActiveCount;
            ActiveProjectileCount = activeProjectileCount;
            PeakActiveProjectileCount = peakActiveProjectileCount;
            SpawnCount = spawnCount;
            ReusedSpawnCount = reusedSpawnCount;
            RuntimeInstantiationCount = runtimeInstantiationCount;
            PrewarmedInstantiationCount = prewarmedInstantiationCount;
            RecycleCount = recycleCount;
            DestroyedUntrackedCount = destroyedUntrackedCount;
        }

        public int PrefabPoolCount { get; }
        public int ActiveCount { get; }
        public int InactiveCount { get; }
        public int PeakActiveCount { get; }
        public int ActiveProjectileCount { get; }
        public int PeakActiveProjectileCount { get; }
        public long SpawnCount { get; }
        public long ReusedSpawnCount { get; }
        public long RuntimeInstantiationCount { get; }
        public long PrewarmedInstantiationCount { get; }
        public long RecycleCount { get; }
        public long DestroyedUntrackedCount { get; }
    }

    private sealed class PooledInstanceData
    {
        public GameObject Prefab;
        public bool IsProjectile;
        public ICombatPoolable[] Lifecycle;
        public ParticleSystem[] Particles;
        public TrailRenderer[] Trails;
        public Rigidbody Rigidbody;
        public AutoRecycleEffect AutoRecycle;
    }

    private static CombatFeedbackPool instance;
    public static CombatFeedbackPool Instance => instance;

    private readonly Dictionary<GameObject, Queue<GameObject>> pools = new Dictionary<GameObject, Queue<GameObject>>();
    private readonly Dictionary<GameObject, PooledInstanceData> instanceLookup = new Dictionary<GameObject, PooledInstanceData>();
    private readonly HashSet<GameObject> activeObjects = new HashSet<GameObject>();
    private Transform poolRoot;
    private int peakActiveCount;
    private int activeProjectileCount;
    private int peakActiveProjectileCount;
    private long spawnCount;
    private long reusedSpawnCount;
    private long runtimeInstantiationCount;
    private long prewarmedInstantiationCount;
    private long recycleCount;
    private long destroyedUntrackedCount;

    public int ActiveCount => activeObjects.Count;
    public int PeakActiveCount => peakActiveCount;

    public int InactiveCount
    {
        get
        {
            int count = 0;
            foreach (Queue<GameObject> queue in pools.Values)
            {
                count += queue.Count;
            }
            return count;
        }
    }

    /// <summary>
    /// Returns a value-only snapshot suitable for a low-frequency developer
    /// overlay or profiler marker. Reading it performs no managed allocation.
    /// </summary>
    public Statistics CurrentStatistics => new Statistics(
        pools.Count,
        activeObjects.Count,
        InactiveCount,
        peakActiveCount,
        activeProjectileCount,
        peakActiveProjectileCount,
        spawnCount,
        reusedSpawnCount,
        runtimeInstantiationCount,
        prewarmedInstantiationCount,
        recycleCount,
        destroyedUntrackedCount
    );

    public static bool TryGetStatistics(out Statistics statistics)
    {
        if (instance == null)
        {
            statistics = default;
            return false;
        }

        statistics = instance.CurrentStatistics;
        return true;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        poolRoot = transform;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
        {
            return null;
        }

        if (instance == null)
        {
            GameObject poolObj = new GameObject("CombatFeedbackPool");
            instance = poolObj.AddComponent<CombatFeedbackPool>();
        }

        return instance.SpawnInternal(prefab, position, rotation);
    }

    public static void Recycle(GameObject instanceObj)
    {
        if (instanceObj == null || instance == null)
        {
            if (instanceObj != null)
            {
                Destroy(instanceObj);
            }
            return;
        }

        instance.RecycleInternal(instanceObj);
    }

    public static void Prewarm(GameObject prefab, int count)
    {
        if (prefab == null || count <= 0)
        {
            return;
        }

        if (instance == null)
        {
            GameObject poolObj = new GameObject("CombatFeedbackPool");
            instance = poolObj.AddComponent<CombatFeedbackPool>();
        }

        instance.PrewarmInternal(prefab, count);
    }

    private GameObject SpawnInternal(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        spawnCount++;
        if (!pools.TryGetValue(prefab, out Queue<GameObject> pool))
        {
            pool = new Queue<GameObject>();
            pools[prefab] = pool;
        }

        GameObject obj = null;
        while (pool.Count > 0)
        {
            obj = pool.Dequeue();
            if (obj != null)
            {
                break;
            }
        }

        if (obj == null)
        {
            obj = Instantiate(prefab, position, rotation, poolRoot);
            runtimeInstantiationCount++;
            instanceLookup[obj] = CreateInstanceData(obj, prefab);
        }
        else
        {
            reusedSpawnCount++;
            obj.transform.SetParent(poolRoot, false);
            obj.transform.position = position;
            obj.transform.rotation = rotation;
        }

        obj.SetActive(true);
        ResetPooledObject(obj);
        activeObjects.Add(obj);
        peakActiveCount = Mathf.Max(peakActiveCount, activeObjects.Count);
        if (
            instanceLookup.TryGetValue(obj, out PooledInstanceData spawnedData) &&
            spawnedData.IsProjectile
        )
        {
            activeProjectileCount++;
            peakActiveProjectileCount = Mathf.Max(
                peakActiveProjectileCount,
                activeProjectileCount
            );
        }
        InvokeSpawned(obj);

        return obj;
    }

    private void RecycleInternal(GameObject obj)
    {
        if (
            !instanceLookup.TryGetValue(obj, out PooledInstanceData data) ||
            data.Prefab == null
        )
        {
            destroyedUntrackedCount++;
            Destroy(obj);
            return;
        }

        GameObject prefab = data.Prefab;

        if (!activeObjects.Remove(obj))
        {
            return;
        }

        recycleCount++;
        if (data.IsProjectile)
        {
            activeProjectileCount = Mathf.Max(0, activeProjectileCount - 1);
        }

        InvokeRecycled(obj);

        obj.SetActive(false);
        obj.transform.SetParent(poolRoot, false);

        if (pools.TryGetValue(prefab, out Queue<GameObject> pool))
        {
            pool.Enqueue(obj);
        }
        else
        {
            pool = new Queue<GameObject>();
            pool.Enqueue(obj);
            pools[prefab] = pool;
        }
    }

    private void PrewarmInternal(GameObject prefab, int targetInactiveCount)
    {
        if (!pools.TryGetValue(prefab, out Queue<GameObject> pool))
        {
            pool = new Queue<GameObject>();
            pools[prefab] = pool;
        }

        while (pool.Count < targetInactiveCount)
        {
            GameObject obj = Instantiate(prefab, poolRoot);
            prewarmedInstantiationCount++;
            obj.name = prefab.name;
            instanceLookup[obj] = CreateInstanceData(obj, prefab);
            InvokeRecycled(obj);
            obj.SetActive(false);
            pool.Enqueue(obj);
        }
    }

    private static PooledInstanceData CreateInstanceData(
        GameObject obj,
        GameObject prefab
    )
    {
        MonoBehaviour[] behaviours = obj.GetComponentsInChildren<MonoBehaviour>(true);
        List<ICombatPoolable> lifecycle = new List<ICombatPoolable>(behaviours.Length);
        bool isProjectile = false;
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is ICombatPoolable poolable)
            {
                lifecycle.Add(poolable);
                isProjectile |= poolable is ICombatProjectilePoolable;
            }
        }

        return new PooledInstanceData
        {
            Prefab = prefab,
            IsProjectile = isProjectile,
            Lifecycle = lifecycle.ToArray(),
            Particles = obj.GetComponentsInChildren<ParticleSystem>(true),
            Trails = obj.GetComponentsInChildren<TrailRenderer>(true),
            Rigidbody = obj.GetComponent<Rigidbody>(),
            AutoRecycle = obj.GetComponent<AutoRecycleEffect>()
        };
    }

    private void InvokeSpawned(GameObject obj)
    {
        if (!instanceLookup.TryGetValue(obj, out PooledInstanceData data))
        {
            return;
        }

        foreach (ICombatPoolable poolable in data.Lifecycle)
        {
            poolable.OnPoolSpawned();
        }
    }

    private void InvokeRecycled(GameObject obj)
    {
        if (!instanceLookup.TryGetValue(obj, out PooledInstanceData data))
        {
            return;
        }

        foreach (ICombatPoolable poolable in data.Lifecycle)
        {
            poolable.OnPoolRecycled();
        }
    }

    private void ResetPooledObject(GameObject obj)
    {
        if (!instanceLookup.TryGetValue(obj, out PooledInstanceData data))
        {
            return;
        }

        foreach (ParticleSystem ps in data.Particles)
        {
            ps.Clear(true);
            ps.Play(true);
        }

        foreach (TrailRenderer trail in data.Trails)
        {
            trail.Clear();
        }

        Rigidbody rb = data.Rigidbody;
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        AutoRecycleEffect autoRecycle = data.AutoRecycle;
        if (autoRecycle != null)
        {
            autoRecycle.RestartLifetime();
        }
    }

}
