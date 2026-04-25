using UnityEngine;

/// <summary>
/// Drives the Vampire's Animator from VampireEnemy events and Rigidbody2D velocity.
/// Attach alongside VampireEnemy on the same GameObject.
/// </summary>
[RequireComponent(typeof(Animator), typeof(Rigidbody2D), typeof(VampireEnemy))]
public class VampireAnimationHandler : MonoBehaviour
{
    private Animator      _anim;
    private Rigidbody2D   _rb;
    private VampireEnemy  _vampire;

    private static readonly int HashMoveX      = Animator.StringToHash("MoveX");
    private static readonly int HashMoveY      = Animator.StringToHash("MoveY");
    private static readonly int HashIsMoving   = Animator.StringToHash("IsMoving");
    private static readonly int HashHurt       = Animator.StringToHash("Hurt");
    private static readonly int HashDeath      = Animator.StringToHash("Death");
    private static readonly int HashAttack     = Animator.StringToHash("Attack");
    private static readonly int HashVulnerable = Animator.StringToHash("Vulnerable");

    private float _prevHealth;

    private void Awake()
    {
        _anim    = GetComponent<Animator>();
        _rb      = GetComponent<Rigidbody2D>();
        _vampire = GetComponent<VampireEnemy>();
    }

    private void OnEnable()
    {
        _vampire.OnHealthChanged    += HandleHealthChanged;
        _vampire.OnBanished         += HandleBanished;
        _vampire.OnPermanentlyKilled += HandlePermanentDeath;
        _vampire.OnAttackPerformed  += HandleAttack;
        _prevHealth = _vampire.CurrentHealth;
    }

    private void OnDisable()
    {
        _vampire.OnHealthChanged    -= HandleHealthChanged;
        _vampire.OnBanished         -= HandleBanished;
        _vampire.OnPermanentlyKilled -= HandlePermanentDeath;
        _vampire.OnAttackPerformed  -= HandleAttack;
    }

    private void Update()
    {
        Vector2 vel    = _rb.linearVelocity;
        bool    moving = vel.sqrMagnitude > 0.04f;

        _anim.SetBool(HashIsMoving, moving);
        _anim.SetBool(HashVulnerable, _vampire.IsVulnerable);

        if (moving)
        {
            _anim.SetFloat(HashMoveX, vel.x);
            _anim.SetFloat(HashMoveY, vel.y);
        }
    }

    private void HandleHealthChanged(float current, float max)
    {
        if (current < _prevHealth)
            _anim.SetTrigger(HashHurt);
        _prevHealth = current;
    }

    private void HandleBanished()      => _anim.SetTrigger(HashDeath);
    private void HandlePermanentDeath() => _anim.SetTrigger(HashDeath);
    private void HandleAttack()        => _anim.SetTrigger(HashAttack);
}
