namespace Powersuit.Combat
{
    /// <summary>
    /// Optional combat response implemented by enemies which can have an
    /// attack interrupted without coupling weapons to a concrete AI adapter.
    /// </summary>
    public interface IStaggerReceiver
    {
        bool CanReceiveStagger { get; }
        bool TryApplyStagger(float durationSeconds);
    }
}
