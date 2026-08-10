namespace Powersuit.Combat
{
    /// <summary>
    /// Central scope-eligibility policy shared by authored weapon data and
    /// presentation adapters. Magnified scopes are deliberately restricted to
    /// the precision-rifle class even if another asset is misconfigured.
    /// </summary>
    public static class WeaponScopeEligibility
    {
        public static bool CanUseMagnifiedScope(
            WeaponClass weaponClass,
            bool authoredScopeSupport
        )
        {
            return authoredScopeSupport &&
                   weaponClass == WeaponClass.PrecisionRifle;
        }
    }
}
