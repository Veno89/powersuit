namespace Powersuit.Combat
{
    /// <summary>
    /// Implemented by gameplay adapters that can receive impulses or sustained
    /// pulls, such as the future void-orb effect.
    /// </summary>
    public interface IExternalForceReceiver
    {
        bool CanReceiveExternalForce { get; }

        void ApplyExternalForce(CombatVector3 force, object source);
    }
}
