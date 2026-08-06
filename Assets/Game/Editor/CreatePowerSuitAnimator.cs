using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CreatePowerSuitAnimator
{
    private const string OutputFolder = "Assets/Game/Animation";
    private const string OutputPath =
        OutputFolder + "/PowerSuitAnimator.controller";

    [MenuItem("PowerSuit/Create Animator Controller")]
    public static void CreateController()
    {
        EnsureFolderExists();

        AnimationClip idle = FindClip("PS_Idle");
        AnimationClip walk = FindClip("PS_Walk");
        AnimationClip hover = FindClip("PS_Hover");

        if (idle == null || walk == null || hover == null)
        {
            Debug.LogError(
                "Could not find all required animation clips. " +
                "Required: PS_Idle, PS_Walk and PS_Hover. " +
                "Check the Console for discovered FBX clip names."
            );

            LogAvailableFbxClips();
            return;
        }

        AnimatorController existingController =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(
                OutputPath
            );

        if (existingController != null)
        {
            AssetDatabase.DeleteAsset(OutputPath);
        }

        AnimatorController controller =
            AnimatorController.CreateAnimatorControllerAtPath(
                OutputPath
            );

        controller.AddParameter(
            "IsMoving",
            AnimatorControllerParameterType.Bool
        );

        controller.AddParameter(
            "IsFlying",
            AnimatorControllerParameterType.Bool
        );

        controller.AddParameter(
            "IsAiming",
            AnimatorControllerParameterType.Bool
        );

        AnimatorStateMachine stateMachine =
            controller.layers[0].stateMachine;

        AnimatorState idleState = stateMachine.AddState(
            "Idle",
            new Vector3(250f, 50f)
        );

        AnimatorState walkState = stateMachine.AddState(
            "Walk",
            new Vector3(500f, 50f)
        );

        AnimatorState hoverState = stateMachine.AddState(
            "Hover",
            new Vector3(375f, 220f)
        );

        idleState.motion = idle;
        walkState.motion = walk;
        hoverState.motion = hover;

        stateMachine.defaultState = idleState;

        AddTransition(
            idleState,
            walkState,
            new TransitionCondition(
                "IsMoving",
                AnimatorConditionMode.If
            ),
            new TransitionCondition(
                "IsFlying",
                AnimatorConditionMode.IfNot
            )
        );

        AddTransition(
            walkState,
            idleState,
            new TransitionCondition(
                "IsMoving",
                AnimatorConditionMode.IfNot
            )
        );

        AddTransition(
            idleState,
            hoverState,
            new TransitionCondition(
                "IsFlying",
                AnimatorConditionMode.If
            )
        );

        AddTransition(
            walkState,
            hoverState,
            new TransitionCondition(
                "IsFlying",
                AnimatorConditionMode.If
            )
        );

        AddTransition(
            hoverState,
            idleState,
            new TransitionCondition(
                "IsFlying",
                AnimatorConditionMode.IfNot
            ),
            new TransitionCondition(
                "IsMoving",
                AnimatorConditionMode.IfNot
            )
        );

        AddTransition(
            hoverState,
            walkState,
            new TransitionCondition(
                "IsFlying",
                AnimatorConditionMode.IfNot
            ),
            new TransitionCondition(
                "IsMoving",
                AnimatorConditionMode.If
            )
        );

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = controller;
        EditorGUIUtility.PingObject(controller);

        Debug.Log(
            "Created PowerSuitAnimator.controller with " +
            "Idle, Walk and Hover states."
        );
    }

    private static void AddTransition(
        AnimatorState from,
        AnimatorState to,
        params TransitionCondition[] conditions
    )
    {
        AnimatorStateTransition transition =
            from.AddTransition(to);

        transition.hasExitTime = false;
        transition.duration = 0.12f;

        foreach (TransitionCondition condition in conditions)
        {
            transition.AddCondition(
                condition.Mode,
                0f,
                condition.Parameter
            );
        }
    }

    private static AnimationClip FindClip(string wantedName)
    {
        string[] assetPaths = AssetDatabase.GetAllAssetPaths();

        foreach (string path in assetPaths)
        {
            if (!path.EndsWith(
                    ".fbx",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AnimationClip[] clips = AssetDatabase
                .LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(clip =>
                    !clip.name.StartsWith(
                        "__preview__",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToArray();

            foreach (AnimationClip clip in clips)
            {
                string simplifiedName =
                    SimplifyClipName(clip.name);

                if (simplifiedName.Equals(
                        wantedName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log(
                        $"Found {wantedName} in {path} " +
                        $"as clip '{clip.name}'."
                    );

                    return clip;
                }
            }
        }

        return null;
    }

    private static string SimplifyClipName(string clipName)
    {
        string simplified = clipName;

        if (simplified.Contains("|"))
        {
            simplified = simplified
                .Split('|')
                .Last();
        }

        if (simplified.Contains(":"))
        {
            simplified = simplified
                .Split(':')
                .Last();
        }

        return simplified.Trim();
    }

    private static void LogAvailableFbxClips()
    {
        Debug.Log("Animation clips discovered inside FBX files:");

        foreach (string path in AssetDatabase.GetAllAssetPaths())
        {
            if (!path.EndsWith(
                    ".fbx",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AnimationClip[] clips = AssetDatabase
                .LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(clip =>
                    !clip.name.StartsWith(
                        "__preview__",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToArray();

            foreach (AnimationClip clip in clips)
            {
                Debug.Log(
                    $"{path}: '{clip.name}' " +
                    $"→ '{SimplifyClipName(clip.name)}'"
                );
            }
        }
    }

    private static void EnsureFolderExists()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Game"))
        {
            AssetDatabase.CreateFolder(
                "Assets",
                "Game"
            );
        }

        if (!AssetDatabase.IsValidFolder(OutputFolder))
        {
            AssetDatabase.CreateFolder(
                "Assets/Game",
                "Animation"
            );
        }
    }

    private readonly struct TransitionCondition
    {
        public string Parameter { get; }
        public AnimatorConditionMode Mode { get; }

        public TransitionCondition(
            string parameter,
            AnimatorConditionMode mode
        )
        {
            Parameter = parameter;
            Mode = mode;
        }
    }
}