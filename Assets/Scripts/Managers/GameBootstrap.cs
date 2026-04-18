using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ensures the game always starts from the Main Menu scene (Build Settings index 0),
/// regardless of which scene is currently open in the Unity Editor.
/// No GameObject needed — the [RuntimeInitializeOnLoadMethod] attribute makes Unity
/// call this automatically before any scene Awake fires.
/// </summary>
public static class GameBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        // If we're already on scene 0 (MainMenuScene), do nothing.
        if (SceneManager.GetActiveScene().buildIndex == 0) return;

        // Otherwise load scene 0 immediately, replacing whatever is open.
        SceneManager.LoadScene(0);
    }
}
