using UnityEngine;

public sealed class AutoRecycleEffect : MonoBehaviour
{
    [SerializeField] private float duration = 1f;

    private float timer;

    public void SetDuration(float value)
    {
        duration = Mathf.Max(0.01f, value);
        timer = duration;
    }

    public void RestartLifetime()
    {
        timer = duration;
    }

    private void OnEnable()
    {
        timer = duration;
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            CombatFeedbackPool.Recycle(gameObject);
        }
    }
}
