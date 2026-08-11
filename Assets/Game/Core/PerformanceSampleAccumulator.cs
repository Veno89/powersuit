using System;
using System.Globalization;

namespace Powersuit.Core
{
    /// <summary>
    /// Immutable distribution summary produced after a bounded performance run.
    /// Percentiles use linear interpolation between the two nearest samples.
    /// </summary>
    public readonly struct PerformanceSampleSummary
    {
        public PerformanceSampleSummary(
            int count,
            int droppedCount,
            double average,
            double maximum,
            double percentile50,
            double percentile95,
            double percentile99
        )
        {
            Count = count;
            DroppedCount = droppedCount;
            Average = average;
            Maximum = maximum;
            Percentile50 = percentile50;
            Percentile95 = percentile95;
            Percentile99 = percentile99;
        }

        public int Count { get; }
        public int DroppedCount { get; }
        public double Average { get; }
        public double Maximum { get; }
        public double Percentile50 { get; }
        public double Percentile95 { get; }
        public double Percentile99 { get; }
    }

    /// <summary>
    /// Fixed-capacity sampler. Adding a sample never allocates; the one sorted
    /// copy required for percentiles is created only when the final report is
    /// requested outside the measured window.
    /// </summary>
    public sealed class PerformanceSampleAccumulator
    {
        private readonly double[] samples;
        private int count;
        private int droppedCount;
        private double total;
        private double maximum;

        public PerformanceSampleAccumulator(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            samples = new double[capacity];
        }

        public int Capacity => samples.Length;
        public int Count => count;
        public int DroppedCount => droppedCount;

        public bool Add(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
            {
                return false;
            }

            if (count >= samples.Length)
            {
                droppedCount++;
                return false;
            }

            samples[count++] = value;
            total += value;
            maximum = Math.Max(maximum, value);
            return true;
        }

        public void Reset()
        {
            count = 0;
            droppedCount = 0;
            total = 0d;
            maximum = 0d;
        }

        public PerformanceSampleSummary CreateSummary()
        {
            if (count == 0)
            {
                return new PerformanceSampleSummary(
                    0,
                    droppedCount,
                    0d,
                    0d,
                    0d,
                    0d,
                    0d
                );
            }

            double[] sorted = new double[count];
            Array.Copy(samples, sorted, count);
            Array.Sort(sorted);
            return new PerformanceSampleSummary(
                count,
                droppedCount,
                total / count,
                maximum,
                Percentile(sorted, 0.50d),
                Percentile(sorted, 0.95d),
                Percentile(sorted, 0.99d)
            );
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            if (sorted.Length == 1)
            {
                return sorted[0];
            }

            double position = percentile * (sorted.Length - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper)
            {
                return sorted[lower];
            }

            double blend = position - lower;
            return sorted[lower] + (sorted[upper] - sorted[lower]) * blend;
        }
    }

    /// <summary>
    /// Strict command-line contract for the opt-in Development Build soak.
    /// Normal players never create a runner unless -powersuit-soak is present.
    /// </summary>
    public readonly struct PerformanceSoakOptions
    {
        public const int MinimumDurationSeconds = 10;
        public const int MaximumDurationSeconds = 3600;
        public const int MinimumWarmupSeconds = 1;
        public const int MaximumWarmupSeconds = 120;
        public const int MinimumEnemyCap = 1;
        public const int MaximumEnemyCap = 128;
        public const int MinimumTargetFrameRate = 30;
        public const int MaximumTargetFrameRate = 240;

        public PerformanceSoakOptions(
            bool enabled,
            int durationSeconds,
            int warmupSeconds,
            int enemyCap,
            int targetFrameRate,
            string outputPath,
            bool exitWhenFinished
        )
        {
            Enabled = enabled;
            DurationSeconds = durationSeconds;
            WarmupSeconds = warmupSeconds;
            EnemyCap = enemyCap;
            TargetFrameRate = targetFrameRate;
            OutputPath = outputPath ?? string.Empty;
            ExitWhenFinished = exitWhenFinished;
        }

        public bool Enabled { get; }
        public int DurationSeconds { get; }
        public int WarmupSeconds { get; }
        public int EnemyCap { get; }
        public int TargetFrameRate { get; }
        public string OutputPath { get; }
        public bool ExitWhenFinished { get; }

        public static bool TryParse(
            string[] arguments,
            out PerformanceSoakOptions options,
            out string error
        )
        {
            bool enabled = HasFlag(arguments, "-powersuit-soak");
            int duration = 30;
            int warmup = 8;
            int enemyCap = 24;
            int targetFrameRate = 60;
            string output = "PowerSuitPerformanceReport.json";
            bool exit = HasFlag(arguments, "-powersuit-soak-exit");

            if (!enabled)
            {
                options = new PerformanceSoakOptions(
                    false,
                    duration,
                    warmup,
                    enemyCap,
                    targetFrameRate,
                    output,
                    exit
                );
                error = string.Empty;
                return true;
            }

            if (!TryReadBoundedInt(arguments, "-powersuit-soak-duration", MinimumDurationSeconds, MaximumDurationSeconds, duration, out duration, out error) ||
                !TryReadBoundedInt(arguments, "-powersuit-soak-warmup", MinimumWarmupSeconds, MaximumWarmupSeconds, warmup, out warmup, out error) ||
                !TryReadBoundedInt(arguments, "-powersuit-soak-enemies", MinimumEnemyCap, MaximumEnemyCap, enemyCap, out enemyCap, out error) ||
                !TryReadBoundedInt(arguments, "-powersuit-soak-fps", MinimumTargetFrameRate, MaximumTargetFrameRate, targetFrameRate, out targetFrameRate, out error))
            {
                options = default;
                return false;
            }

            if (warmup >= duration)
            {
                options = default;
                error = "The soak warmup must be shorter than the measured duration.";
                return false;
            }

            TryReadString(arguments, "-powersuit-soak-output", output, out output);
            if (string.IsNullOrWhiteSpace(output))
            {
                options = default;
                error = "The soak output path cannot be empty.";
                return false;
            }

            options = new PerformanceSoakOptions(
                true,
                duration,
                warmup,
                enemyCap,
                targetFrameRate,
                output,
                exit
            );
            error = string.Empty;
            return true;
        }

        private static bool HasFlag(string[] arguments, string flag)
        {
            if (arguments == null)
            {
                return false;
            }

            for (int index = 0; index < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryReadBoundedInt(
            string[] arguments,
            string name,
            int minimum,
            int maximum,
            int fallback,
            out int value,
            out string error
        )
        {
            if (!TryReadString(arguments, name, null, out string text))
            {
                value = fallback;
                error = string.Empty;
                return true;
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
                value < minimum || value > maximum)
            {
                error = name + " must be an integer from " + minimum + " to " + maximum + ".";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryReadString(
            string[] arguments,
            string name,
            string fallback,
            out string value
        )
        {
            value = fallback;
            if (arguments == null)
            {
                return false;
            }

            for (int index = 0; index < arguments.Length; index++)
            {
                string argument = arguments[index];
                if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 < arguments.Length)
                    {
                        value = arguments[index + 1];
                    }
                    return true;
                }

                string prefix = name + "=";
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    value = argument.Substring(prefix.Length);
                    return true;
                }
            }
            return false;
        }
    }
}
