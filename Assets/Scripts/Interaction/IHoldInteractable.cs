/// <summary>
/// Implemented by objects that require the player to hold the interact button.
/// PlayerController drives all callbacks — implementors only react.
/// </summary>
public interface IHoldInteractable
{
    float HoldDuration { get; }

    /// <summary>Called once when the player begins holding.</summary>
    void OnHoldStart();

    /// <summary>Called every frame while the button is held. progress is 0..1.</summary>
    void OnHoldTick(float progress);

    /// <summary>Called when the player releases before completing the hold.</summary>
    void OnHoldCancelled();

    /// <summary>Called when progress reaches 1. Implementor should destroy/consume itself.</summary>
    void OnHoldCompleted();
}
