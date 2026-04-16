public interface IEnemyAttackable
{
    float CurrentHealth { get; }
    float MaxHealth { get; }
    bool IsDestroyed { get; }
    void ReceiveEnemyAttack(float damage, float attackInterval);
}
