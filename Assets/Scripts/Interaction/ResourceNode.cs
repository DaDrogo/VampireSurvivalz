using UnityEngine;

public class ResourceNode : MonoBehaviour, IInteractable
{
    [SerializeField] private string resourceType = "Wood";
    [SerializeField] private int amount = 10;

    public void Interact()
    {
        if (ResourceManager.Instance == null)
        {
            Debug.LogWarning("ResourceNode: no ResourceManager in scene.");
            return;
        }

        ResourceManager.Instance.AddResource(resourceType, amount);
        Debug.Log($"Collected {amount} {resourceType}.");
        Destroy(gameObject);
    }
}
