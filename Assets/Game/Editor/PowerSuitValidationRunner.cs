#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Powersuit.Editor
{
    /// <summary>
    /// Noninteractive project validation entry point for local automation and
    /// the in-editor menu. Results are written beneath ignored Temp so a test
    /// run never dirties project assets or loaded scenes.
    /// </summary>
    [InitializeOnLoad]
    public static class PowerSuitValidationRunner
    {
        public const string EditModeResultPath =
            "Temp/PowerSuitValidationEditMode.txt";
        public const string PlayModeResultPath =
            "Temp/PowerSuitValidationPlayMode.txt";

        private const string PendingKey =
            "Powersuit.Validation.Pending";
        private const string ResultPathKey =
            "Powersuit.Validation.ResultPath";

        private static TestRunnerApi activeRunner;
        private static PowerSuitValidationCallbacks activeCallbacks;

        public static bool IsRunning => SessionState.GetBool(PendingKey, false);

        static PowerSuitValidationRunner()
        {
            // PlayMode tests reload the editor domain. Re-register the API
            // callback after that reload so the same run still emits its
            // deterministic Temp summary and releases the running latch.
            if (IsRunning)
            {
                RegisterPendingCallback(
                    SessionState.GetString(ResultPathKey, string.Empty)
                );
            }
        }

        [MenuItem("Tools/Powered Suit/Validation/Run All EditMode Tests")]
        public static void RunAllEditModeTests()
        {
            Start(TestMode.EditMode, EditModeResultPath);
        }

        [MenuItem("Tools/Powered Suit/Validation/Run All PlayMode Tests")]
        public static void RunAllPlayModeTests()
        {
            Start(TestMode.PlayMode, PlayModeResultPath);
        }

        public static void Start(TestMode mode, string resultPath)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException(
                    "A PowerSuit validation run is already active."
                );
            }

            if (string.IsNullOrWhiteSpace(resultPath))
            {
                throw new ArgumentException(
                    "A result path is required.",
                    nameof(resultPath)
                );
            }

            string directory = Path.GetDirectoryName(resultPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }

            SessionState.SetBool(PendingKey, true);
            SessionState.SetString(ResultPathKey, resultPath);
            RegisterPendingCallback(resultPath);
            activeRunner = ScriptableObject.CreateInstance<TestRunnerApi>();
            activeRunner.Execute(
                new ExecutionSettings(
                    new Filter { testMode = mode }
                )
            );
        }

        private static void RegisterPendingCallback(string resultPath)
        {
            if (
                activeCallbacks != null ||
                string.IsNullOrWhiteSpace(resultPath)
            )
            {
                return;
            }

            activeCallbacks = new PowerSuitValidationCallbacks(
                resultPath,
                FinishActiveRun
            );
            TestRunnerApi.RegisterTestCallback(activeCallbacks);
        }

        private static void FinishActiveRun()
        {
            if (activeCallbacks != null)
            {
                TestRunnerApi.UnregisterTestCallback(activeCallbacks);
            }

            if (activeRunner != null)
            {
                UnityEngine.Object.DestroyImmediate(activeRunner);
            }

            activeRunner = null;
            activeCallbacks = null;
            SessionState.EraseBool(PendingKey);
            SessionState.EraseString(ResultPathKey);
        }
    }

    public sealed class PowerSuitValidationCallbacks : IErrorCallbacks
    {
        private readonly string resultPath;
        private readonly Action finished;

        public PowerSuitValidationCallbacks(
            string outputPath,
            Action onFinished
        )
        {
            resultPath = outputPath;
            finished = onFinished;
        }

        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }

        public void OnError(string message)
        {
            File.WriteAllText(
                resultPath,
                "Error|" + (message ?? "Unknown test-run error.")
            );
            Debug.LogError("[PowerSuitValidation] " + message);
            finished?.Invoke();
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            var builder = new StringBuilder(2048);
            builder.Append(result.TestStatus);
            builder.Append("|pass=");
            builder.Append(result.PassCount);
            builder.Append("|fail=");
            builder.Append(result.FailCount);
            builder.Append("|skip=");
            builder.Append(result.SkipCount);
            builder.Append("|inconclusive=");
            builder.Append(result.InconclusiveCount);
            builder.Append("|duration=");
            builder.Append(result.Duration.ToString("0.000"));
            AppendFailures(result, builder);
            File.WriteAllText(resultPath, builder.ToString());
            Debug.Log("[PowerSuitValidation] " + builder);
            finished?.Invoke();
        }

        private static void AppendFailures(
            ITestResultAdaptor result,
            StringBuilder builder
        )
        {
            if (
                result.Children == null ||
                !result.Children.Any()
            )
            {
                if (result.FailCount <= 0)
                {
                    return;
                }

                builder.AppendLine();
                builder.Append("FAIL: ");
                builder.Append(result.FullName);
                if (!string.IsNullOrWhiteSpace(result.Message))
                {
                    builder.AppendLine();
                    builder.Append(result.Message);
                }
                return;
            }

            foreach (ITestResultAdaptor child in result.Children)
            {
                if (child.FailCount > 0)
                {
                    AppendFailures(child, builder);
                }
            }
        }
    }
}
#endif
