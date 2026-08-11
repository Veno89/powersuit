#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using Powersuit.Abilities.UnityAdapters;
using Powersuit.Core;
using Powersuit.Enemies.UnityAdapters;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Opt-in Development Build performance harness. It is created only when the
/// player is launched with -powersuit-soak, leaves normal gameplay untouched,
/// and writes one machine-readable report after a bounded stress run.
/// </summary>
[DefaultExecutionOrder(10000)]
public sealed class PowerSuitPerformanceSoakRunner : MonoBehaviour
{
    private const int MaximumRecordedSamples = 900000;
    private const double NanosecondsToMilliseconds = 0.000001d;
    private static bool hasCreatedRunner;

    [Serializable]
    private sealed class MetricReport
    {
        public bool available;
        public int samples;
        public int droppedSamples;
        public double average;
        public double maximum;
        public double p50;
        public double p95;
        public double p99;
    }

    [Serializable]
    private sealed class PoolMissReport
    {
        public string prefab;
        public long runtimeInstantiations;
    }

    [Serializable]
    private sealed class SoakReport
    {
        public string schema = "powersuit.performance-soak.v1";
        public string timestampUtc;
        public string unityVersion;
        public string platform;
        public string graphicsDevice;
        public int targetFrameRate;
        public int durationSeconds;
        public int warmupSeconds;
        public int requestedEnemyCap;
        public int peakActiveEnemies;
        public int totalEnemiesSpawned;
        public long steadyStatePoolInstantiations;
        public long steadyStatePoolSpawns;
        public PoolMissReport[] steadyStatePoolMisses;
        public int peakPooledObjects;
        public int peakPooledProjectiles;
        public int loggedErrors;
        public bool passed;
        public string failureReason;
        public MetricReport frameTimeMilliseconds;
        public MetricReport mainThreadMilliseconds;
        public MetricReport cpuFrameMilliseconds;
        public MetricReport gpuFrameMilliseconds;
        public MetricReport gcAllocatedBytesPerFrame;
        public MetricReport mainThreadManagedAllocatedBytesPerFrame;
        public MetricReport drawCallsPerFrame;
    }

    private PerformanceSoakOptions options;
    private int loggedErrors;
    private int peakActiveEnemies;
    private ProfilerRecorder mainThreadRecorder;
    private ProfilerRecorder gcAllocatedRecorder;
    private ProfilerRecorder drawCallsRecorder;
    private bool profilerWasEnabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void TryCreateFromCommandLine()
    {
        if (hasCreatedRunner)
        {
            return;
        }

        if (!PerformanceSoakOptions.TryParse(
                Environment.GetCommandLineArgs(),
                out PerformanceSoakOptions parsed,
                out string error))
        {
            Debug.LogError("[PowerSuitPerformance] " + error);
            return;
        }

        if (!parsed.Enabled)
        {
            return;
        }

        hasCreatedRunner = true;
        GameObject host = new GameObject("PowerSuitPerformanceSoakRunner");
        DontDestroyOnLoad(host);
        PowerSuitPerformanceSoakRunner runner =
            host.AddComponent<PowerSuitPerformanceSoakRunner>();
        runner.options = parsed;
    }

    private IEnumerator Start()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = options.TargetFrameRate;
        Application.logMessageReceived += HandleLog;

        string fatalError = string.Empty;
        PowerSuitDemoBootstrap bootstrap = null;
        float bootstrapDeadline = Time.realtimeSinceStartup + 15f;
        while (bootstrap == null && Time.realtimeSinceStartup < bootstrapDeadline)
        {
            bootstrap = FindAnyObjectByType<PowerSuitDemoBootstrap>(
                FindObjectsInactive.Include
            );
            yield return null;
        }

        if (bootstrap == null || !bootstrap.TryInitializeDemo())
        {
            fatalError = bootstrap == null
                ? "Timed out waiting for PowerSuitDemoBootstrap."
                : "Demo bootstrap failed: " + bootstrap.LastInitializationError;
            WriteFailureAndExit(fatalError);
            yield break;
        }

        EnemySpawnDirector director = bootstrap.SpawnDirector;
        if (director == null)
        {
            WriteFailureAndExit("The demo bootstrap has no EnemySpawnDirector.");
            yield break;
        }

        PlayerHealth health = bootstrap.OwningPlayer != null
            ? bootstrap.OwningPlayer.GetComponent<PlayerHealth>()
            : null;
        health?.SetGodMode(true);

