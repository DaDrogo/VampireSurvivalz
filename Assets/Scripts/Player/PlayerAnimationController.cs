using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerAnimationController : MonoBehaviour
{
    private static readonly int ParamMoveX        = Animator.StringToHash("MoveX");
    private static readonly int ParamMoveY        = Animator.StringToHash("MoveY");
    private static readonly int ParamIsMoving     = Animator.StringToHash("IsMoving");
    private static readonly int ParamDeath        = Animator.StringToHash("Death");
    private static readonly int ParamIsHarvesting = Animator.StringToHash("IsHarvesting");

    [SerializeField] private Color hurtFlashColor    = Color.red;
    [SerializeField] private float hurtFlashDuration = 0.15f;

    private Animator        _animator;
    private SpriteRenderer  _sprite;
    private Color           _originalColor;
    private Coroutine       _flashCoroutine;

    private Vector2 _lastDir = Vector2.down;

    private void Awake()
    {
        _animator      = GetComponent<Animator>();
        _sprite        = GetComponent<SpriteRenderer>();
        _originalColor = _sprite != null ? _sprite.color : Color.white;
    }

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
            _animator.SetFloat(ParamMoveX, _lastDir.x);
            _animator.SetFloat(ParamMoveY, _lastDir.y);
        }
    }

    public void TriggerHurt()
    {
        if (_sprite == null) return;
        if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
        _flashCoroutine = StartCoroutine(HurtFlash());
    }

    private IEnumerator HurtFlash()
    {
        _sprite.color = hurtFlashColor;
        yield return new WaitForSeconds(hurtFlashDuration);
        _sprite.color = _originalColor;
        _flashCoroutine = null;
    }

    public void TriggerDeath()            => _animator.SetTrigger(ParamDeath);
    public void SetHarvesting(bool value) => _animator.SetBool(ParamIsHarvesting, value);

    public void ApplyOverride(AnimatorOverrideController overrideController)
    {
        if (overrideController != null)
            _animator.runtimeAnimatorController = overrideController;
    }
}
