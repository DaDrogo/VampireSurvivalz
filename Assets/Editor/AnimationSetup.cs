using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Run once via  Tools → Setup Player Animations
/// Creates Assets/Animations/PlayerAnimator.controller + 10 empty animation clips.
/// After running: assign sprites to each .anim clip in the Animation window.
/// </summary>
public static class AnimationSetup
{
    [MenuItem("Tools/Setup Player Animations")]
    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Animations"))
            AssetDatabase.CreateFolder("Assets", "Animations");

        var walkDown  = Clip("WalkDown",  loop: true);
        var walkUp    = Clip("WalkUp",    loop: true);
        var walkLeft  = Clip("WalkLeft",  loop: true);
        var walkRight = Clip("WalkRight", loop: true);
        var idleDown  = Clip("IdleDown",  loop: true);
        var idleUp    = Clip("IdleUp",    loop: true);
        var idleLeft  = Clip("IdleLeft",  loop: true);
        var idleRight = Clip("IdleRight", loop: true);
        var hurt      = Clip("Hurt",      loop: false);
        var death     = Clip("Death",     loop: false);

        const string path = "Assets/Animations/PlayerAnimator.controller";

        // Don't overwrite an existing controller
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)
                   ?? AnimatorController.CreateAnimatorControllerAtPath(path);

        // Parameters (skip if already present)
        AddParam(ctrl, "MoveX",    AnimatorControllerParameterType.Float);
        AddParam(ctrl, "MoveY",    AnimatorControllerParameterType.Float);
        AddParam(ctrl, "IsMoving", AnimatorControllerParameterType.Bool);
        AddParam(ctrl, "Hurt",     AnimatorControllerParameterType.Trigger);
        AddParam(ctrl, "Death",    AnimatorControllerParameterType.Trigger);

        var sm = ctrl.layers[0].stateMachine;

        // Idle blend tree
        BlendTree idleTree;
        var idleState = ctrl.CreateBlendTreeInController("Idle", out idleTree, 0);
        idleTree.blendType       = BlendTreeType.FreeformDirectional2D;
        idleTree.blendParameter  = "MoveX";
        idleTree.blendParameterY = "MoveY";
        idleTree.AddChild(idleDown,  new Vector2( 0f, -1f));
        idleTree.AddChild(idleUp,    new Vector2( 0f,  1f));
        idleTree.AddChild(idleLeft,  new Vector2(-1f,  0f));
        idleTree.AddChild(idleRight, new Vector2( 1f,  0f));

        // Walk blend tree
        BlendTree walkTree;
        var walkState = ctrl.CreateBlendTreeInController("Walk", out walkTree, 0);
        walkTree.blendType       = BlendTreeType.FreeformDirectional2D;
        walkTree.blendParameter  = "MoveX";
        walkTree.blendParameterY = "MoveY";
        walkTree.AddChild(walkDown,  new Vector2( 0f, -1f));
        walkTree.AddChild(walkUp,    new Vector2( 0f,  1f));
        walkTree.AddChild(walkLeft,  new Vector2(-1f,  0f));
        walkTree.AddChild(walkRight, new Vector2( 1f,  0f));

        var hurtState  = sm.AddState("Hurt");  hurtState.motion  = hurt;
        var deathState = sm.AddState("Death"); deathState.motion = death;
        sm.defaultState = idleState;

        // Idle ↔ Walk
        Transition(idleState, walkState, "IsMoving",  AnimatorConditionMode.If);
        Transition(walkState, idleState, "IsMoving",  AnimatorConditionMode.IfNot);

        // Any → Hurt → Idle
        var anyHurt = sm.AddAnyStateTransition(hurtState);
        anyHurt.AddCondition(AnimatorConditionMode.If, 0, "Hurt");
        anyHurt.duration = 0f; anyHurt.canTransitionToSelf = false;
        var hurtExit = hurtState.AddTransition(idleState);
        hurtExit.hasExitTime = true; hurtExit.exitTime = 1f; hurtExit.duration = 0f;

        // Any → Death (terminal)
        var anyDeath = sm.AddAnyStateTransition(deathState);
        anyDeath.AddCondition(AnimatorConditionMode.If, 0, "Death");
        anyDeath.duration = 0f; anyDeath.canTransitionToSelf = false;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[AnimationSetup] Done — PlayerAnimator.controller + 10 clips created in Assets/Animations/");
        EditorUtility.DisplayDialog("Animation Setup", "Done!\n\nPlayerAnimator.controller and 10 animation clips created in Assets/Animations/.\n\nNext: assign sprites to each clip in the Animation window.", "OK");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AnimationClip Clip(string name, bool loop)
    {
        string p = $"Assets/Animations/{name}.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
        if (existing != null) return existing;

        var clip = new AnimationClip { name = name, frameRate = 8f };
        var s = AnimationUtility.GetAnimationClipSettings(clip);
        s.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, s);
        AssetDatabase.CreateAsset(clip, p);
        return clip;
    }

    private static void AddParam(AnimatorController ctrl, string name, AnimatorControllerParameterType type)
    {
        foreach (var p in ctrl.parameters)
            if (p.name == name) return;
        ctrl.AddParameter(name, type);
    }

    private static void Transition(AnimatorState from, AnimatorState to, string param, AnimatorConditionMode mode)
    {
        var t = from.AddTransition(to);
        t.AddCondition(mode, 0, param);
        t.duration = 0f;
        t.hasExitTime = false;
    }
}
