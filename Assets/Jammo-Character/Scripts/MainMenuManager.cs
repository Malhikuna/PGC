using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>
/// MainMenuManager controls the 3D Main Menu scene for the Space Fighter game.
/// Provides an interactive sci-fi spaceship showcase, atmospheric cosmic skybox,
/// full menu navigation (Play, Controls, Settings, Credits, Exit), audio controls,
/// and gamepad/keyboard/mouse input support.
/// </summary>
[ExecuteAlways]
public class MainMenuManager : MonoBehaviour
{
    public static MainMenuManager Instance { get; private set; }

    [Header("Scene Configuration")]
    public string gameplaySceneName = "GamePlay";
    public Material skyboxMaterial;
    public GameObject spaceshipPrefab;

    [Header("Audio Settings")]
    public AudioClip menuMusicClip;
    public AudioClip hoverSfxClip;
    public AudioClip clickSfxClip;
    public AudioClip launchSfxClip;

    [Header("UI Colors")]
    public Color cyanAccent = new Color(0.25f, 0.88f, 1.0f, 1.0f);
    public Color goldAccent = new Color(1.0f, 0.72f, 0.12f, 1.0f);
    public Color redAccent = new Color(1.0f, 0.32f, 0.32f, 1.0f);
    public Color darkBgColor = new Color(0.04f, 0.06f, 0.10f, 0.94f);

    // Runtime state
    private AudioSource musicSource;
    private AudioSource sfxSource;
    private GameObject showcaseShip;
    private Vector3 shipOriginPos = new Vector3(1.6f, 0.15f, 0f);
    private Quaternion shipOriginRot = Quaternion.Euler(-10f, -42f, 12f);

    private Canvas mainCanvas;
    private GameObject menuPanel;
    private GameObject controlsModal;
    private GameObject settingsModal;
    private GameObject creditsModal;

    private Slider musicVolumeSlider;
    private Slider sfxVolumeSlider;
    private Toggle fullscreenToggle;
    private Text launchButtonText;
    private bool isTransitioning = false;

    private int selectedButtonIndex = 0;
    private readonly List<Button> mainButtons = new List<Button>();
    private readonly List<GameObject> buttonHighlights = new List<GameObject>();

    private Sprite cachedSolidSprite;
    private Sprite cachedBorderSprite;
    private Font defaultFont;

    private void Awake()
    {
        Instance = this;
        EnsureResources();
        EnsureEnvironment();
        EnsureSpaceshipShowcase();
        EnsureUI();
        EnsureAudio();
    }

    private void OnEnable()
    {
        Instance = this;
        EnsureResources();
        EnsureEnvironment();
        EnsureSpaceshipShowcase();
        EnsureUI();
        EnsureAudio();
    }

    private void Start()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        EnsureResources();
        EnsureEnvironment();
        EnsureSpaceshipShowcase();
        EnsureUI();
        EnsureAudio();

