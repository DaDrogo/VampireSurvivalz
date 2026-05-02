using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// One-click setup for CampScene.
/// Menu: Camp ► Setup Camp Scene
/// </summary>
public static class CampSceneSetup
{
    [MenuItem("Camp/Setup Camp Scene")]
    public static void SetupCampScene()
    {
        // ── Open / create the scene ───────────────────────────────────────────
        string scenePath = "Assets/Scenes/CampScene.unity";
        if (!System.IO.File.Exists(scenePath))
        {
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(newScene, scenePath);
        }

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        // Clear everything except camera
        foreach (var root in scene.GetRootGameObjects())
            Object.DestroyImmediate(root);

        // ── Main Camera ───────────────────────────────────────────────────────
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        camGO.transform.position = new Vector3(0f, 0f, -10f);
        var cam = camGO.AddComponent<Camera>();
        cam.orthographic     = true;
        cam.orthographicSize = 7f;
        cam.backgroundColor  = new Color(0.01f, 0.01f, 0.04f);
        cam.clearFlags       = CameraClearFlags.SolidColor;
        camGO.AddComponent<AudioListener>();

        // URP camera data
        var urpCam = camGO.AddComponent<UniversalAdditionalCameraData>();
        urpCam.renderType = CameraRenderType.Base;

        camGO.AddComponent<CampCameraFollow>();

        // ── Global Light 2D ───────────────────────────────────────────────────
        var lightGO = new GameObject("GlobalLight2D");
        var light   = lightGO.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;
        light.intensity = 0.05f;
        light.color     = new Color(0.20f, 0.24f, 0.40f);

        // ── Grid + Tilemaps ───────────────────────────────────────────────────
        var gridGO = new GameObject("Grid");
        gridGO.AddComponent<Grid>();

        var floorGO   = new GameObject("Tilemap_Floor");
        floorGO.transform.SetParent(gridGO.transform);
        var floorTM   = floorGO.AddComponent<Tilemap>();
        var floorRend = floorGO.AddComponent<TilemapRenderer>();
        floorRend.sortingOrder = 0;

        var wallGO   = new GameObject("Tilemap_Walls");
        wallGO.transform.SetParent(gridGO.transform);
        var wallTM   = wallGO.AddComponent<Tilemap>();
        var wallRend = wallGO.AddComponent<TilemapRenderer>();
        wallRend.sortingOrder = 1;
        var wallCol  = wallGO.AddComponent<TilemapCollider2D>();
        wallCol.compositeOperation = Collider2D.CompositeOperation.Merge;
        var wallRB   = wallGO.AddComponent<Rigidbody2D>();
        wallRB.bodyType = RigidbodyType2D.Static;
        wallGO.AddComponent<CompositeCollider2D>();

        var riverGO   = new GameObject("Tilemap_River");
        riverGO.transform.SetParent(gridGO.transform);
        var riverTM   = riverGO.AddComponent<Tilemap>();
        var riverRend = riverGO.AddComponent<TilemapRenderer>();
        riverRend.sortingOrder = 1;
        var riverCol  = riverGO.AddComponent<TilemapCollider2D>();
        riverCol.compositeOperation = Collider2D.CompositeOperation.Merge;
        var riverRB   = riverGO.AddComponent<Rigidbody2D>();
        riverRB.bodyType = RigidbodyType2D.Static;
        riverGO.AddComponent<CompositeCollider2D>();

        var treeGO   = new GameObject("Tilemap_Trees");
        treeGO.transform.SetParent(gridGO.transform);
        var treeTM   = treeGO.AddComponent<Tilemap>();
        var treeRend = treeGO.AddComponent<TilemapRenderer>();
        treeRend.sortingOrder = 2;
        var treeCol  = treeGO.AddComponent<TilemapCollider2D>();
        treeCol.compositeOperation = Collider2D.CompositeOperation.Merge;
        var treeRB   = treeGO.AddComponent<Rigidbody2D>();
        treeRB.bodyType = RigidbodyType2D.Static;
        treeGO.AddComponent<CompositeCollider2D>();

        // ── Manager GO ───────────────────────────────────────────────────────
        var mgrGO     = new GameObject("CampManager");
        var campMan   = mgrGO.AddComponent<CampManager>();
        var mapGen    = mgrGO.AddComponent<CampMapGenerator>();
        var campScene = mgrGO.AddComponent<CampSceneManager>();

        // Wire tilemap and light references via SerializedObject
        var soMapGen = new SerializedObject(mapGen);
        soMapGen.FindProperty("floorTilemap").objectReferenceValue  = floorTM;
        soMapGen.FindProperty("wallTilemap").objectReferenceValue   = wallTM;
        soMapGen.FindProperty("riverTilemap").objectReferenceValue  = riverTM;
        soMapGen.FindProperty("treeTilemap").objectReferenceValue   = treeTM;
        soMapGen.FindProperty("globalLight").objectReferenceValue   = light;
        soMapGen.ApplyModifiedProperties();

        // Wire CampCameraFollow -> mapGen
        var campCam = camGO.GetComponent<CampCameraFollow>();
        var soCam   = new SerializedObject(campCam);
        soCam.FindProperty("mapGen").objectReferenceValue = mapGen;
        soCam.ApplyModifiedProperties();

        // ── EventSystem ───────────────────────────────────────────────────────
        var esGO = new GameObject("EventSystem");
        esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
        esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

        // ── Save ──────────────────────────────────────────────────────────────
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        // Add to Build Settings if missing
        AddSceneToBuildSettings(scenePath);

        Debug.Log("[CampSceneSetup] Done! Now:\n" +
                  "1. Select CampManager GO and assign:\n" +
                  "   • Tile assets (floorTiles, wallTiles) in CampMapGenerator\n" +
                  "   • TentDefinition array in CampMapGenerator + CampManager\n" +
                  "   • Tent prefab + Player prefab in CampMapGenerator\n" +
                  "2. Create a CampTentObject prefab (SpriteRenderer + CircleCollider2D trigger + CampTentObject)\n" +
                  "3. Create a Player prefab (SpriteRenderer + Rigidbody2D + CampPlayerController)\n" +
                  "4. Press Play in CampScene to test.");

        Selection.activeGameObject = mgrGO;
        EditorGUIUtility.PingObject(mgrGO);
    }

    [MenuItem("Camp/Add CampScene to Build Settings")]
    public static void AddCampSceneToBuildSettings()
        => AddSceneToBuildSettings("Assets/Scenes/CampScene.unity");

    private static void AddSceneToBuildSettings(string path)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
            EditorBuildSettings.scenes);

        foreach (var s in scenes)
            if (s.path == path) return; // already there

        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"[CampSceneSetup] Added {path} to Build Settings.");
    }
}
