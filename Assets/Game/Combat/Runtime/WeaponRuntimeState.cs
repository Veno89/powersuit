using System;

namespace Powersuit.Combat
{
    public interface IWeaponRandomSource
    {
        double NextUnitValue();
    }

    public sealed class SystemWeaponRandomSource : IWeaponRandomSource
    {
        private readonly Random random;

        public SystemWeaponRandomSource()
            : this(new Random())
        {
        }

        public SystemWeaponRandomSource(int seed)
            : this(new Random(seed))
        {
        }

        private SystemWeaponRandomSource(Random random)
        {
            this.random = random;
        }

        public double NextUnitValue()
        {
            return random.NextDouble();
        }
    }

    public enum WeaponFireBlockReason
    {
        None = 0,
        Reloading = 1,
        ManualCycleInProgress = 2,
        FireCadence = 3,
        EmptyMagazine = 4,
        PresentationLocked = 5
    }

    public enum WeaponReloadStartResult
    {
        Started = 0,
        AlreadyReloading = 1,
        InfiniteAmmo = 2,
        MagazineFull = 3,
        NoReserveAmmo = 4,
        ManualCycleInProgress = 5,
        PresentationLocked = 6
    }

    public struct WeaponFireResult
    {
        private WeaponFireResult(
            bool fired,
            WeaponFireBlockReason blockReason,
            float damage,
            bool isCritical,
            int remainingMagazineAmmo
        )
        {
            Fired = fired;
            BlockReason = blockReason;
            Damage = damage;
            IsCritical = isCritical;
            RemainingMagazineAmmo = remainingMagazineAmmo;
        }

        public bool Fired { get; }
        public WeaponFireBlockReason BlockReason { get; }
        public float Damage { get; }
        public bool IsCritical { get; }
        public int RemainingMagazineAmmo { get; }

        public static WeaponFireResult Blocked(
            WeaponFireBlockReason reason,
            int remainingMagazineAmmo
        )
        {
            return new WeaponFireResult(
                fired: false,
                blockReason: reason,
                damage: 0f,
                isCritical: false,
                remainingMagazineAmmo: remainingMagazineAmmo
            );
        }

        internal static WeaponFireResult Successful(
            float damage,
            bool isCritical,
            int remainingMagazineAmmo
        )
        {
            return new WeaponFireResult(
                fired: true,
                blockReason: WeaponFireBlockReason.None,
                damage: damage,
                isCritical: isCritical,
                remainingMagazineAmmo: remainingMagazineAmmo
            );
        }
    }

    /// <summary>
    /// Plain-C# mutable state for one equipped weapon instance.
    /// Unity input, effects, animation, and projectile spawning stay in adapters.
    /// </summary>
    public sealed class WeaponRuntimeState
    {
        private const float TimeEpsilon = 0.00001f;

        private readonly IWeaponRandomSource randomSource;
        private float fireCooldownRemaining;
        private float reloadElapsed;
        private float manualCycleRemaining;
        private bool reloadCommitted;

        public WeaponRuntimeState(
            WeaponRuntimeConfig configuration,
            IWeaponRandomSource randomSource = null
        )
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            configuration.ValidateOrThrow();

            Configuration = configuration;
            this.randomSource = randomSource ?? new SystemWeaponRandomSource();
            CurrentMagazineAmmo = configuration.MagazineCapacity;
            CurrentReserveAmmo = configuration.StartingReserveAmmo;
        }

        public WeaponRuntimeConfig Configuration { get; }
        public int CurrentMagazineAmmo { get; private set; }
        public int CurrentReserveAmmo { get; private set; }
        public bool IsReloading { get; private set; }
        public bool IsManualCycleInProgress => manualCycleRemaining > TimeEpsilon;
        public bool CanStartAutomaticReload =>
            !Configuration.UsesInfiniteAmmo &&
            !IsReloading &&
            !IsManualCycleInProgress &&
            CurrentMagazineAmmo <= 0 &&
            CurrentReserveAmmo > 0;
        public bool HasReloadCommitted => IsReloading && reloadCommitted;
        public float FireCooldownRemaining => fireCooldownRemaining;
        public float ManualCycleRemaining => manualCycleRemaining;
        public float ReloadElapsed => reloadElapsed;
        public float ReloadNormalizedTime =>
            !IsReloading || Configuration.ReloadDurationSeconds <= 0f
                ? 0f
                : Math.Min(1f, reloadElapsed / Configuration.ReloadDurationSeconds);

