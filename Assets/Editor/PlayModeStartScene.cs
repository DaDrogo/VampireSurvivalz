using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Pins MainMenuScene as the Play Mode start scene in the Unity Editor.
/// Pressing Play will always start from MainMenuScene, regardless of which
/// scene is currently open. Has no effect on builds.
/// </summary>
[InitializeOnLoad]
public static class PlayModeStartScene
{
    private const string ScenePath = "Assets/Scenes/MainMenuScene.unity";

    static PlayModeStartScene()
    {
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (scene != null)
            EditorSceneManager.playModeStartScene = scene;
    }
}
