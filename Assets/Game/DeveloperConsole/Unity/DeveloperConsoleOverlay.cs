using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Powersuit.DeveloperConsole.UnityAdapters
{
    public enum DeveloperConsoleToggleKey
    {
        Backquote = 0,
        F1 = 1
    }

    /// <summary>
    /// Development-only IMGUI presentation over the engine-independent console.
    /// IMGUI matches this project's existing debug UI and avoids adding a TMP
    /// dependency. Production builds disable this component at startup.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class DeveloperConsoleOverlay : MonoBehaviour, IDeveloperConsoleHost
    {
        private const string InputControlName = "PowerSuitDeveloperConsoleInput";
        private const float StatisticsRefreshSeconds = 0.25f;
        private const float ConsoleWidth = 760f;
        private const float ConsoleHeight = 430f;

        [Header("Activation")]
        [SerializeField] private DeveloperConsoleToggleKey toggleKey =
            DeveloperConsoleToggleKey.Backquote;
        [SerializeField] private bool allowF1Fallback = true;

        [Header("Input isolation")]
        [Tooltip("Behaviours such as the player input router to suspend while typing.")]
        [SerializeField] private Behaviour[] gameplayInputBehaviours =
            Array.Empty<Behaviour>();

        [Header("Capacity")]
        [SerializeField, Min(8)] private int historyCapacity = 64;
        [SerializeField, Min(16)] private int outputCapacity = 96;

        [Header("Statistics extensions")]
        [Tooltip("Optional MonoBehaviours implementing IDeveloperStatisticsProvider.")]
        [SerializeField] private MonoBehaviour[] statisticsProviderBehaviours =
            Array.Empty<MonoBehaviour>();

        private readonly List<IDeveloperStatisticsProvider> statisticsProviders =
            new List<IDeveloperStatisticsProvider>(8);
        private readonly StringBuilder outputBuilder = new StringBuilder(2048);
        private readonly StringBuilder statisticsBuilder = new StringBuilder(512);
        private readonly GUIContent outputContent = new GUIContent();
        private readonly GUIContent statisticsContent = new GUIContent();

        private DeveloperConsoleSession session;
        private GUI.WindowFunction drawWindow;
        private GUIStyle outputStyle;
        private GUIStyle statisticsStyle;
        private bool[] gameplayBehaviourWasEnabled;
        private bool isOpen;
        private bool showStatistics;
        private bool focusInputOnNextGui;
        private int ignoreToggleCharacterFrame = -1;
        private int renderedOutputVersion = -1;
        private string inputLine = string.Empty;
        private Vector2 outputScroll;
        private Rect consoleRect;
        private CursorLockMode previousCursorLockMode;
        private bool previousCursorVisible;
        private float statisticsElapsed;
        private int statisticsFrames;
        private float sampledFps;
        private float sampledFrameMilliseconds;
        private long sampledManagedBytes;

        public static bool IsSupportedBuild
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }

        public bool IsOpen => isOpen;

        public bool ShowStatistics
        {
            get => showStatistics;
            set
            {
                if (showStatistics == value)
                {
                    return;
                }

                showStatistics = value;
                statisticsElapsed = 0f;
                statisticsFrames = 0;
                if (value)
                {
                    RefreshStatisticsText();
                }
            }
        }

        public float TimeScale
        {
            get => Time.timeScale;
            set => Time.timeScale = Mathf.Clamp(
                value,
                DeveloperConsoleBuiltIns.MinimumTimeScale,
                DeveloperConsoleBuiltIns.MaximumTimeScale
            );
        }

        public DeveloperConsoleSession Session
        {
            get
            {
                EnsureInitialized();
                return session;
            }
        }

        public ConsoleCommandRegistry Registry => Session.Registry;

        private void Awake()
        {
            if (!IsSupportedBuild)
            {
                enabled = false;
                return;
            }

            EnsureInitialized();
        }

        private void OnDisable()
        {
            if (isOpen)
            {
                Close();
            }
            else
            {
                DeveloperConsoleInputGate.SetBlocked(GetEntityId(), false);
            }
        }

        private void Update()
        {
            if (!IsSupportedBuild)
            {
                return;
            }

            if (WasTogglePressed())
            {
                ignoreToggleCharacterFrame = Time.frameCount;
                Toggle();
            }

            if (showStatistics)
            {
                statisticsElapsed += Time.unscaledDeltaTime;
                statisticsFrames++;
                if (statisticsElapsed >= StatisticsRefreshSeconds)
                {
                    sampledFps = statisticsElapsed > 0f
                        ? statisticsFrames / statisticsElapsed
                        : 0f;
                    sampledFrameMilliseconds = statisticsFrames > 0
                        ? statisticsElapsed * 1000f / statisticsFrames
                        : 0f;
                    sampledManagedBytes = GC.GetTotalMemory(false);
                    statisticsElapsed = 0f;
                    statisticsFrames = 0;
                    RefreshStatisticsText();
                }
            }
        }

        private void OnGUI()
        {
            if (!IsSupportedBuild)
            {
                return;
            }

            EnsureStyles();

            if (isOpen)
            {
                consoleRect = GUI.Window(
                    GetEntityId().GetHashCode(),
                    consoleRect,
                    drawWindow,
                    "POWERSUIT DEVELOPER CONSOLE"
                );
            }

            if (showStatistics)
            {
                DrawStatisticsOverlay();
            }
        }

        public void Toggle()
        {
            if (isOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Open()
        {
            if (!IsSupportedBuild || isOpen)
            {
                return;
            }

            EnsureInitialized();
            isOpen = true;
            previousCursorLockMode = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SuspendGameplayInput();
            DeveloperConsoleInputGate.SetBlocked(GetEntityId(), true);
            focusInputOnNextGui = true;
        }

        public void Close()
        {
            if (!isOpen)
            {
                return;
            }

            isOpen = false;
            RestoreGameplayInput();
            DeveloperConsoleInputGate.SetBlocked(GetEntityId(), false);
            Cursor.lockState = previousCursorLockMode;
            Cursor.visible = previousCursorVisible;
            session?.History.ResetNavigation();
        }

        public bool RegisterStatisticsProvider(IDeveloperStatisticsProvider provider)
        {
            if (provider == null || statisticsProviders.Contains(provider))
            {
                return false;
            }

            statisticsProviders.Add(provider);
            return true;
        }

        public bool UnregisterStatisticsProvider(IDeveloperStatisticsProvider provider)
        {
            return provider != null && statisticsProviders.Remove(provider);
        }

        private void EnsureInitialized()
        {
            if (session != null)
            {
                return;
            }

            var registry = new ConsoleCommandRegistry();
            DeveloperConsoleBuiltIns.Register(registry, this);
            session = new DeveloperConsoleSession(
                registry,
                Mathf.Max(8, historyCapacity),
                Mathf.Max(16, outputCapacity)
            );
            session.Output.Add(
                "Developer console ready. Enter 'help' to list commands.",
                ConsoleMessageType.Information
            );

            drawWindow = DrawConsoleWindow;
            gameplayBehaviourWasEnabled = new bool[gameplayInputBehaviours.Length];
            consoleRect = new Rect(
                20f,
                20f,
                Mathf.Min(ConsoleWidth, Mathf.Max(320f, Screen.width - 40f)),
                Mathf.Min(ConsoleHeight, Mathf.Max(220f, Screen.height - 40f))
            );

            for (int index = 0; index < statisticsProviderBehaviours.Length; index++)
            {
                if (
                    statisticsProviderBehaviours[index] is IDeveloperStatisticsProvider provider
                )
                {
                    RegisterStatisticsProvider(provider);
                }
            }
        }

        private void SuspendGameplayInput()
        {
            if (gameplayBehaviourWasEnabled.Length != gameplayInputBehaviours.Length)
            {
                gameplayBehaviourWasEnabled = new bool[gameplayInputBehaviours.Length];
            }

            for (int index = 0; index < gameplayInputBehaviours.Length; index++)
            {
                Behaviour behaviour = gameplayInputBehaviours[index];
                bool canSuspend = behaviour != null && behaviour != this;
                gameplayBehaviourWasEnabled[index] = canSuspend && behaviour.enabled;
                if (canSuspend)
                {
                    behaviour.enabled = false;
                }
            }
        }

        private void RestoreGameplayInput()
        {
            int count = Mathf.Min(
                gameplayInputBehaviours.Length,
                gameplayBehaviourWasEnabled.Length
            );
            for (int index = 0; index < count; index++)
            {
                Behaviour behaviour = gameplayInputBehaviours[index];
                if (behaviour != null && behaviour != this)
                {
                    behaviour.enabled = gameplayBehaviourWasEnabled[index];
                }
            }
        }

        private void DrawConsoleWindow(int windowId)
        {
            Event current = Event.current;
            if (current.type == EventType.KeyDown)
            {
                if (
                    Time.frameCount == ignoreToggleCharacterFrame &&
                    IsConfiguredToggleEvent(current.keyCode)
                )
                {
                    current.Use();
                }
                else if (
                    current.keyCode == KeyCode.Return ||
                    current.keyCode == KeyCode.KeypadEnter
                )
                {
                    SubmitInput();
                    current.Use();
                }
                else if (current.keyCode == KeyCode.UpArrow)
                {
                    if (session.History.TryPrevious(inputLine, out string previous))
                    {
                        inputLine = previous;
                    }
                    current.Use();
                }
                else if (current.keyCode == KeyCode.DownArrow)
                {
                    session.History.TryNext(out inputLine);
                    current.Use();
                }
                else if (current.keyCode == KeyCode.Escape)
                {
                    Close();
                    current.Use();
                }
            }

            float padding = 8f;
            float titleOffset = 22f;
            float inputHeight = 24f;
            float hintHeight = 18f;
            float outputHeight = consoleRect.height - titleOffset - inputHeight - hintHeight - padding * 3f;
            var outputRect = new Rect(
                padding,
                titleOffset,
                consoleRect.width - padding * 2f,
                Mathf.Max(80f, outputHeight)
            );

            RefreshOutputTextIfNeeded(outputRect.width - 20f);
            float contentHeight = Mathf.Max(
                outputRect.height,
                outputStyle.CalcHeight(outputContent, outputRect.width - 20f) + 8f
            );
            outputScroll = GUI.BeginScrollView(
                outputRect,
                outputScroll,
                new Rect(0f, 0f, outputRect.width - 20f, contentHeight)
            );
            GUI.Label(
                new Rect(2f, 2f, outputRect.width - 24f, contentHeight - 4f),
                outputContent,
                outputStyle
            );
            GUI.EndScrollView();

            float inputY = outputRect.yMax + padding;
            GUI.SetNextControlName(InputControlName);
            inputLine = GUI.TextField(
                new Rect(padding, inputY, consoleRect.width - padding * 2f, inputHeight),
                inputLine
            );
            GUI.Label(
                new Rect(padding, inputY + inputHeight + 2f, consoleRect.width - padding * 2f, hintHeight),
                "Enter: execute    Up/Down: history    Escape: close"
            );

            if (focusInputOnNextGui)
            {
                GUI.FocusControl(InputControlName);
                focusInputOnNextGui = false;
            }

            GUI.DragWindow(new Rect(0f, 0f, consoleRect.width, titleOffset));
        }

        private void SubmitInput()
        {
            if (string.IsNullOrWhiteSpace(inputLine))
            {
                return;
            }

            session.Submit(inputLine);
            inputLine = string.Empty;
            focusInputOnNextGui = true;
        }

        private void RefreshOutputTextIfNeeded(float width)
        {
            ConsoleOutputBuffer output = session.Output;
            if (renderedOutputVersion == output.Version)
            {
                return;
            }

            outputBuilder.Clear();
            for (int index = 0; index < output.Count; index++)
            {
                ConsoleOutputEntry entry = output.GetAt(index);
                if (index > 0)
                {
                    outputBuilder.Append('\n');
                }

                switch (entry.MessageType)
                {
                    case ConsoleMessageType.Success:
                        outputBuilder.Append("[ok] ");
                        break;
                    case ConsoleMessageType.Error:
                        outputBuilder.Append("[error] ");
                        break;
                    default:
                        break;
                }

                outputBuilder.Append(entry.Text);
            }

            outputContent.text = outputBuilder.ToString();
            renderedOutputVersion = output.Version;
            outputScroll.y = outputStyle.CalcHeight(outputContent, width);
        }

        private void RefreshStatisticsText()
        {
            statisticsBuilder.Clear();
            statisticsBuilder.Append("DEVELOPER STATS\nFPS: ");
            statisticsBuilder.Append(sampledFps.ToString("0.0"));
            statisticsBuilder.Append("\nFrame: ");
            statisticsBuilder.Append(sampledFrameMilliseconds.ToString("0.00"));
            statisticsBuilder.Append(" ms\nManaged: ");
            statisticsBuilder.Append((sampledManagedBytes / (1024f * 1024f)).ToString("0.0"));
            statisticsBuilder.Append(" MB");

            for (int index = 0; index < statisticsProviders.Count; index++)
            {
                IDeveloperStatisticsProvider provider = statisticsProviders[index];
                if (provider == null)
                {
                    continue;
                }

                statisticsBuilder.Append('\n');
                try
                {
                    provider.AppendDeveloperStatistics(statisticsBuilder);
                }
                catch (Exception)
                {
                    statisticsBuilder.Append("Statistics provider unavailable");
                }
            }

            statisticsContent.text = statisticsBuilder.ToString();
        }

        private void DrawStatisticsOverlay()
        {
            float width = 240f;
            float height = statisticsStyle.CalcHeight(statisticsContent, width - 16f) + 16f;
            var rect = new Rect(Screen.width - width - 12f, 12f, width, height);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(
                new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f),
                statisticsContent,
                statisticsStyle
            );
        }

        private void EnsureStyles()
        {
            if (outputStyle != null)
            {
                return;
            }

            outputStyle = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            statisticsStyle = new GUIStyle(GUI.skin.label)
            {
                wordWrap = false,
                alignment = TextAnchor.UpperLeft,
                fontSize = 12
            };
        }

        private bool WasTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            bool primary = toggleKey == DeveloperConsoleToggleKey.Backquote
                ? keyboard.backquoteKey.wasPressedThisFrame
                : keyboard.f1Key.wasPressedThisFrame;
            return primary || (allowF1Fallback && keyboard.f1Key.wasPressedThisFrame);
#else
            KeyCode primary = toggleKey == DeveloperConsoleToggleKey.Backquote
                ? KeyCode.BackQuote
                : KeyCode.F1;
            return Input.GetKeyDown(primary) || (allowF1Fallback && Input.GetKeyDown(KeyCode.F1));
#endif
        }

        private bool IsConfiguredToggleEvent(KeyCode keyCode)
        {
            KeyCode primary = toggleKey == DeveloperConsoleToggleKey.Backquote
                ? KeyCode.BackQuote
                : KeyCode.F1;
            return keyCode == primary || (allowF1Fallback && keyCode == KeyCode.F1);
        }
    }
}
