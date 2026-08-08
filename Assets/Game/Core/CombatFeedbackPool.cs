using System.Collections.Generic;
using UnityEngine;

public sealed class CombatFeedbackPool : MonoBehaviour
{
    private static CombatFeedbackPool instance;
    public static CombatFeedbackPool Instance => instance;

    private readonly Dictionary<GameObject, Queue<GameObject>> pools = new Dictionary<GameObject, Queue<GameObject>>();
    private readonly Dictionary<GameObject, GameObject> prefabLookup = new Dictionary<GameObject, GameObject>();
    private Transform poolRoot;

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

    private GameObject SpawnInternal(GameObject prefab, Vector3 position, Quaternion rotation)
    {
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
            prefabLookup[obj] = prefab;
        }
        else
        {
            obj.transform.SetParent(poolRoot, false);
            obj.transform.position = position;
            obj.transform.rotation = rotation;
        }

        obj.SetActive(true);
        ResetPooledObject(obj);

        return obj;
    }

    private void RecycleInternal(GameObject obj)
    {
        if (!prefabLookup.TryGetValue(obj, out GameObject prefab) || prefab == null)
        {
            Destroy(obj);
            return;
        }

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

    private void ResetPooledObject(GameObject obj)
    {
        ParticleSystem[] particles = obj.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem ps in particles)
        {
            ps.Clear(true);
            ps.Play(true);
        }

        TrailRenderer[] trails = obj.GetComponentsInChildren<TrailRenderer>(true);
        foreach (TrailRenderer trail in trails)
        {
            trail.Clear();
        }

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        AutoRecycleEffect autoRecycle = obj.GetComponent<AutoRecycleEffect>();
        if (autoRecycle != null)
        {
            autoRecycle.RestartLifetime();
        }
    }

}
