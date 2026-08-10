/// <summary>
/// Optional lifecycle contract for objects managed by CombatFeedbackPool.
/// Implementations must restore every transient value they own so a reused
/// instance behaves exactly like a newly spawned one.
/// </summary>
public interface ICombatPoolable
{
    void OnPoolSpawned();
    void OnPoolRecycled();
}

/// <summary>
/// Optional marker used by pool diagnostics to count active combat
/// projectiles without component searches or gameplay-assembly dependencies.
/// </summary>
public interface ICombatProjectilePoolable : ICombatPoolable
{
}