        PowerSuitAbilityController abilities = bootstrap.OwningPlayer != null
            ? bootstrap.OwningPlayer.GetComponent<PowerSuitAbilityController>()
            : null;
        abilities?.SetCooldownsEnabled(false);

        director.SetActiveEnemyCap(options.EnemyCap);
        director.PrewarmForConcurrentPopulation(options.EnemyCap);
        director.SetSpawnIntervalSeconds(0.15f);
        director.SetDirectorEnabled(true);
        director.SetPaused(false);
        profilerWasEnabled = Profiler.enabled;
        Profiler.enabled = true;

        float warmupEnd = Time.realtimeSinceStartup + options.WarmupSeconds;
        float nextFill = 0f;
        float nextAbilityPulse = 0f;
        float warmupStarted = Time.realtimeSinceStartup;
        float nextWarmupRecycle = warmupStarted + 2f;
        while (Time.realtimeSinceStartup < warmupEnd)
        {
            if (Time.realtimeSinceStartup >= nextFill)
            {
                director.SpawnRandom(options.EnemyCap);
                nextFill = Time.realtimeSinceStartup + 0.5f;
            }
            if (Time.realtimeSinceStartup >= nextAbilityPulse)
            {
                ExerciseAbilities(bootstrap, director);
                nextAbilityPulse = Time.realtimeSinceStartup + 1f;
            }
            if (Time.realtimeSinceStartup >= nextWarmupRecycle &&
                warmupEnd - Time.realtimeSinceStartup > 1f)
            {
                director.KillAllActiveEnemies();
                nextWarmupRecycle += 2f;
            }
            peakActiveEnemies = Mathf.Max(
                peakActiveEnemies,
                director.ActiveInstanceCount
            );
            yield return null;
        }

        CombatFeedbackPool.TryGetStatistics(out CombatFeedbackPool.Statistics poolBefore);
        CombatFeedbackPool.RuntimeInstantiationEntry[] poolBreakdownBefore =
            new CombatFeedbackPool.RuntimeInstantiationEntry[128];
        int poolBreakdownBeforeCount =
            CombatFeedbackPool.CopyRuntimeInstantiationEntries(poolBreakdownBefore);
        int spawnedBefore = director.TotalSpawned;
        int capacity = Mathf.Clamp(
            options.DurationSeconds * options.TargetFrameRate * 2,
            1024,
            MaximumRecordedSamples
        );
        PerformanceSampleAccumulator frameSamples = new(capacity);
        PerformanceSampleAccumulator mainThreadSamples = new(capacity);
        PerformanceSampleAccumulator cpuFrameSamples = new(capacity);
        PerformanceSampleAccumulator gpuFrameSamples = new(capacity);
        PerformanceSampleAccumulator gcSamples = new(capacity);
        PerformanceSampleAccumulator mainThreadManagedSamples = new(capacity);
        PerformanceSampleAccumulator drawCallSamples = new(capacity);

        StartRecorders();
        FrameTiming[] frameTimingBuffer = new FrameTiming[1];
        FrameTimingManager.CaptureFrameTimings();
        long previousThreadAllocation = GC.GetAllocatedBytesForCurrentThread();
        float measuredStart = Time.realtimeSinceStartup;
        float measuredEnd = measuredStart + options.DurationSeconds;
        float nextStressPulse = measuredStart + 2f;
        float nextRecyclePulse = measuredStart + options.DurationSeconds / 3f;
        int recyclePulses = 0;
        while (Time.realtimeSinceStartup < measuredEnd)
        {
            yield return null;

            frameSamples.Add(Time.unscaledDeltaTime * 1000d);
            if (mainThreadRecorder.Valid)
            {
                mainThreadSamples.Add(
                    mainThreadRecorder.LastValue * NanosecondsToMilliseconds
                );
            }
            if (gcAllocatedRecorder.Valid)
            {
                gcSamples.Add(gcAllocatedRecorder.LastValue);
            }
            if (drawCallsRecorder.Valid)
            {
                drawCallSamples.Add(drawCallsRecorder.LastValue);
            }
            if (FrameTimingManager.GetLatestTimings(1, frameTimingBuffer) > 0)
            {
                FrameTiming timing = frameTimingBuffer[0];
                cpuFrameSamples.Add(timing.cpuFrameTime);
                // Some desktop player backends return a timing record with a
                // zero GPU duration when no usable GPU sample is exposed.
                // Treat that as unavailable instead of reporting a perfect
                // zero-millisecond GPU frame.
                if (timing.gpuFrameTime > 0d)
                {
                    gpuFrameSamples.Add(timing.gpuFrameTime);
                }
            }
            FrameTimingManager.CaptureFrameTimings();
            long currentThreadAllocation = GC.GetAllocatedBytesForCurrentThread();
            mainThreadManagedSamples.Add(
                Math.Max(0L, currentThreadAllocation - previousThreadAllocation)
            );
            previousThreadAllocation = currentThreadAllocation;

            peakActiveEnemies = Mathf.Max(
                peakActiveEnemies,
                director.ActiveInstanceCount
            );

            float now = Time.realtimeSinceStartup;
            if (now >= nextStressPulse)
            {
                director.SpawnRandom(options.EnemyCap);
                ExerciseAbilities(bootstrap, director);
                nextStressPulse = now + 2f;
            }

            if (recyclePulses < 2 && now >= nextRecyclePulse)
            {
                director.KillAllActiveEnemies();
                recyclePulses++;
                nextRecyclePulse += options.DurationSeconds / 3f;
            }
        }
        DisposeRecorders();