        if (Application.isPlaying)
        {
            float musicVol = PlayerPrefs.GetFloat("MusicVolume", 0.75f);
            if (musicSource != null)
            {
                musicSource.volume = musicVol;
                if (!musicSource.isPlaying && musicMusicClipValid())
                {
                    musicSource.Play();
                }
            }
            SelectButton(0, playSound: false);
        }
    }

    private bool musicMusicClipValid()
    {
        return musicSource != null && musicSource.clip != null;
    }

    private void Update()
    {
        // Animate idle floating spaceship in background
        if (showcaseShip != null)
        {
            float t = Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
            Vector3 bobbingPos = shipOriginPos;
            bobbingPos.y += Mathf.Sin(t * 1.3f) * 0.065f;
            bobbingPos.x += Mathf.Cos(t * 0.85f) * 0.035f;
            showcaseShip.transform.position = bobbingPos;

            Quaternion idleTilt = Quaternion.Euler(
                Mathf.Sin(t * 1.1f) * 2.2f,
                Mathf.Cos(t * 0.75f) * 3.0f,
                Mathf.Sin(t * 1.4f) * 1.8f
            );
            showcaseShip.transform.rotation = shipOriginRot * idleTilt;
        }

        if (!Application.isPlaying) return;

        HandleKeyboardGamepadNavigation();
    }

    private void HandleKeyboardGamepadNavigation()
    {
        // Modal back / close with ESC or Gamepad B
        if (AnyModalOpen())
        {
            bool cancelPressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

            if (cancelPressed)
            {
                CloseAllModals();
                PlaySfx(clickSfxClip, 0.8f);
            }
            return;
        }

        // Navigate main menu with keyboard arrows or Gamepad D-pad/Stick
        bool navDown = false;
        bool navUp = false;
        bool confirm = false;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame) navDown = true;
            if (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame) navUp = true;
            if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame) confirm = true;
        }

        if (Gamepad.current != null)
        {
            if (Gamepad.current.dpad.down.wasPressedThisFrame || Gamepad.current.leftStick.down.wasPressedThisFrame) navDown = true;
            if (Gamepad.current.dpad.up.wasPressedThisFrame || Gamepad.current.leftStick.up.wasPressedThisFrame) navUp = true;
            if (Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.startButton.wasPressedThisFrame) confirm = true;
        }

        if (mainButtons.Count > 0)
        {
            if (navDown)
            {
                int next = (selectedButtonIndex + 1) % mainButtons.Count;
                SelectButton(next, playSound: true);
            }
            else if (navUp)
            {
                int prev = (selectedButtonIndex - 1 + mainButtons.Count) % mainButtons.Count;
                SelectButton(prev, playSound: true);
            }
            else if (confirm)
            {
                if (selectedButtonIndex >= 0 && selectedButtonIndex < mainButtons.Count)
                {
                    mainButtons[selectedButtonIndex].onClick.Invoke();
                }
            }
        }
    }

    private void SelectButton(int index, bool playSound)
    {
        if (index < 0 || index >= mainButtons.Count) return;
        selectedButtonIndex = index;

        for (int i = 0; i < buttonHighlights.Count; i++)
        {
            if (buttonHighlights[i] != null)
            {
                buttonHighlights[i].SetActive(i == index);
            }
        }

        if (playSound)
        {
            PlaySfx(hoverSfxClip, 0.65f);
        }
    }

    // =========================================================================
    // ENVIRONMENT SETUP (Skybox, Camera, Lights)
    // =========================================================================

    public void EnsureEnvironment()
    {
        // 1. Skybox
        if (skyboxMaterial == null)
        {
#if UNITY_EDITOR
            skyboxMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Textures/Skybox/Skybox.mat");
#endif
        }
        if (skyboxMaterial != null && RenderSettings.skybox != skyboxMaterial)
        {
            RenderSettings.skybox = skyboxMaterial;
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.18f, 0.28f, 0.42f);
        RenderSettings.ambientEquatorColor = new Color(0.10f, 0.16f, 0.24f);
        RenderSettings.ambientGroundColor = new Color(0.04f, 0.06f, 0.10f);

        // 2. Camera
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camObj = GameObject.Find("Main Camera");
            if (camObj == null) camObj = new GameObject("Main Camera");
            cam = camObj.GetComponent<Camera>();
            if (cam == null) cam = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
        }

        cam.transform.position = new Vector3(0f, 0.9f, -5.5f);
        cam.transform.rotation = Quaternion.Euler(5.5f, 0f, 0f);
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 58f;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 1200f;

        if (cam.GetComponent<AudioListener>() == null)
        {
            cam.gameObject.AddComponent<AudioListener>();
        }

        // 3. Directional Key Light
        Transform keyLightTrans = transform.Find("Menu_KeyLight");
        GameObject keyLightObj = (keyLightTrans != null) ? keyLightTrans.gameObject : new GameObject("Menu_KeyLight");
        if (keyLightTrans == null) keyLightObj.transform.SetParent(transform, false);

        keyLightObj.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
        Light keyL = keyLightObj.GetComponent<Light>();
        if (keyL == null) keyL = keyLightObj.AddComponent<Light>();
        keyL.type = LightType.Directional;
        keyL.color = new Color(0.95f, 0.98f, 1.0f);
        keyL.intensity = 1.35f;

        // 4. Fill Rim Light
        Transform rimLightTrans = transform.Find("Menu_RimLight");
        GameObject rimLightObj = (rimLightTrans != null) ? rimLightTrans.gameObject : new GameObject("Menu_RimLight");
        if (rimLightTrans == null) rimLightObj.transform.SetParent(transform, false);

        rimLightObj.transform.rotation = Quaternion.Euler(-25f, 145f, 0f);
        Light rimL = rimLightObj.GetComponent<Light>();
        if (rimL == null) rimL = rimLightObj.AddComponent<Light>();
        rimL.type = LightType.Directional;
        rimL.color = new Color(0.25f, 0.65f, 1.0f);
        rimL.intensity = 0.85f;
    }

    // =========================================================================
    // 3D SPACESHIP SHOWCASE
    // =========================================================================

    public void EnsureSpaceshipShowcase()
    {
        Transform shipTrans = transform.Find("Showcase_Spaceship");
        if (shipTrans != null)
        {
            showcaseShip = shipTrans.gameObject;
        }
        else
        {
            if (spaceshipPrefab == null)
            {
#if UNITY_EDITOR
                spaceshipPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/StarSparrow/Prefabs/ModularExamples/StarSparrow21.prefab");
#endif
            }

            if (spaceshipPrefab != null)
            {
                showcaseShip = Instantiate(spaceshipPrefab, transform);
                showcaseShip.name = "Showcase_Spaceship";
            }
            else
            {
                showcaseShip = new GameObject("Showcase_Spaceship");
                showcaseShip.transform.SetParent(transform, false);
            }
        }

        showcaseShip.transform.position = shipOriginPos;
        showcaseShip.transform.rotation = shipOriginRot;
        showcaseShip.transform.localScale = new Vector3(0.85f, 0.85f, 0.85f);

        // Remove any gameplay scripts on showcase ship
        MonoBehaviour[] scripts = showcaseShip.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < scripts.Length; i++)
        {
            if (scripts[i] != null && !(scripts[i] is MainMenuManager))
            {
                string sName = scripts[i].GetType().Name;
                if (sName.Contains("SpaceshipController") || sName.Contains("EnemySpaceship") || sName.Contains("MovementInput"))
                {
                    if (Application.isPlaying) Destroy(scripts[i]);
                    else DestroyImmediate(scripts[i]);
                }
            }
        }

        // Add Thruster Plumes to the 3 nozzles
        EnsureShowcaseThrusters();
    }

    private void EnsureShowcaseThrusters()
    {
        if (showcaseShip == null) return;

        Vector3[] nozzlePositions = new Vector3[]
        {
            new Vector3(0f, 0.117f, -4.95f),
            new Vector3(0.36f, -0.486f, -4.90f),
            new Vector3(-0.36f, -0.486f, -4.90f)
        };
        float[] nozzleScales = new float[] { 0.36f, 0.24f, 0.24f };

        for (int i = 0; i < 3; i++)
        {
            string plumeName = "Showcase_Plume_" + i;
            Transform pTrans = showcaseShip.transform.Find(plumeName);
            GameObject plumeObj = (pTrans != null) ? pTrans.gameObject : null;
            if (plumeObj == null)
            {
                plumeObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                plumeObj.name = plumeName;
                plumeObj.transform.SetParent(showcaseShip.transform, false);
                Collider col = plumeObj.GetComponent<Collider>();
                if (col != null)
                {
                    if (Application.isPlaying) Destroy(col);
                    else DestroyImmediate(col);
                }
            }

            plumeObj.transform.localPosition = nozzlePositions[i];
            plumeObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            float baseScale = nozzleScales[i];
            plumeObj.transform.localScale = new Vector3(baseScale, baseScale * 2.2f, baseScale);

            Renderer rend = plumeObj.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.sharedMaterial = GetOrCreatePlumeMaterial();
            }
        }

        // Thruster Engine Glow Light
        Transform glowTrans = showcaseShip.transform.Find("Showcase_EngineGlow");
        GameObject glowObj = (glowTrans != null) ? glowTrans.gameObject : new GameObject("Showcase_EngineGlow");
        if (glowTrans == null) glowObj.transform.SetParent(showcaseShip.transform, false);

        glowObj.transform.localPosition = new Vector3(0f, -0.15f, -5.3f);
        Light gLight = glowObj.GetComponent<Light>();
        if (gLight == null) gLight = glowObj.AddComponent<Light>();
        gLight.type = LightType.Point;
        gLight.color = new Color(0.2f, 0.8f, 1.0f);
        gLight.intensity = 9f;
        gLight.range = 16f;
    }

    private Material cachedPlumeMat;
    private Material GetOrCreatePlumeMaterial()
    {
        if (cachedPlumeMat != null) return cachedPlumeMat;

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Mobile/Particles/Additive");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        cachedPlumeMat = new Material(sh);
        cachedPlumeMat.name = "ShowcasePlume_Mat";
        if (cachedPlumeMat.HasProperty("_BaseColor"))
            cachedPlumeMat.SetColor("_BaseColor", new Color(0.15f, 0.85f, 1.0f, 0.95f));
        else if (cachedPlumeMat.HasProperty("_Color"))
            cachedPlumeMat.SetColor("_Color", new Color(0.15f, 0.85f, 1.0f, 0.95f));

        return cachedPlumeMat;
    }

    // =========================================================================
    // AUDIO SETUP
    // =========================================================================

    public void EnsureAudio()
    {
        if (menuMusicClip == null)
        {
#if UNITY_EDITOR
            menuMusicClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/space.mp3");
#endif
        }
        if (hoverSfxClip == null)
        {
#if UNITY_EDITOR
            hoverSfxClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/SlimUI/Modern Menu 1/Audio/Clicks/SFX_Click_Whoosh.mp3");
#endif
        }
        if (clickSfxClip == null)
        {
#if UNITY_EDITOR
            clickSfxClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/SlimUI/Modern Menu 1/Audio/Clicks/SFX_Click_Mechanical.mp3");
#endif
        }
        if (launchSfxClip == null)
        {
#if UNITY_EDITOR
            launchSfxClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/blaster-shot.mp3");
#endif
        }

        Transform mTrans = transform.Find("Menu_MusicSource");
        GameObject mObj = (mTrans != null) ? mTrans.gameObject : new GameObject("Menu_MusicSource");
        if (mTrans == null) mObj.transform.SetParent(transform, false);

        musicSource = mObj.GetComponent<AudioSource>();
        if (musicSource == null) musicSource = mObj.AddComponent<AudioSource>();
        musicSource.clip = menuMusicClip;
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.volume = PlayerPrefs.GetFloat("MusicVolume", 0.75f);

        Transform sfxTrans = transform.Find("Menu_SFXSource");
        GameObject sfxObj = (sfxTrans != null) ? sfxTrans.gameObject : new GameObject("Menu_SFXSource");
        if (sfxTrans == null) sfxObj.transform.SetParent(transform, false);

        sfxSource = sfxObj.GetComponent<AudioSource>();
        if (sfxSource == null) sfxSource = sfxObj.AddComponent<AudioSource>();
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;
        sfxSource.volume = PlayerPrefs.GetFloat("SFXVolume", 0.85f);

        if (Application.isPlaying && musicSource.clip != null && !musicSource.isPlaying)
        {
            musicSource.Play();
        }
    }

    public void PlaySfx(AudioClip clip, float volumeScale = 1.0f)
    {
        if (clip == null || sfxSource == null) return;
        float sfxMaster = PlayerPrefs.GetFloat("SFXVolume", 0.85f);
        sfxSource.PlayOneShot(clip, volumeScale * sfxMaster);
    }

    // =========================================================================
    // UI CANVAS & MENUS SETUP
    // =========================================================================

    public void EnsureUI()
    {
        EnsureEventSystem();

        Transform canvasTrans = transform.Find("MainMenu_Canvas");
        GameObject canvasObj = (canvasTrans != null) ? canvasTrans.gameObject : new GameObject("MainMenu_Canvas");
        if (canvasTrans == null) canvasObj.transform.SetParent(transform, false);

        mainCanvas = canvasObj.GetComponent<Canvas>();
        if (mainCanvas == null) mainCanvas = canvasObj.AddComponent<Canvas>();
        mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        mainCanvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (canvasObj.GetComponent<GraphicRaycaster>() == null)
        {
            canvasObj.AddComponent<GraphicRaycaster>();
        }

        // Clean up legacy broken Main_Menu if it was placed under this scene
        GameObject legacyMenu = GameObject.Find("Main_Menu");
        if (legacyMenu != null && legacyMenu != gameObject && !legacyMenu.transform.IsChildOf(transform))
        {
            if (Application.isPlaying) Destroy(legacyMenu);
            else DestroyImmediate(legacyMenu);
        }

        // Build Left Navigation Panel
        BuildLeftNavigationPanel(canvasObj.transform);

        // Build Modals
        BuildControlsModal(canvasObj.transform);
        BuildSettingsModal(canvasObj.transform);
        BuildCreditsModal(canvasObj.transform);
    }

    private void EnsureEventSystem()
    {
        EventSystem es = FindAnyObjectByType<EventSystem>();
        if (es == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            es = esObj.AddComponent<EventSystem>();
        }

        StandaloneInputModule legacyMod = es.GetComponent<StandaloneInputModule>();
        if (legacyMod != null)
        {
            if (Application.isPlaying) Destroy(legacyMod);
            else DestroyImmediate(legacyMod);
        }

        if (es.GetComponent<InputSystemUIInputModule>() == null)
        {
            es.gameObject.AddComponent<InputSystemUIInputModule>();
        }
    }

    private void BuildLeftNavigationPanel(Transform canvasRoot)
    {
        Transform panelTrans = canvasRoot.Find("LeftMenu_Panel");
        menuPanel = (panelTrans != null) ? panelTrans.gameObject : new GameObject("LeftMenu_Panel", typeof(RectTransform), typeof(Image));
        if (panelTrans == null) menuPanel.transform.SetParent(canvasRoot, false);

        RectTransform panelRect = menuPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = new Vector2(90f, 0f);
        panelRect.sizeDelta = new Vector2(580f, 0f);

        Image panelBg = menuPanel.GetComponent<Image>();
        panelBg.sprite = cachedBorderSprite;
        panelBg.type = Image.Type.Sliced;
        panelBg.color = darkBgColor;

        // Title Block
        GetOrCreateText(
            menuPanel.transform,
            "Title_Header",
            "SPACE FIGHTER",
            52,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            cyanAccent,
            new Vector2(290f, 380f),
            new Vector2(520f, 65f)
        );

        GetOrCreateText(
            menuPanel.transform,
            "Title_Sub",
            "GALAXY INFILTRATOR",
            24,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            goldAccent,
            new Vector2(290f, 328f),
            new Vector2(520f, 36f)
        );

        // Glowing horizontal line
        CreateDivider(menuPanel.transform, "Title_Divider", new Vector2(290f, 298f), new Vector2(500f, 4f), cyanAccent);

        GetOrCreateText(
            menuPanel.transform,
            "Title_StatusTag",
            "DEFENSE PROTOCOL ACTIVE  //  SECTOR 04",
            13,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            new Color(0.6f, 0.85f, 1.0f, 0.85f),
            new Vector2(290f, 275f),
            new Vector2(500f, 24f)
        );

        // 5 Primary Buttons
        mainButtons.Clear();
        buttonHighlights.Clear();

        string[] btnLabels = new string[]
        {
            "START MISSION",
            "CONTROLS & GUIDE",
            "SETTINGS",
            "CREDITS",
            "EXIT GAME"
        };

        Color[] btnAccents = new Color[]
        {
            goldAccent,
            cyanAccent,
            cyanAccent,
            cyanAccent,
            redAccent
        };

        float startY = 195f;
        float spacing = 88f;

        for (int i = 0; i < btnLabels.Length; i++)
        {
            int idx = i;
            Vector2 pos = new Vector2(290f, startY - (i * spacing));
            Vector2 size = new Vector2(490f, 66f);

            CreateMenuButton(
                menuPanel.transform,
                "Btn_" + i,
                btnLabels[i],
                pos,
                size,
                btnAccents[i],
                () => OnMenuButtonClicked(idx),
                out Button btn,
                out GameObject highlight,
                out Text lblText
            );

            if (idx == 0) launchButtonText = lblText;

            mainButtons.Add(btn);
            buttonHighlights.Add(highlight);
        }

        // Footer Info
        GetOrCreateText(
            menuPanel.transform,
            "Footer_Info",
            "PGC // HIKMAL MAULANA (233040091)   |   UNPAS",
            13,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            new Color(0.55f, 0.75f, 0.95f, 0.65f),
            new Vector2(290f, -440f),
            new Vector2(520f, 30f)
        );

        GetOrCreateText(
            menuPanel.transform,
            "Footer_ControlsHint",
            "[Mouse Click]  |  [W/S/Arrows] Navigate  |  [Space/Enter/Gamepad A] Confirm",
            12,
            FontStyle.Normal,
            TextAnchor.MiddleCenter,
            new Color(0.8f, 0.8f, 0.8f, 0.55f),
            new Vector2(290f, -470f),
            new Vector2(520f, 26f)
        );
    }

    private void CreateMenuButton(
        Transform parent,
        string name,
        string label,
        Vector2 anchoredPos,
        Vector2 size,
        Color accentColor,
        UnityEngine.Events.UnityAction onClickAction,
        out Button btnComp,
        out GameObject highlightObj,
        out Text labelText)
    {
        Transform existing = parent.Find(name);
        GameObject btnObj = (existing != null) ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        if (existing == null) btnObj.transform.SetParent(parent, false);

        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        Image bgImg = btnObj.GetComponent<Image>();
        bgImg.sprite = cachedBorderSprite;
        bgImg.type = Image.Type.Sliced;
        bgImg.color = new Color(accentColor.r * 0.25f, accentColor.g * 0.25f, accentColor.b * 0.25f, 0.90f);

        // Highlight selection border
        Transform hlTrans = btnObj.transform.Find("HighlightBorder");
        highlightObj = (hlTrans != null) ? hlTrans.gameObject : new GameObject("HighlightBorder", typeof(RectTransform), typeof(Image));
        if (hlTrans == null) highlightObj.transform.SetParent(btnObj.transform, false);

        RectTransform hlRect = highlightObj.GetComponent<RectTransform>();
        hlRect.anchorMin = Vector2.zero;
        hlRect.anchorMax = Vector2.one;
        hlRect.offsetMin = new Vector2(-3f, -3f);
        hlRect.offsetMax = new Vector2(3f, 3f);

        Image hlImg = highlightObj.GetComponent<Image>();
        hlImg.sprite = cachedBorderSprite;
        hlImg.type = Image.Type.Sliced;
        hlImg.color = accentColor;
        hlImg.raycastTarget = false;
        highlightObj.SetActive(false);

        btnComp = btnObj.GetComponent<Button>();
        btnComp.targetGraphic = bgImg;

        ColorBlock cb = btnComp.colors;
        cb.normalColor = bgImg.color;
        cb.highlightedColor = new Color(accentColor.r * 0.45f, accentColor.g * 0.45f, accentColor.b * 0.45f, 1f);
        cb.pressedColor = accentColor;
        cb.selectedColor = cb.highlightedColor;
        cb.fadeDuration = 0.08f;
        btnComp.colors = cb;

        btnComp.onClick.RemoveAllListeners();
        btnComp.onClick.AddListener(onClickAction);

        int bIdx = mainButtons.Count;
        EventTrigger trigger = btnObj.GetComponent<EventTrigger>();
        if (trigger == null) trigger = btnObj.AddComponent<EventTrigger>();
        trigger.triggers.Clear();

        EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener((_) =>
        {
            SelectButton(bIdx, playSound: true);
        });
        trigger.triggers.Add(enter);

        labelText = GetOrCreateText(
            btnObj.transform,
            "Btn_Label",
            label,
            18,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            Color.white,
            Vector2.zero,
            size
        );
    }

    private void OnMenuButtonClicked(int index)
    {
        if (isTransitioning) return;
        PlaySfx(clickSfxClip, 1.0f);

        switch (index)
        {
            case 0:
                StartGame();
                break;
            case 1:
                OpenControlsModal();
                break;
            case 2:
                OpenSettingsModal();
                break;
            case 3:
                OpenCreditsModal();
                break;
            case 4:
                QuitGame();
                break;
        }
    }

    // =========================================================================
    // MODALS (CONTROLS, SETTINGS, CREDITS)
    // =========================================================================

    private bool AnyModalOpen()
    {
        return (controlsModal != null && controlsModal.activeSelf)
            || (settingsModal != null && settingsModal.activeSelf)
            || (creditsModal != null && creditsModal.activeSelf);
    }

    private void CloseAllModals()
    {
        if (controlsModal != null) controlsModal.SetActive(false);
        if (settingsModal != null) settingsModal.SetActive(false);
        if (creditsModal != null) creditsModal.SetActive(false);
    }

    public void OpenControlsModal()
    {
        CloseAllModals();
        if (controlsModal != null) controlsModal.SetActive(true);
    }

    public void OpenSettingsModal()
    {
        CloseAllModals();
        if (settingsModal != null)
        {
            settingsModal.SetActive(true);
            if (musicVolumeSlider != null)
                musicVolumeSlider.value = PlayerPrefs.GetFloat("MusicVolume", 0.75f);
            if (sfxVolumeSlider != null)
                sfxVolumeSlider.value = PlayerPrefs.GetFloat("SFXVolume", 0.85f);
            if (fullscreenToggle != null)
                fullscreenToggle.isOn = Screen.fullScreen;
        }
    }

    public void OpenCreditsModal()
    {
        CloseAllModals();
        if (creditsModal != null) creditsModal.SetActive(true);
    }

    private void BuildControlsModal(Transform canvasRoot)
    {
        controlsModal = CreateModalWindow(
            canvasRoot,
            "Controls_Modal",
            "MISSION BRIEFING & CONTROLS",
            new Vector2(980f, 620f),
            cyanAccent,
            out GameObject window
        );

        // Column 1: Spaceship Flight Combat
        CreateDivider(window.transform, "Div_Col1", new Vector2(-230f, 215f), new Vector2(420f, 3f), cyanAccent);
        GetOrCreateText(
            window.transform,
            "Header_Ship",
            "SPACESHIP DOGFIGHT CONTROLS",
            19,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            cyanAccent,
            new Vector2(-230f, 235f),
            new Vector2(420f, 30f)
        );

        string shipControls =
            "<b>[ W ]</b> Throttle Forward (Accelerate)\n" +
            "<b>[ S ]</b> Reverse Throttle / Airbrake\n" +
            "<b>[ A / D ]</b> Yaw Turn Left / Right\n" +
            "<b>[ Mouse Move ]</b> Pitch Up/Down & Bank Roll\n" +
            "<b>[ Space / Shift ]</b> Vertical Thrusters / Boost\n" +
            "<b>[ Left Click / RT ]</b> Fire Dual Laser Blasters\n" +
            "<b>[ Right Click / LT ]</b> Target Lock-On Aim Assist\n" +
            "<b>[ E ]</b> Disembark to Station (when docked)";

        GetOrCreateText(
            window.transform,
            "Body_Ship",
            shipControls,
            16,
            FontStyle.Normal,
            TextAnchor.UpperLeft,
            Color.white,
            new Vector2(-230f, 30f),
            new Vector2(420f, 340f)
        );

        // Column 2: Space Station Jammo Controls
        CreateDivider(window.transform, "Div_Col2", new Vector2(230f, 215f), new Vector2(420f, 3f), goldAccent);
        GetOrCreateText(
            window.transform,
            "Header_Station",
            "STATION / CHARACTER CONTROLS",
            19,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            goldAccent,
            new Vector2(230f, 235f),
            new Vector2(420f, 30f)
        );

        string charControls =
            "<b>[ W / A / S / D ]</b> Walk / Run Movement\n" +
            "<b>[ Mouse ]</b> Third-Person Camera Look\n" +
            "<b>[ Space ]</b> Jump Obstacles\n" +
            "<b>[ Left Click ]</b> Fire Handheld Blaster\n" +
            "<b>[ E ]</b> Board Spaceship Cockpit\n\n" +
            "<b>MISSION OBJECTIVE:</b>\n" +
            "Board the StarSparrow Fighter, defend the Space Station from enemy fighter squadrons, and survive incoming assault waves!";

        GetOrCreateText(
            window.transform,
            "Body_Station",
            charControls,
            16,
            FontStyle.Normal,
            TextAnchor.UpperLeft,
            Color.white,
            new Vector2(230f, 30f),
            new Vector2(420f, 340f)
        );

        CreateCloseButton(window.transform, () => CloseAllModals(), new Vector2(0f, -250f));
        controlsModal.SetActive(false);
    }

    private void BuildSettingsModal(Transform canvasRoot)
    {
        settingsModal = CreateModalWindow(
            canvasRoot,
            "Settings_Modal",
            "AUDIO & DISPLAY SETTINGS",
            new Vector2(720f, 520f),
            goldAccent,
            out GameObject window
        );

        // Music Volume Slider
        GetOrCreateText(
            window.transform,
            "Label_Music",
            "MUSIC VOLUME",
            18,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            Color.white,
            new Vector2(-150f, 130f),
            new Vector2(240f, 30f)
        );

        musicVolumeSlider = CreateSlider(window.transform, "Slider_Music", new Vector2(120f, 130f), new Vector2(260f, 24f), (val) =>
        {
            PlayerPrefs.SetFloat("MusicVolume", val);
            PlayerPrefs.Save();
            if (musicSource != null) musicSource.volume = val;
        });

        // SFX Volume Slider
        GetOrCreateText(
            window.transform,
            "Label_SFX",
            "SFX VOLUME",
            18,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            Color.white,
            new Vector2(-150f, 50f),
            new Vector2(240f, 30f)
        );

        sfxVolumeSlider = CreateSlider(window.transform, "Slider_SFX", new Vector2(120f, 50f), new Vector2(260f, 24f), (val) =>
        {
            PlayerPrefs.SetFloat("SFXVolume", val);
            PlayerPrefs.Save();
            if (sfxSource != null) sfxSource.volume = val;
            PlaySfx(hoverSfxClip, 0.5f);
        });

        // Fullscreen Toggle
        GetOrCreateText(
            window.transform,
            "Label_Fullscreen",
            "FULLSCREEN DISPLAY",
            18,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            Color.white,
            new Vector2(-150f, -30f),
            new Vector2(240f, 30f)
        );

        fullscreenToggle = CreateToggle(window.transform, "Toggle_Fullscreen", new Vector2(120f, -30f), (isFull) =>
        {
            Screen.fullScreen = isFull;
        });

        // Quality Preset note
        GetOrCreateText(
            window.transform,
            "Label_Quality",
            "GRAPHICS PRESET: HIGH RESOLUTION URP",
            14,
            FontStyle.Normal,
            TextAnchor.MiddleCenter,
            new Color(0.7f, 0.85f, 1.0f, 0.8f),
            new Vector2(0f, -110f),
            new Vector2(500f, 30f)
        );

        CreateCloseButton(window.transform, () => CloseAllModals(), new Vector2(0f, -195f));
        settingsModal.SetActive(false);
    }

    private void BuildCreditsModal(Transform canvasRoot)
    {
        creditsModal = CreateModalWindow(
            canvasRoot,
            "Credits_Modal",
            "DEVELOPER CREDITS",
            new Vector2(720f, 480f),
            cyanAccent,
            out GameObject window
        );

        string creditsContent =
            "<b>PRAKTIKUM PEMROGRAMAN GAME (PGC)</b>\n\n" +
            "<color=#FFAA1D>PROJECT:</color> Space Fighter Dogfight & Combat System\n\n" +
            "<color=#4DEEEA>DEVELOPED BY:</color>\n" +
            "<b>Hikmal Maulana</b>\n" +
            "NIM: <b>233040091</b>\n\n" +
            "Program Studi Teknik Informatika\n" +
            "Fakultas Teknik - Universitas Pasundan (UNPAS)\n" +
            "Bandung, Indonesia";

        GetOrCreateText(
            window.transform,
            "Body_Credits",
            creditsContent,
            18,
            FontStyle.Normal,
            TextAnchor.MiddleCenter,
            Color.white,
            new Vector2(0f, 25f),
            new Vector2(620f, 280f)
        );

        CreateCloseButton(window.transform, () => CloseAllModals(), new Vector2(0f, -175f));
        creditsModal.SetActive(false);
    }

    private GameObject CreateModalWindow(
        Transform parent,
        string name,
        string title,
        Vector2 size,
        Color headerAccent,
        out GameObject windowObj)
    {
        Transform existing = parent.Find(name);
        GameObject modalObj = (existing != null) ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
        if (existing == null) modalObj.transform.SetParent(parent, false);

        RectTransform mRect = modalObj.GetComponent<RectTransform>();
        mRect.anchorMin = Vector2.zero;
        mRect.anchorMax = Vector2.one;
        mRect.offsetMin = Vector2.zero;
        mRect.offsetMax = Vector2.zero;

        Image mBg = modalObj.GetComponent<Image>();
        mBg.sprite = cachedSolidSprite;
        mBg.color = new Color(0.02f, 0.04f, 0.07f, 0.88f);

        // Center Window Frame
        Transform wTrans = modalObj.transform.Find("WindowFrame");
        windowObj = (wTrans != null) ? wTrans.gameObject : new GameObject("WindowFrame", typeof(RectTransform), typeof(Image));
        if (wTrans == null) windowObj.transform.SetParent(modalObj.transform, false);

        RectTransform wRect = windowObj.GetComponent<RectTransform>();
        wRect.anchorMin = new Vector2(0.5f, 0.5f);
        wRect.anchorMax = new Vector2(0.5f, 0.5f);
        wRect.pivot = new Vector2(0.5f, 0.5f);
        wRect.anchoredPosition = Vector2.zero;
        wRect.sizeDelta = size;

        Image wBg = windowObj.GetComponent<Image>();
        wBg.sprite = cachedBorderSprite;
        wBg.type = Image.Type.Sliced;
        wBg.color = darkBgColor;

        // Window Title
        GetOrCreateText(
            windowObj.transform,
            "Title",
            title,
            24,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            headerAccent,
            new Vector2(0f, (size.y * 0.5f) - 40f),
            new Vector2(size.x - 60f, 40f)
        );

        // Title Divider line
        CreateDivider(
            windowObj.transform,
            "Title_Div",
            new Vector2(0f, (size.y * 0.5f) - 65f),
            new Vector2(size.x - 100f, 3f),
            headerAccent
        );

        return modalObj;
    }

    private void CreateCloseButton(Transform parent, UnityEngine.Events.UnityAction onClose, Vector2 anchoredPos)
    {
        CreateMenuButton(
            parent,
            "Btn_Close",
            "BACK TO MENU",
            anchoredPos,
            new Vector2(280f, 54f),
            goldAccent,
            onClose,
            out _,
            out _,
            out _
        );
    }

    private Slider CreateSlider(Transform parent, string name, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction<float> onValueChanged)
    {
        Transform existing = parent.Find(name);
        GameObject sObj = (existing != null) ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Slider));
        if (existing == null) sObj.transform.SetParent(parent, false);

        RectTransform sRect = sObj.GetComponent<RectTransform>();
        sRect.anchorMin = new Vector2(0.5f, 0.5f);
        sRect.anchorMax = new Vector2(0.5f, 0.5f);
        sRect.pivot = new Vector2(0.5f, 0.5f);
        sRect.anchoredPosition = anchoredPos;
        sRect.sizeDelta = size;

        Slider slider = sObj.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;

        // Background
        Transform bgTrans = sObj.transform.Find("Background");
        GameObject bgObj = (bgTrans != null) ? bgTrans.gameObject : new GameObject("Background", typeof(RectTransform), typeof(Image));
        if (bgTrans == null) bgObj.transform.SetParent(sObj.transform, false);

        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        Image bgImg = bgObj.GetComponent<Image>();
        bgImg.sprite = cachedSolidSprite;
        bgImg.color = new Color(0.12f, 0.16f, 0.24f, 1f);

        // Fill Area
        Transform fillAreaTrans = sObj.transform.Find("Fill Area");
        GameObject fillAreaObj = (fillAreaTrans != null) ? fillAreaTrans.gameObject : new GameObject("Fill Area", typeof(RectTransform));
        if (fillAreaTrans == null) fillAreaObj.transform.SetParent(sObj.transform, false);

        RectTransform faRect = fillAreaObj.GetComponent<RectTransform>();
        faRect.anchorMin = Vector2.zero;
        faRect.anchorMax = Vector2.one;
        faRect.offsetMin = new Vector2(2f, 2f);
        faRect.offsetMax = new Vector2(-2f, -2f);

        Transform fillTrans = fillAreaObj.transform.Find("Fill");
        GameObject fillObj = (fillTrans != null) ? fillTrans.gameObject : new GameObject("Fill", typeof(RectTransform), typeof(Image));
        if (fillTrans == null) fillObj.transform.SetParent(fillAreaObj.transform, false);

        Image fillImg = fillObj.GetComponent<Image>();
        fillImg.sprite = cachedSolidSprite;
        fillImg.color = goldAccent;

        slider.targetGraphic = bgImg;
        slider.fillRect = fillObj.GetComponent<RectTransform>();

        slider.onValueChanged.RemoveAllListeners();
        slider.onValueChanged.AddListener(onValueChanged);

        return slider;
    }

    private Toggle CreateToggle(Transform parent, string name, Vector2 anchoredPos, UnityEngine.Events.UnityAction<bool> onToggleChanged)
    {
        Transform existing = parent.Find(name);
        GameObject tObj = (existing != null) ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Toggle));
        if (existing == null) tObj.transform.SetParent(parent, false);

        RectTransform tRect = tObj.GetComponent<RectTransform>();
        tRect.anchorMin = new Vector2(0.5f, 0.5f);
        tRect.anchorMax = new Vector2(0.5f, 0.5f);
        tRect.pivot = new Vector2(0.5f, 0.5f);
        tRect.anchoredPosition = anchoredPos;
        tRect.sizeDelta = new Vector2(36f, 36f);

        Toggle toggle = tObj.GetComponent<Toggle>();

        // Background box
        Transform bgTrans = tObj.transform.Find("Background");
        GameObject bgObj = (bgTrans != null) ? bgTrans.gameObject : new GameObject("Background", typeof(RectTransform), typeof(Image));
        if (bgTrans == null) bgObj.transform.SetParent(tObj.transform, false);

        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        Image bgImg = bgObj.GetComponent<Image>();
        bgImg.sprite = cachedBorderSprite;
        bgImg.type = Image.Type.Sliced;
        bgImg.color = new Color(0.12f, 0.18f, 0.28f, 1f);

        // Checkmark
        Transform checkTrans = bgObj.transform.Find("Checkmark");
        GameObject checkObj = (checkTrans != null) ? checkTrans.gameObject : new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        if (checkTrans == null) checkObj.transform.SetParent(bgObj.transform, false);

        RectTransform checkRect = checkObj.GetComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.2f, 0.2f);
        checkRect.anchorMax = new Vector2(0.8f, 0.8f);
        checkRect.offsetMin = Vector2.zero;
        checkRect.offsetMax = Vector2.zero;

        Image checkImg = checkObj.GetComponent<Image>();
        checkImg.sprite = cachedSolidSprite;
        checkImg.color = goldAccent;

        toggle.targetGraphic = bgImg;
        toggle.graphic = checkImg;
        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(onToggleChanged);

        return toggle;
    }

    // =========================================================================
    // BUTTON ACTIONS
    // =========================================================================

    public void StartGame()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        PlaySfx(launchSfxClip, 1.0f);
        if (launchButtonText != null)
        {
            launchButtonText.text = "LAUNCHING MISSION...";
            launchButtonText.color = cyanAccent;
        }

        StartCoroutine(LoadGameplayAsyncRoutine());
    }

    private IEnumerator LoadGameplayAsyncRoutine()
    {
        yield return new WaitForSecondsRealtime(0.4f);
        SceneManager.LoadScene(gameplaySceneName);
    }

    public void QuitGame()
    {
        PlaySfx(clickSfxClip, 1.0f);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // =========================================================================
    // UI HELPERS (Text, Dividers, Procedural Sprites)
    // =========================================================================

    private void EnsureResources()
    {
        if (defaultFont == null)
        {
            defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        if (cachedSolidSprite == null)
        {
            Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    tex.SetPixel(x, y, Color.white);
            tex.Apply();
            cachedSolidSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        }

        if (cachedBorderSprite == null)
        {
            int s = 32;
            Texture2D bTex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            bTex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    bool isBorder = (x < 3 || x >= s - 3 || y < 3 || y >= s - 3);
                    bTex.SetPixel(x, y, isBorder ? Color.white : new Color(1f, 1f, 1f, 0.35f));
                }
            }
            bTex.Apply();
            cachedBorderSprite = Sprite.Create(bTex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(6, 6, 6, 6));
        }
    }

    private Text GetOrCreateText(
        Transform parent,
        string name,
        string content,
        int fontSize,
        FontStyle fontStyle,
        TextAnchor alignment,
        Color color,
        Vector2 anchoredPos,
        Vector2 size)
    {
        Transform existing = parent.Find(name);
        GameObject tObj = (existing != null) ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Text));
        if (existing == null) tObj.transform.SetParent(parent, false);

        RectTransform rect = tObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        Text txt = tObj.GetComponent<Text>();
        if (defaultFont != null) txt.font = defaultFont;
        txt.fontSize = fontSize;
        txt.fontStyle = fontStyle;
        txt.alignment = alignment;
        txt.color = color;
        txt.text = content;
        txt.raycastTarget = false;

        return txt;
    }

    private void CreateDivider(Transform parent, string name, Vector2 anchoredPos, Vector2 size, Color color)
    {
        Transform existing = parent.Find(name);
        GameObject divObj = (existing != null) ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
        if (existing == null) divObj.transform.SetParent(parent, false);

        RectTransform rect = divObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        Image img = divObj.GetComponent<Image>();
        img.sprite = cachedSolidSprite;
        img.color = color;
        img.raycastTarget = false;
    }

    /// <summary>
    /// Context menu and Editor method to rebuild the Main Menu scene completely.
    /// </summary>
    [ContextMenu("Rebuild Main Menu")]
    public static void EnsureMainMenuInScene()
    {
        MainMenuManager mgr = FindAnyObjectByType<MainMenuManager>();
        if (mgr == null)
        {
            GameObject mgrObj = new GameObject("MainMenuManager");
            mgr = mgrObj.AddComponent<MainMenuManager>();
        }

        mgr.EnsureResources();
        mgr.EnsureEnvironment();
        mgr.EnsureSpaceshipShowcase();
        mgr.EnsureUI();
        mgr.EnsureAudio();
    }
}
