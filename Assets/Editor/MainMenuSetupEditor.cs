using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public class MainMenuSetupEditor
{
    static MainMenuSetupEditor()
    {
        EditorApplication.delayCall += CheckAndSetupCurrentScene;
    }

    private static void CheckAndSetupCurrentScene()
    {
        Scene current = SceneManager.GetActiveScene();
        if (current.IsValid() && current.name == "MainMenu")
        {
            SetupMainMenuSceneInternal(save: false);
        }
    }

    [MenuItem("Tools/Setup Main Menu Scene")]
    public static void SetupMainMenuScene()
    {
        string scenePath = "Assets/Scenes/MainMenu.unity";
        Scene activeScene = SceneManager.GetActiveScene();

        if (activeScene.path != scenePath)
        {
            activeScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        SetupMainMenuSceneInternal(save: true);
    }

    public static void SetupMainMenuSceneInternal(bool save)
    {
        // 1. Remove old broken SlimUI Main_Menu instance if present
        GameObject[] rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
        for (int i = 0; i < rootObjects.Length; i++)
        {
            GameObject obj = rootObjects[i];
            if (obj == null) continue;

            if (obj.name == "Main_Menu" || obj.name == "Main_Menu_Mobile")
            {
                Object.DestroyImmediate(obj);
            }
        }

        // 2. Set Skybox to SciFi Skybox.mat
        Material skyMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Textures/Skybox/Skybox.mat");
        if (skyMat != null)
        {
            RenderSettings.skybox = skyMat;
        }

        // 3. Find or Create MainMenuManager
        MainMenuManager mgr = Object.FindAnyObjectByType<MainMenuManager>();
        if (mgr == null)
        {
            GameObject mgrObj = new GameObject("MainMenuManager");
            mgr = mgrObj.AddComponent<MainMenuManager>();
        }

        mgr.skyboxMaterial = skyMat;
        mgr.spaceshipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/StarSparrow/Prefabs/ModularExamples/StarSparrow21.prefab");
        mgr.menuMusicClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/space.mp3");
        mgr.hoverSfxClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
            "Assets/SlimUI/Modern Menu 1/Audio/Clicks/SFX_Click_Whoosh.mp3");
        mgr.clickSfxClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
            "Assets/SlimUI/Modern Menu 1/Audio/Clicks/SFX_Click_Mechanical.mp3");
        mgr.launchSfxClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/blaster-shot.mp3");

        // 4. Run construction
        mgr.EnsureEnvironment();
        mgr.EnsureSpaceshipShowcase();
        mgr.EnsureUI();
        mgr.EnsureAudio();

        if (save)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("[MainMenuSetupEditor] Main Menu Scene successfully setup and saved!");
        }
    }
}
