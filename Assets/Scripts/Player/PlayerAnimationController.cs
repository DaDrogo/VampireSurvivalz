using UnityEngine;

/// <summary>
/// Drives the Animator on the player based on movement and game events.
/// Attach alongside PlayerController. Requires an Animator component.
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerAnimationController : MonoBehaviour
{
    // Cached parameter hashes — avoids string lookup every frame
    private static readonly int ParamMoveX    = Animator.StringToHash("MoveX");
    private static readonly int ParamMoveY    = Animator.StringToHash("MoveY");
    private static readonly int ParamIsMoving = Animator.StringToHash("IsMoving");
    private static readonly int ParamHurt     = Animator.StringToHash("Hurt");
    private static readonly int ParamDeath    = Animator.StringToHash("Death");

    private Animator _animator;

    // Last non-zero direction — used to hold the correct idle facing
    private Vector2 _lastDir = Vector2.down;

    private void Awake() => _animator = GetComponent<Animator>();

    /// <summary>Call every FixedUpdate with the player's current velocity.</summary>
    public void SetMovement(Vector2 velocity)
    {
        bool moving = velocity.sqrMagnitude > 0.01f;
        _animator.SetBool(ParamIsMoving, moving);

        if (moving)
        {
            _lastDir = velocity.normalized;
            _animator.SetFloat(ParamMoveX, _lastDir.x);
            _animator.SetFloat(ParamMoveY, _lastDir.y);
        }
        else
        {
            // Hold last facing direction so idle uses the right directional clip
            _animator.SetFloat(ParamMoveX, _lastDir.x);
            _animator.SetFloat(ParamMoveY, _lastDir.y);
        }
    }

    public void TriggerHurt()  => _animator.SetTrigger(ParamHurt);
    public void TriggerDeath() => _animator.SetTrigger(ParamDeath);

    /// <summary>Swaps animation clips for the selected character without changing the state machine.</summary>
    public void ApplyOverride(AnimatorOverrideController overrideController)
    {
        if (overrideController != null)
            _animator.runtimeAnimatorController = overrideController;
    }
}