        public WeaponFireBlockReason CurrentFireBlockReason
        {
            get
            {
                if (IsReloading)
                {
                    return WeaponFireBlockReason.Reloading;
                }

                if (IsManualCycleInProgress)
                {
                    return WeaponFireBlockReason.ManualCycleInProgress;
                }

                if (fireCooldownRemaining > TimeEpsilon)
                {
                    return WeaponFireBlockReason.FireCadence;
                }

                if (!Configuration.UsesInfiniteAmmo && CurrentMagazineAmmo <= 0)
                {
                    return WeaponFireBlockReason.EmptyMagazine;
                }

                return WeaponFireBlockReason.None;
            }
        }

        public event Action AmmunitionChanged;
        public event Action ReloadStarted;
        public event Action<int> ReloadAmmoCommitted;
        public event Action ReloadCompleted;
        public event Action ReloadCancelled;
        public event Action ManualCycleStarted;
        public event Action ManualCycleCompleted;
        public event Action ManualCycleCancelled;
        public event Action<WeaponFireResult> ShotFired;

        public WeaponFireResult TryFire()
        {
            WeaponFireBlockReason blockReason = CurrentFireBlockReason;
            if (blockReason != WeaponFireBlockReason.None)
            {
                return WeaponFireResult.Blocked(blockReason, CurrentMagazineAmmo);
            }

            if (!Configuration.UsesInfiniteAmmo)
            {
                CurrentMagazineAmmo--;
                AmmunitionChanged?.Invoke();
            }

            bool isCritical = RollCriticalHit();
            float resolvedDamage = Configuration.BaseDamage;
            if (isCritical)
            {
                resolvedDamage *= Configuration.CriticalDamageMultiplier;
            }

            fireCooldownRemaining = Configuration.ShotIntervalSeconds;

            if (Configuration.RequiresManualCycle)
            {
                manualCycleRemaining = Configuration.ManualCycleDurationSeconds;
                ManualCycleStarted?.Invoke();
            }

            WeaponFireResult result = WeaponFireResult.Successful(
                resolvedDamage,
                isCritical,
                CurrentMagazineAmmo
            );
            ShotFired?.Invoke(result);
            return result;
        }

        public WeaponReloadStartResult TryStartReload()
        {
            if (IsReloading)
            {
                return WeaponReloadStartResult.AlreadyReloading;
            }

            if (Configuration.UsesInfiniteAmmo)
            {
                return WeaponReloadStartResult.InfiniteAmmo;
            }

            if (IsManualCycleInProgress)
            {
                return WeaponReloadStartResult.ManualCycleInProgress;
            }

            if (CurrentMagazineAmmo >= Configuration.MagazineCapacity)
            {
                return WeaponReloadStartResult.MagazineFull;
            }

            if (CurrentReserveAmmo <= 0)
            {
                return WeaponReloadStartResult.NoReserveAmmo;
            }

            IsReloading = true;
            reloadElapsed = 0f;
            reloadCommitted = false;
            ReloadStarted?.Invoke();

            if (Configuration.ReloadCommitTimeSeconds <= TimeEpsilon)
            {
                CommitReload();
            }

            if (Configuration.ReloadDurationSeconds <= TimeEpsilon)
            {
                CompleteReload();
            }

            return WeaponReloadStartResult.Started;
        }

        public bool CommitReload()
        {
            if (!IsReloading || reloadCommitted)
            {
                return false;
            }

            int roundsNeeded = Configuration.MagazineCapacity - CurrentMagazineAmmo;
            int roundsTransferred = Math.Min(roundsNeeded, CurrentReserveAmmo);

            CurrentMagazineAmmo += roundsTransferred;
            CurrentReserveAmmo -= roundsTransferred;
            reloadCommitted = true;

            if (roundsTransferred > 0)
            {
                AmmunitionChanged?.Invoke();
            }

            ReloadAmmoCommitted?.Invoke(roundsTransferred);
            return true;
        }