        CombatFeedbackPool.TryGetStatistics(out CombatFeedbackPool.Statistics poolAfter);
        CombatFeedbackPool.RuntimeInstantiationEntry[] poolBreakdownAfter =
            new CombatFeedbackPool.RuntimeInstantiationEntry[128];
        int poolBreakdownAfterCount =
            CombatFeedbackPool.CopyRuntimeInstantiationEntries(poolBreakdownAfter);
        SoakReport report = CreateReport(
            director,
            poolBefore,
            poolAfter,
            poolBreakdownBefore,
            poolBreakdownBeforeCount,
            poolBreakdownAfter,
            poolBreakdownAfterCount,
            spawnedBefore,
            frameSamples,
            mainThreadSamples,
            cpuFrameSamples,
            gpuFrameSamples,
            gcSamples,
            mainThreadManagedSamples,
            drawCallSamples,
            fatalError
        );
        WriteReport(report);
        Debug.Log(
            "[PowerSuitPerformance] Completed: " +
            (report.passed ? "PASS" : "FAIL") +
            ", frame p95=" +
            report.frameTimeMilliseconds.p95.ToString("0.00", CultureInfo.InvariantCulture) +
            " ms, GC p95=" +
            report.gcAllocatedBytesPerFrame.p95.ToString("0", CultureInfo.InvariantCulture) +
            " B, enemies peak=" + report.peakActiveEnemies + "."
        );
        Finish(report.passed ? 0 : 2);
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= HandleLog;
        DisposeRecorders();
        Profiler.enabled = profilerWasEnabled;
    }

    private void StartRecorders()
    {
        mainThreadRecorder = StartRecorder(ProfilerCategory.Internal, "Main Thread");
        gcAllocatedRecorder = StartRecorder(ProfilerCategory.Memory, "GC Allocated In Frame");
        drawCallsRecorder = StartRecorder(ProfilerCategory.Render, "Draw Calls Count");
    }

    private static ProfilerRecorder StartRecorder(
        ProfilerCategory category,
        string statName
    )
    {
        try
        {
            return ProfilerRecorder.StartNew(category, statName, 1);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[PowerSuitPerformance] Profiler counter unavailable: " +
                statName + " (" + exception.Message + ")"
            );
            return default;
        }
    }

    private void DisposeRecorders()
    {
        if (mainThreadRecorder.Valid)
        {
            mainThreadRecorder.Dispose();
        }
        if (gcAllocatedRecorder.Valid)
        {
            gcAllocatedRecorder.Dispose();
        }
        if (drawCallsRecorder.Valid)
        {
            drawCallsRecorder.Dispose();
        }
        mainThreadRecorder = default;
        gcAllocatedRecorder = default;
        drawCallsRecorder = default;
    }

    private static void ExerciseAbilities(
        PowerSuitDemoBootstrap bootstrap,
        EnemySpawnDirector director
    )
    {
        if (bootstrap.OwningPlayer == null || director.ActiveInstanceCount <= 0)
        {
            return;
        }

        EnemyArchetypeController enemy = director.GetActiveEnemy(0);
        if (enemy == null)
        {
            return;
        }

        Vector3 origin = bootstrap.OwningPlayer.position + Vector3.up * 1.25f;
        Vector3 target = enemy.transform.position;
        ShoulderRocketAbility rocket =
            bootstrap.OwningPlayer.GetComponent<ShoulderRocketAbility>();
        rocket?.TryLaunch(target);

        LightningStrikeAbility lightning =
            bootstrap.OwningPlayer.GetComponent<LightningStrikeAbility>();
        if (lightning != null && lightning.TryBeginTargeting())
        {
            lightning.UpdateTarget(origin, target, Vector3.up, true, false);
            lightning.ReleaseTargeting();
        }

        VoidUltimateAbility ultimate =
            bootstrap.OwningPlayer.GetComponent<VoidUltimateAbility>();
        if (ultimate != null && !ultimate.IsActive)
        {
            ultimate.FillMeter();
            ultimate.TryActivate(origin, target, Vector3.up, true, false);
        }
    }

    private SoakReport CreateReport(
        EnemySpawnDirector director,
        CombatFeedbackPool.Statistics poolBefore,
        CombatFeedbackPool.Statistics poolAfter,
        CombatFeedbackPool.RuntimeInstantiationEntry[] poolBreakdownBefore,
        int poolBreakdownBeforeCount,
        CombatFeedbackPool.RuntimeInstantiationEntry[] poolBreakdownAfter,
        int poolBreakdownAfterCount,
        int spawnedBefore,
        PerformanceSampleAccumulator frameSamples,
        PerformanceSampleAccumulator mainThreadSamples,
        PerformanceSampleAccumulator cpuFrameSamples,
        PerformanceSampleAccumulator gpuFrameSamples,
        PerformanceSampleAccumulator gcSamples,
        PerformanceSampleAccumulator mainThreadManagedSamples,
        PerformanceSampleAccumulator drawCallSamples,
        string fatalError
    )
    {
        PerformanceSampleSummary frame = frameSamples.CreateSummary();
        PerformanceSampleSummary main = mainThreadSamples.CreateSummary();
        PerformanceSampleSummary cpuFrame = cpuFrameSamples.CreateSummary();
        PerformanceSampleSummary gpuFrame = gpuFrameSamples.CreateSummary();
        PerformanceSampleSummary gc = gcSamples.CreateSummary();
        PerformanceSampleSummary mainManaged =
            mainThreadManagedSamples.CreateSummary();
        PerformanceSampleSummary draw = drawCallSamples.CreateSummary();
        double targetFrameMilliseconds = 1000d / options.TargetFrameRate;
        long steadyStateInstantiations =
            poolAfter.RuntimeInstantiationCount - poolBefore.RuntimeInstantiationCount;
        PoolMissReport[] poolMisses = CreatePoolMissReport(
            poolBreakdownBefore,
            poolBreakdownBeforeCount,
            poolBreakdownAfter,
            poolBreakdownAfterCount
        );

        string failure = fatalError;
        if (string.IsNullOrEmpty(failure) && loggedErrors > 0)
        {
            failure = "The player logged " + loggedErrors + " error(s) during measurement.";
        }
        if (string.IsNullOrEmpty(failure) && frame.Count == 0)
        {
            failure = "No frame samples were recorded.";
        }
        if (string.IsNullOrEmpty(failure) && frame.DroppedCount > 0)
        {
            failure = "The fixed sample buffer was exhausted.";
        }
        if (string.IsNullOrEmpty(failure) && peakActiveEnemies < Math.Min(8, options.EnemyCap))
        {
            failure = "The stress load never reached the minimum representative enemy count.";
        }
        if (string.IsNullOrEmpty(failure) && steadyStateInstantiations > 0)
        {
            failure = "The combat pool instantiated objects after warmup.";
        }
        if (string.IsNullOrEmpty(failure) && mainManaged.Percentile95 > 256d)
        {
            failure = "Main-thread managed allocation p95 exceeded 256 bytes per frame.";
        }
        if (string.IsNullOrEmpty(failure) && frame.Percentile95 > targetFrameMilliseconds * 1.35d)
        {
            failure = "Frame-time p95 exceeded 135% of the requested frame budget.";
        }

        return new SoakReport
        {
            timestampUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            unityVersion = Application.unityVersion,
            platform = Application.platform.ToString(),
            graphicsDevice = SystemInfo.graphicsDeviceName,
            targetFrameRate = options.TargetFrameRate,
            durationSeconds = options.DurationSeconds,
            warmupSeconds = options.WarmupSeconds,
            requestedEnemyCap = options.EnemyCap,
            peakActiveEnemies = peakActiveEnemies,
            totalEnemiesSpawned = director.TotalSpawned - spawnedBefore,
            steadyStatePoolInstantiations = steadyStateInstantiations,
            steadyStatePoolSpawns = poolAfter.SpawnCount - poolBefore.SpawnCount,
            steadyStatePoolMisses = poolMisses,
            peakPooledObjects = poolAfter.PeakActiveCount,
            peakPooledProjectiles = poolAfter.PeakActiveProjectileCount,
            loggedErrors = loggedErrors,
            passed = string.IsNullOrEmpty(failure),
            failureReason = failure ?? string.Empty,
            frameTimeMilliseconds = ToMetric(frame, true),
            mainThreadMilliseconds = ToMetric(main, mainThreadRecorder.Valid || main.Count > 0),
            cpuFrameMilliseconds = ToMetric(cpuFrame, cpuFrame.Count > 0),
            gpuFrameMilliseconds = ToMetric(gpuFrame, gpuFrame.Count > 0),
            gcAllocatedBytesPerFrame = ToMetric(gc, gcAllocatedRecorder.Valid || gc.Count > 0),
            mainThreadManagedAllocatedBytesPerFrame = ToMetric(mainManaged, true),
            drawCallsPerFrame = ToMetric(draw, drawCallsRecorder.Valid || draw.Count > 0)
        };
    }

    private static MetricReport ToMetric(
        PerformanceSampleSummary summary,
        bool available
    )
    {
        return new MetricReport
        {
            available = available && summary.Count > 0,
            samples = summary.Count,
            droppedSamples = summary.DroppedCount,
            average = summary.Average,
            maximum = summary.Maximum,
            p50 = summary.Percentile50,
            p95 = summary.Percentile95,
            p99 = summary.Percentile99
        };
    }

    private static PoolMissReport[] CreatePoolMissReport(
        CombatFeedbackPool.RuntimeInstantiationEntry[] before,
        int beforeCount,
        CombatFeedbackPool.RuntimeInstantiationEntry[] after,
        int afterCount
    )
    {
        PoolMissReport[] temporary = new PoolMissReport[afterCount];
        int written = 0;
        for (int afterIndex = 0; afterIndex < afterCount; afterIndex++)
        {
            CombatFeedbackPool.RuntimeInstantiationEntry current =
                after[afterIndex];
            long previous = 0L;
            for (int beforeIndex = 0; beforeIndex < beforeCount; beforeIndex++)
            {
                if (before[beforeIndex].Prefab == current.Prefab)
                {
                    previous = before[beforeIndex].Count;
                    break;
                }
            }

            long delta = current.Count - previous;
            if (delta <= 0L)
            {
                continue;
            }
            temporary[written++] = new PoolMissReport
            {
                prefab = current.Prefab != null
                    ? current.Prefab.name
                    : "<destroyed prefab>",
                runtimeInstantiations = delta
            };
        }

        if (written == temporary.Length)
        {
            return temporary;
        }
        PoolMissReport[] result = new PoolMissReport[written];
        Array.Copy(temporary, result, written);
        return result;
    }

    private void HandleLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            loggedErrors++;
        }
    }

    private void WriteFailureAndExit(string reason)
    {
        SoakReport report = new SoakReport
        {
            timestampUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            unityVersion = Application.unityVersion,
            platform = Application.platform.ToString(),
            graphicsDevice = SystemInfo.graphicsDeviceName,
            targetFrameRate = options.TargetFrameRate,
            durationSeconds = options.DurationSeconds,
            warmupSeconds = options.WarmupSeconds,
            requestedEnemyCap = options.EnemyCap,
            loggedErrors = loggedErrors,
            passed = false,
            failureReason = reason,
            frameTimeMilliseconds = new MetricReport(),
            mainThreadMilliseconds = new MetricReport(),
            cpuFrameMilliseconds = new MetricReport(),
            gpuFrameMilliseconds = new MetricReport(),
            gcAllocatedBytesPerFrame = new MetricReport(),
            mainThreadManagedAllocatedBytesPerFrame = new MetricReport(),
            drawCallsPerFrame = new MetricReport()
        };
        WriteReport(report);
        Debug.LogError("[PowerSuitPerformance] " + reason);
        Finish(2);
    }

    private void WriteReport(SoakReport report)
    {
        string path = Path.GetFullPath(options.OutputPath);
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, JsonUtility.ToJson(report, true));
    }

    private void Finish(int exitCode)
    {
        Application.logMessageReceived -= HandleLog;
        Profiler.enabled = profilerWasEnabled;
        if (options.ExitWhenFinished)
        {
            Application.Quit(exitCode);
        }
    }
}
#endif