        public bool CompleteReload()
        {
            if (!IsReloading)
            {
                return false;
            }

            if (!reloadCommitted)
            {
                CommitReload();
            }

            IsReloading = false;
            reloadElapsed = 0f;
            ReloadCompleted?.Invoke();
            return true;
        }

        public bool CancelReload()
        {
            if (!IsReloading)
            {
                return false;
            }

            IsReloading = false;
            reloadElapsed = 0f;
            ReloadCancelled?.Invoke();
            return true;
        }

        public bool CompleteManualCycle()
        {
            if (!IsManualCycleInProgress)
            {
                return false;
            }

            manualCycleRemaining = 0f;
            ManualCycleCompleted?.Invoke();
            return true;
        }

        public int AddReserveAmmo(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount),
                    "Ammo pickup amount cannot be negative."
                );
            }

            if (Configuration.UsesInfiniteAmmo || amount == 0)
            {
                return 0;
            }

            int availableSpace = Configuration.MaximumReserveAmmo - CurrentReserveAmmo;
            int accepted = Math.Min(amount, Math.Max(0, availableSpace));
            if (accepted > 0)
            {
                CurrentReserveAmmo += accepted;
                AmmunitionChanged?.Invoke();
            }

            return accepted;
        }

        public void Reset()
        {
            CurrentMagazineAmmo = Configuration.MagazineCapacity;
            CurrentReserveAmmo = Configuration.StartingReserveAmmo;
            ResetTransientState();
            AmmunitionChanged?.Invoke();
        }

        /// <summary>
        /// Cancels in-progress actions and cadence without changing ammunition.
        /// Cancellation events keep presentation adapters synchronized.
        /// </summary>
        public void ResetTransientState()
        {
            bool cancelledReload = IsReloading;
            bool cancelledManualCycle = IsManualCycleInProgress;

            fireCooldownRemaining = 0f;
            reloadElapsed = 0f;
            manualCycleRemaining = 0f;
            reloadCommitted = false;
            IsReloading = false;

            if (cancelledReload)
            {
                ReloadCancelled?.Invoke();
            }

            if (cancelledManualCycle)
            {
                ManualCycleCancelled?.Invoke();
            }
        }

        /// <summary>
        /// Cancels reload and manual-action presentation when this weapon is
        /// unequipped, while preserving ammunition and cadence. Preserving the
        /// fire cooldown prevents rapid slot swapping from bypassing a
        /// weapon's authored rate of fire.
        /// </summary>
        public void PrepareForUnequip()
        {
            bool cancelledReload = IsReloading;
            bool cancelledManualCycle = IsManualCycleInProgress;

            reloadElapsed = 0f;
            manualCycleRemaining = 0f;
            reloadCommitted = false;
            IsReloading = false;

            if (cancelledReload)
            {
                ReloadCancelled?.Invoke();
            }

            if (cancelledManualCycle)
            {
                ManualCycleCancelled?.Invoke();
            }
        }

        public void Advance(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaSeconds),
                    "Elapsed time must be a finite non-negative value."
                );
            }

            if (fireCooldownRemaining > 0f)
            {
                fireCooldownRemaining = Math.Max(0f, fireCooldownRemaining - deltaSeconds);
            }

            if (manualCycleRemaining > 0f)
            {
                manualCycleRemaining = Math.Max(0f, manualCycleRemaining - deltaSeconds);
                if (manualCycleRemaining <= TimeEpsilon)
                {
                    manualCycleRemaining = 0f;
                    ManualCycleCompleted?.Invoke();
                }
            }

            if (!IsReloading)
            {
                return;
            }

            reloadElapsed += deltaSeconds;

            if (
                !reloadCommitted &&
                reloadElapsed + TimeEpsilon >= Configuration.ReloadCommitTimeSeconds
            )
            {
                CommitReload();
            }

            if (
                IsReloading &&
                reloadElapsed + TimeEpsilon >= Configuration.ReloadDurationSeconds
            )
            {
                CompleteReload();
            }
        }

        private bool RollCriticalHit()
        {
            if (Configuration.CriticalChance <= 0f)
            {
                return false;
            }

            if (Configuration.CriticalChance >= 1f)
            {
                return true;
            }

            float roll = (float)randomSource.NextUnitValue();
            if (float.IsNaN(roll))
            {
                roll = 1f;
            }

            roll = Math.Max(0f, Math.Min(1f, roll));
            return roll < Configuration.CriticalChance;
        }
    }

    public enum WeaponSelectionRequestResult
    {
        Queued = 0,
        AlreadyEquipped = 1,
        InvalidSlot = 2
    }

    /// <summary>
    /// Engine-independent inventory state for a compact fixed weapon loadout.
    /// Every slot owns its own ammo/cadence state; Unity adapters only select
    /// definitions, route input, and present the equipped slot.
    /// </summary>
    public sealed class WeaponLoadoutState
    {
        private readonly WeaponRuntimeState[] slots;
        private int pendingIndex = -1;

        public WeaponLoadoutState(
            WeaponRuntimeConfig[] configurations,
            int startingIndex = 0
        )
        {
            if (configurations == null)
            {
                throw new ArgumentNullException(nameof(configurations));
            }

            if (configurations.Length == 0)
            {
                throw new ArgumentException(
                    "A weapon loadout requires at least one slot.",
                    nameof(configurations)
                );
            }

            if (startingIndex < 0 || startingIndex >= configurations.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(startingIndex));
            }

            slots = new WeaponRuntimeState[configurations.Length];
            for (int index = 0; index < configurations.Length; index++)
            {
                WeaponRuntimeConfig configuration = configurations[index];
                if (configuration == null)
                {
                    throw new ArgumentException(
                        "Weapon loadout configurations cannot contain null slots.",
                        nameof(configurations)
                    );
                }

                slots[index] = new WeaponRuntimeState(configuration);
            }

            EquippedIndex = startingIndex;
        }

        public int SlotCount => slots.Length;
        public int EquippedIndex { get; private set; }
        public int PendingIndex => pendingIndex;
        public bool HasPendingSelection => pendingIndex >= 0;
        public WeaponRuntimeState EquippedWeapon => slots[EquippedIndex];

        public WeaponRuntimeState GetWeapon(int index)
        {
            if (index < 0 || index >= slots.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return slots[index];
        }

        public WeaponSelectionRequestResult RequestSelection(int index)
        {
            if (index < 0 || index >= slots.Length)
            {
                return WeaponSelectionRequestResult.InvalidSlot;
            }

            if (index == EquippedIndex)
            {
                pendingIndex = -1;
                return WeaponSelectionRequestResult.AlreadyEquipped;
            }

            pendingIndex = index;
            return WeaponSelectionRequestResult.Queued;
        }

        public WeaponSelectionRequestResult RequestNext()
        {
            return RequestSelection((EquippedIndex + 1) % slots.Length);
        }

        public bool TryCommitPendingSelection(bool canSwitch)
        {
            if (!canSwitch || pendingIndex < 0)
            {
                return false;
            }

            EquippedIndex = pendingIndex;
            pendingIndex = -1;
            return true;
        }

        public void CancelPendingSelection()
        {
            pendingIndex = -1;
        }

        /// <summary>
        /// Advances only holstered slots. The equipped weapon remains owned by
        /// PowerSuitWeapon, preventing a doubled cadence/reload tick.
        /// </summary>
        public void AdvanceInactive(float deltaSeconds)
        {
            for (int index = 0; index < slots.Length; index++)
            {
                if (index != EquippedIndex)
                {
                    slots[index].Advance(deltaSeconds);
                }
            }
        }

        public void ResetTransientStates()
        {
            pendingIndex = -1;
            for (int index = 0; index < slots.Length; index++)
            {
                slots[index].ResetTransientState();
            }
        }
    }
}
