using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Central GameManager for Space Station Defense in the GamePlay scene.
/// Responsibilities:
/// - Pre-wave countdown timer at game start and between waves before enemies jump in.
/// - Spawns waves of enemy spaceships (StarSparrow17) with Jumpspace arrival effects.
/// - Tracks Space Station health & renders the Circular Health Bar at the Top-Left of the screen.
/// - Renders the Locked Enemy Health Bar at the Top-Center of the camera/screen (plus 3D screen-space lock reticle).
/// - Pause Menu & Game Over Menu built using assets from 3D Modern Menu UI (SlimUI/Modern Menu 1).
/// </summary>
[ExecuteAlways]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Enemy Wave Spawning (StarSparrow17)")]
    [Tooltip("Prefab for the enemy spaceship (StarSparrow17).")]
    public GameObject enemyShipPrefab;
    [Tooltip("Center position of the Space Station that enemies attack and player/NPCs defend.")]
    public Vector3 stationCenterPosition = new Vector3(0f, 4f, 0f);
    [Tooltip("Countdown duration before Wave 1 starts when pressing Play (seconds).")]
    public float initialWaveDelay = 10.0f;
    [Tooltip("Countdown duration when transitioning to the next wave after clearing a wave (seconds).")]
    public float timeBetweenWaves = 8.0f;
    [Tooltip("Number of enemies in Wave 1.")]
    public int baseEnemiesPerWave = 3;
    [Tooltip("Additional enemies added per subsequent wave.")]
    public int extraEnemiesPerWave = 2;
    [Tooltip("Maximum concurrent active enemies at once.")]
    public int maxConcurrentEnemies = 10;

    [Header("Space Station Health")]
    public float stationMaxHealth = 1000f;
    public float stationCurrentHealth = 1000f;

    [Header("Live Mission State")]
    public int currentWave = 0;
    public int enemiesDestroyed = 0;
    public float survivalTime = 0f;
    public bool IsGameOver { get; private set; } = false;
    public bool IsPaused { get; private set; } = false;
    public string gameOverReason = "";

    [Header("3D Modern Menu UI Assets (Auto-Loaded)")]
    public Sprite modernPanelBackdropSprite;
    public Sprite modernPanelFrameSprite;
    public Sprite modernButtonNormalSprite;
    public Sprite modernButtonHoverSprite;
    public Sprite modernButtonPressSprite;
    public Sprite modernButtonHighlightSprite;
    public Sprite modernCornerDetailSprite;
    public Sprite modernDetailDividerSprite;
    public Sprite modernLoadingHighlightSprite;
    public TMP_FontAsset modernHeaderFont;
    public TMP_FontAsset modernBodyFont;
    public AudioClip modernHoverSfx;
    public AudioClip modernClickSfx;
    public AudioClip modernWhooshSfx;

    // UI References - Top-Left Circular Station Health Bar
    private Canvas hudCanvas;
    private Image stationRadialFillImage;
    private Text stationPercentText;
    private Text stationStatusText;
    private float lastStationHitTime = -99f;

    // UI References - Top-Center Locked Enemy Health Bar & Lock Reticle
    private GameObject lockedEnemyTopPanel;
    private Slider lockedEnemyHealthSlider;
    private Text lockedEnemyTitleText;
    private RectTransform targetLockReticleRect;
    private Text targetLockReticleDistText;

    // UI References - Wave Countdown & Alert Banner
    private GameObject waveBannerPanel;
    private Text waveBannerTitleText;
    private Text waveBannerTimerText;
    private Image waveBannerProgressFill;
    private float waveBannerHideTime = -1f;
    private string activeWaveAlertHeader = "";
    private string activeWaveAlertSub = "";

    // UI References - 3D Modern Menu UI Pause Modal
    private GameObject pauseMenuPanel;
    private Text pauseStatusText;
    private Text pauseFooterHintText;
    private Button[] pauseButtons = new Button[3];
    private Image[] pauseButtonBgImages = new Image[3];
    private GameObject[] pauseButtonHighlights = new GameObject[3];
    private Text[] pauseButtonLabels = new Text[3];
    private int selectedPauseButtonIndex = 0;

    // UI References - 3D Modern Menu UI Game Over Modal
    private GameObject gameOverPanel;
    private Text gameOverReasonText;
    private Text gameOverStatsText;
    private Text gameOverFooterHintText;
    private Button[] gameOverButtons = new Button[2];
    private Image[] gameOverButtonBgImages = new Image[2];
    private GameObject[] gameOverButtonHighlights = new GameObject[2];
    private Text[] gameOverButtonLabels = new Text[2];
    private int selectedGameOverButtonIndex = 0;

    // Menu Navigation & Audio
    private float lastMenuNavUnscaledTime = -10f;
    private AudioSource uiAudioSource;

    // Wave state
    private bool waveInProgress = false;
    private bool waitingForNextWave = false;
    private float nextWaveCountdown = 0f;
    private float currentWaveTotalCountdown = 10f;
    private int enemiesToSpawnInCurrentWave = 0;

    private static Sprite cachedRingSprite;
    private static Sprite cachedSolidSprite;
    private static Sprite cachedReticleBracketSprite;

    // Signature 3D Modern Menu UI Theme Colors (from ThemeSettings_ModernMenuUI.asset)
    private static readonly Color ModernGoldAccent = new Color(1.0f, 0.6862745f, 0.0f, 1.0f);
    private static readonly Color ModernCyanAccent = new Color(0.0f, 0.6677432f, 1.0f, 1.0f);
    private static readonly Color ModernCrimsonAccent = new Color(1.0f, 0.24f, 0.24f, 1.0f);

    private void OnEnable()
    {
        Instance = this;
        EnsureEnemyPrefabLoaded();
        EnsureModernMenuAssetsLoaded();
        EnsureHUDElements();
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            Time.timeScale = 1f;
        }
    }

    private void Awake()
    {
        Instance = this;
        EnsureEnemyPrefabLoaded();
        EnsureModernMenuAssetsLoaded();
    }

    private void Start()
    {
        EnsureEnemyPrefabLoaded();
        EnsureModernMenuAssetsLoaded();
        EnsureEventSystemAndAudio();
        EnsureHUDElements();

        if (!Application.isPlaying) return;

        Time.timeScale = 1f;
        IsPaused = false;
        IsGameOver = false;

        // Ensure generous pre-wave countdown timer even if older smaller values were serialized
        if (initialWaveDelay < 8.0f) initialWaveDelay = 10.0f;
        if (timeBetweenWaves < 6.0f) timeBetweenWaves = 8.0f;

        stationCurrentHealth = stationMaxHealth;
        currentWave = 0;
        enemiesDestroyed = 0;
        survivalTime = 0f;

        // Start with countdown timer before Wave 1 begins
        waitingForNextWave = true;
        waveInProgress = false;
        currentWaveTotalCountdown = initialWaveDelay;
        nextWaveCountdown = initialWaveDelay;

        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);

        UpdateHUDElements();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            EnsureEnemyPrefabLoaded();
            EnsureModernMenuAssetsLoaded();
            EnsureHUDElements();
            return;
        }

        if (IsGameOver)
        {
            HandleGameOverMenuInput();
            UpdateMenuVisuals();
            return;
        }

        // Check Pause toggle input (ESC / P on Keyboard, Start on Gamepad)
        if (WasPauseTogglePressed())
        {
            TogglePause();
            return;
        }

        if (IsPaused)
        {
            HandlePauseMenuInput();
            UpdateMenuVisuals();
            return;
        }

        float dt = Time.deltaTime;
        survivalTime += dt;

        UpdateWaveLogic(dt);
        UpdateHUDElements();
    }

    // =========================================================================
    // PAUSE & GAME OVER INPUT / NAVIGATION
    // =========================================================================

    private bool WasPauseTogglePressed()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.pKey.wasPressedThisFrame)
            {
                return true;
            }
        }

        if (Gamepad.current != null)
        {
            if (Gamepad.current.startButton.wasPressedThisFrame)
            {
                return true;
            }
        }

        return false;
    }

    public void TogglePause()
    {
        if (IsGameOver) return;

        if (IsPaused)
        {
            ResumeGame();
        }
        else
        {
            PauseGame();
        }
    }

    public void PauseGame()
    {
        if (IsGameOver || IsPaused) return;

        IsPaused = true;
        Time.timeScale = 0f;

        if (SpaceshipController.Instance != null)
        {
            SpaceshipController.Instance.StopRumble();
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        selectedPauseButtonIndex = 0;
        EnsureHUDElements();

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(true);
        }

        PlayUISound(modernWhooshSfx, 0.75f);
        UpdateMenuVisuals();
    }

    public void ResumeGame()
    {
        if (!IsPaused) return;

        IsPaused = false;
        Time.timeScale = 1f;

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        PlayUISound(modernClickSfx, 0.8f);
    }

    private void HandlePauseMenuInput()
    {
        // B button on controller also resumes
        if (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame)
        {
            ResumeGame();
            return;
        }

        int navDir = ReadMenuVerticalNavigation();
        if (navDir != 0)
        {
            selectedPauseButtonIndex = (selectedPauseButtonIndex + navDir + pauseButtons.Length) % pauseButtons.Length;
            PlayUISound(modernHoverSfx, 0.65f);
        }

        if (WasMenuConfirmPressed())
        {
            PlayUISound(modernClickSfx, 0.85f);
            if (selectedPauseButtonIndex == 0) ResumeGame();
            else if (selectedPauseButtonIndex == 1) RestartMission();
            else if (selectedPauseButtonIndex == 2) QuitMission();
        }
    }

    private void HandleGameOverMenuInput()
    {
        int navDir = ReadMenuVerticalNavigation();
        if (navDir != 0)
        {
            selectedGameOverButtonIndex = (selectedGameOverButtonIndex + navDir + gameOverButtons.Length) % gameOverButtons.Length;
            PlayUISound(modernHoverSfx, 0.65f);
        }

        if (WasMenuConfirmPressed() || (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame))
        {
            PlayUISound(modernClickSfx, 0.85f);
            if (selectedGameOverButtonIndex == 0) RestartMission();
            else if (selectedGameOverButtonIndex == 1) QuitMission();
        }
    }

    private int ReadMenuVerticalNavigation()
    {
        if (Time.unscaledTime - lastMenuNavUnscaledTime < 0.20f) return 0;

        int dir = 0;

        if (Gamepad.current != null)
        {
            if (Gamepad.current.dpad.down.wasPressedThisFrame || Gamepad.current.leftStick.ReadValue().y < -0.55f)
                dir = 1;
            else if (Gamepad.current.dpad.up.wasPressedThisFrame || Gamepad.current.leftStick.ReadValue().y > 0.55f)
                dir = -1;
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.sKey.wasPressedThisFrame || Keyboard.current.downArrowKey.wasPressedThisFrame)
                dir = 1;
            else if (Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.upArrowKey.wasPressedThisFrame)
                dir = -1;
        }

        if (dir != 0)
        {
            lastMenuNavUnscaledTime = Time.unscaledTime;
        }

        return dir;
    }

    private bool WasMenuConfirmPressed()
    {
        if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
        {
            return true;
        }

        if (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
        {
            return true;
        }

        return false;
    }

    public void RestartMission()
    {
        Time.timeScale = 1f;
        IsPaused = false;
        IsGameOver = false;

        if (SpaceshipController.Instance != null)
        {
            SpaceshipController.Instance.StopRumble();
        }

        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.buildIndex);
    }

    public void QuitMission()
    {
        Time.timeScale = 1f;
        IsPaused = false;

        if (SpaceshipController.Instance != null)
        {
            SpaceshipController.Instance.StopRumble();
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // =========================================================================
    // WAVE SPAWNING & COUNTDOWN LOGIC
    // =========================================================================

    private void UpdateWaveLogic(float dt)
    {
        // Clean up any null references in ActiveEnemies
        for (int i = EnemySpaceship.ActiveEnemies.Count - 1; i >= 0; i--)
        {
            if (EnemySpaceship.ActiveEnemies[i] == null)
            {
                EnemySpaceship.ActiveEnemies.RemoveAt(i);
            }
        }

        if (waitingForNextWave)
        {
            nextWaveCountdown -= dt;
            if (nextWaveCountdown <= 0f)
            {
                nextWaveCountdown = 0f;
                waitingForNextWave = false;
                StartNextWave();
            }
        }
        else if (waveInProgress)
        {
            if (enemiesToSpawnInCurrentWave <= 0 && EnemySpaceship.ActiveEnemies.Count == 0)
            {
                waveInProgress = false;
                waitingForNextWave = true;
                currentWaveTotalCountdown = Mathf.Max(1f, timeBetweenWaves);
                nextWaveCountdown = currentWaveTotalCountdown;

                // Repair bonus for surviving the wave
                stationCurrentHealth = Mathf.Min(stationMaxHealth, stationCurrentHealth + stationMaxHealth * 0.15f);
                if (SpaceshipController.Instance != null)
                {
                    SpaceshipController.Instance.shipCurrentHealth = Mathf.Min(
                        SpaceshipController.Instance.shipMaxHealth,
                        SpaceshipController.Instance.shipCurrentHealth + SpaceshipController.Instance.shipMaxHealth * 0.20f
                    );
                }
            }
        }
    }

    private void StartNextWave()
    {
        currentWave++;
        int count = Mathf.Min(maxConcurrentEnemies, baseEnemiesPerWave + (currentWave - 1) * extraEnemiesPerWave);
        enemiesToSpawnInCurrentWave = count;
        waveInProgress = true;

        ShowWaveBanner(
            $"WARNING — WAVE {currentWave} JUMPING IN!",
            $"{count} HOSTILE INTERCEPTORS EMERGING FROM JUMPSPACE",
            3.8f
        );
        StartCoroutine(SpawnWaveCoroutine(currentWave, count));
    }

    private IEnumerator SpawnWaveCoroutine(int waveNum, int count)
    {
        EnsureEnemyPrefabLoaded();

        float baseAngle = Random.Range(0f, 360f);
        for (int i = 0; i < count; i++)
        {
            if (IsGameOver) yield break;

            float angleDeg = baseAngle + (360f / Mathf.Max(1, count)) * i + Random.Range(-18f, 18f);
            float angleRad = angleDeg * Mathf.Deg2Rad;
            float distance = Random.Range(115f, 155f);
            float height = Random.Range(12f, 42f);

            Vector3 exitPos = stationCenterPosition + new Vector3(
                Mathf.Cos(angleRad) * distance,
                height,
                Mathf.Sin(angleRad) * distance
            );

            Vector3 toStation = (stationCenterPosition - exitPos).normalized;
            Quaternion exitRot = Quaternion.LookRotation(toStation, Vector3.up);

            SpawnSingleEnemyFromJumpspace(exitPos, exitRot, waveNum, i);
            enemiesToSpawnInCurrentWave = Mathf.Max(0, enemiesToSpawnInCurrentWave - 1);

            yield return new WaitForSeconds(0.45f);
        }
    }

    private void SpawnSingleEnemyFromJumpspace(Vector3 exitPos, Quaternion exitRot, int waveNum, int unitIdx)
    {
        GameObject enemyObj;
        if (enemyShipPrefab != null)
        {
            enemyObj = Instantiate(enemyShipPrefab, exitPos, exitRot);
        }
        else
        {
            enemyObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            enemyObj.transform.position = exitPos;
            enemyObj.transform.rotation = exitRot;
        }

        enemyObj.name = $"StarSparrow17_Enemy_W{waveNum}_{unitIdx + 1}";

        EnemySpaceship enemyCtrl = enemyObj.GetComponent<EnemySpaceship>();
        if (enemyCtrl == null)
        {
            enemyCtrl = enemyObj.AddComponent<EnemySpaceship>();
        }

        enemyCtrl.InitializeWithJumpspace(exitPos, exitRot, waveNum, unitIdx);
    }

    public Vector3 GetStationTargetPosition()
    {
        return stationCenterPosition;
    }

    public void DamageStation(float amount)
    {
        if (IsGameOver) return;

        lastStationHitTime = Time.time;
        stationCurrentHealth = Mathf.Max(0f, stationCurrentHealth - amount);

        if (stationCurrentHealth <= 0f)
        {
            SpaceshipExplosion.Spawn(stationCenterPosition, 2.2f);
            SpaceshipExplosion.Spawn(stationCenterPosition + new Vector3(14f, 4f, -10f), 1.6f);
            SpaceshipExplosion.Spawn(stationCenterPosition + new Vector3(-16f, 6f, 12f), 1.6f);
            TriggerGameOver("SPACE STATION DESTROYED BY ENEMY FLEET");
        }
    }

    public void OnEnemyDestroyed(EnemySpaceship destroyedEnemy)
    {
        enemiesDestroyed++;

        // If player had this enemy locked, automatically lock onto the next closest enemy if available
        if (SpaceshipController.Instance != null && SpaceshipController.Instance.LockedEnemy == destroyedEnemy)
        {
            SpaceshipController.Instance.OnLockedEnemyDestroyed(destroyedEnemy);
        }
    }

    public void TriggerGameOver(string reason)
    {
        if (IsGameOver) return;

        IsGameOver = true;
        IsPaused = false;
        Time.timeScale = 1f;
        gameOverReason = reason;

        if (SpaceshipController.Instance != null)
        {
            SpaceshipController.Instance.StopRumble();
        }

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        selectedGameOverButtonIndex = 0;
        EnsureHUDElements();
        UpdateHUDElements();

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }

        PlayUISound(modernWhooshSfx, 0.9f);
        UpdateMenuVisuals();
    }

    public void ShowWaveBanner(string title, string subtitle = "", float duration = 3.5f)
    {
        EnsureHUDElements();
        activeWaveAlertHeader = title;
        activeWaveAlertSub = subtitle;
        waveBannerHideTime = Time.time + duration;

        if (waveBannerPanel != null)
        {
            waveBannerPanel.SetActive(true);
        }
    }

    // =========================================================================
    // ASSET LOADING (STARSPARROW17 & 3D MODERN MENU UI)
    // =========================================================================

    private void EnsureEnemyPrefabLoaded()
    {
        if (enemyShipPrefab != null) return;

        enemyShipPrefab = Resources.Load<GameObject>("StarSparrow17");

#if UNITY_EDITOR
        if (enemyShipPrefab == null)
        {
            enemyShipPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/StarSparrow/Prefabs/ModularExamples/StarSparrow17.prefab");
            if (enemyShipPrefab == null)
            {
                enemyShipPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/StarSparrow/Prefabs/Examples/StarSparrow17.prefab");
            }
        }
#endif
    }

    private void EnsureModernMenuAssetsLoaded()
    {
#if UNITY_EDITOR
        if (modernPanelBackdropSprite == null)
            modernPanelBackdropSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/SlimUI/Modern Menu 1/Graphics/Frames/Panel 1920x1080px.png");
        if (modernPanelFrameSprite == null)
            modernPanelFrameSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/SlimUI/Modern Menu 1/Graphics/Frames/Panel Frame 512px.png");
        if (modernButtonNormalSprite == null)
            modernButtonNormalSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/SlimUI/Modern Menu 1/Graphics/Buttons/Button Fram 256px.png");
        if (modernButtonHoverSprite == null)
            modernButtonHoverSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/SlimUI/Modern Menu 1/Graphics/Buttons/Button Frame Hover 256px.png");
        if (modernButtonPressSprite == null)
            modernButtonPressSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/SlimUI/Modern Menu 1/Graphics/Buttons/Button Frame Press 256px.png");
        if (modernButtonHighlightSprite == null)
            modernButtonHighlightSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/SlimUI/Modern Menu 1/Graphics/Buttons/Button Highlighted 256px.png");
        if (modernCornerDetailSprite == null)
            modernCornerDetailSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/SlimUI/Modern Menu 1/Graphics/Misc/Corner Detail 256px.png");
        if (modernDetailDividerSprite == null)
            modernDetailDividerSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/SlimUI/Modern Menu 1/Graphics/Misc/Detail 1 256px.png");
        if (modernLoadingHighlightSprite == null)
            modernLoadingHighlightSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/SlimUI/Modern Menu 1/Graphics/Misc/LoadingHighlight 512px.png");

        if (modernHeaderFont == null)
            modernHeaderFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/SlimUI/Modern Menu 1/Fonts/LATO-BOLD SDF.asset");
        if (modernBodyFont == null)
            modernBodyFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/SlimUI/Modern Menu 1/Fonts/RUBIK-BOLD SDF.asset");

        if (modernHoverSfx == null)
            modernHoverSfx = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SlimUI/Modern Menu 1/Audio/Clicks/SFX_Click_Mechanical.mp3");
        if (modernClickSfx == null)
            modernClickSfx = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SlimUI/Modern Menu 1/Audio/Clicks/SFX_Click_Punch.ogg");
        if (modernWhooshSfx == null)
            modernWhooshSfx = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SlimUI/Modern Menu 1/Audio/Clicks/SFX_Click_Whoosh.mp3");
#endif
    }

    private void EnsureEventSystemAndAudio()
    {
        if (Application.isPlaying && EventSystem.current == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            esObj.AddComponent<EventSystem>();
            esObj.AddComponent<InputSystemUIInputModule>();
        }

        if (uiAudioSource == null)
        {
            uiAudioSource = GetComponent<AudioSource>();
            if (uiAudioSource == null)
            {
                uiAudioSource = gameObject.AddComponent<AudioSource>();
            }
            uiAudioSource.playOnAwake = false;
            uiAudioSource.ignoreListenerPause = true;
            uiAudioSource.spatialBlend = 0f;
        }
    }

    private void PlayUISound(AudioClip clip, float volume = 0.8f)
    {
        if (!Application.isPlaying || clip == null) return;
        EnsureEventSystemAndAudio();
        if (uiAudioSource != null)
        {
            uiAudioSource.PlayOneShot(clip, volume);
        }
    }

    // =========================================================================
    // HUD & 3D MODERN MENU UI CREATION
    // =========================================================================

    public void EnsureHUDElements()
    {
        EnsureProceduralSprites();
        EnsureModernMenuAssetsLoaded();

        GameObject canvasObj = GameObject.Find("Gameplay_HealthHUD_Canvas");
        if (canvasObj != null)
        {
            hudCanvas = canvasObj.GetComponent<Canvas>();
        }

        if (hudCanvas == null)
        {
            canvasObj = new GameObject("Gameplay_HealthHUD_Canvas");
            hudCanvas = canvasObj.AddComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hudCanvas.sortingOrder = 50;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();
        }

        Font uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // 1. Top-Left Circular Station Health Bar (with STATION & 100% text inside)
        EnsureStationCircularHealthUI(hudCanvas.transform, uiFont);

        // 2. Top-Center Locked Enemy Health Bar & Screen-Space Target Lock Reticle
        EnsureLockedEnemyHealthUI(hudCanvas.transform, uiFont);

        // 3. Top-Center Wave Countdown & Alert Banner (and remove old Top-Right box)
        EnsureWaveCountdownBannerUI(hudCanvas.transform, uiFont);

        // 4. Pause Menu Overlay (3D Modern Menu UI)
        EnsurePauseMenuUI(hudCanvas.transform, uiFont);

        // 5. Game Over Menu Overlay (3D Modern Menu UI)
        EnsureGameOverUI(hudCanvas.transform, uiFont);
    }

    private void EnsureStationCircularHealthUI(Transform canvasRoot, Font uiFont)
    {
        Transform existing = canvasRoot.Find("StationCircularHealth_Panel");
        GameObject panelObj;
        if (existing != null)
        {
            panelObj = existing.gameObject;
        }
        else
        {
            panelObj = new GameObject("StationCircularHealth_Panel", typeof(RectTransform));
            panelObj.transform.SetParent(canvasRoot, false);
        }

        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(24f, -24f);
        panelRect.sizeDelta = new Vector2(128f, 128f);

        // Outer Dark Circular Ring Background
        Transform bgRingTrans = panelObj.transform.Find("Circle_BgRing");
        GameObject bgRingObj = bgRingTrans != null ? bgRingTrans.gameObject : new GameObject("Circle_BgRing", typeof(RectTransform), typeof(Image));
        if (bgRingTrans == null) bgRingObj.transform.SetParent(panelObj.transform, false);

        RectTransform bgRingRect = bgRingObj.GetComponent<RectTransform>();
        bgRingRect.anchorMin = Vector2.zero;
        bgRingRect.anchorMax = Vector2.one;
        bgRingRect.offsetMin = Vector2.zero;
        bgRingRect.offsetMax = Vector2.zero;

        Image bgRingImg = bgRingObj.GetComponent<Image>();
        bgRingImg.sprite = cachedRingSprite;
        bgRingImg.type = Image.Type.Simple;
        bgRingImg.color = new Color(0.06f, 0.14f, 0.22f, 0.88f);

        // Radial 360 Filled Circular Health Bar
        Transform fillRingTrans = panelObj.transform.Find("Circle_RadialFill");
        GameObject fillRingObj = fillRingTrans != null ? fillRingTrans.gameObject : new GameObject("Circle_RadialFill", typeof(RectTransform), typeof(Image));
        if (fillRingTrans == null) fillRingObj.transform.SetParent(panelObj.transform, false);

        RectTransform fillRingRect = fillRingObj.GetComponent<RectTransform>();
        fillRingRect.anchorMin = Vector2.zero;
        fillRingRect.anchorMax = Vector2.one;
        fillRingRect.offsetMin = Vector2.zero;
        fillRingRect.offsetMax = Vector2.zero;

        stationRadialFillImage = fillRingObj.GetComponent<Image>();
        stationRadialFillImage.sprite = cachedRingSprite;
        stationRadialFillImage.type = Image.Type.Filled;
        stationRadialFillImage.fillMethod = Image.FillMethod.Radial360;
        stationRadialFillImage.fillOrigin = (int)Image.Origin360.Top;
        stationRadialFillImage.fillClockwise = true;
        stationRadialFillImage.fillAmount = 1f;
        stationRadialFillImage.color = new Color(0.15f, 0.92f, 0.85f, 1f);

        // Center text inside circular health bar: "STATION" and percentage (e.g. "100%")
        stationStatusText = GetOrCreateText(
            panelObj.transform,
            "Station_TitleText",
            uiFont,
            "STATION",
            12,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            new Color(0.75f, 0.95f, 1.0f, 1f),
            new Vector2(0f, 10f),
            new Vector2(100f, 20f)
        );

        stationPercentText = GetOrCreateText(
            panelObj.transform,
            "Station_PercentText",
            uiFont,
            "100%",
            21,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            Color.white,
            new Vector2(0f, -11f),
            new Vector2(100f, 30f)
        );
    }

    private void EnsureLockedEnemyHealthUI(Transform canvasRoot, Font uiFont)
    {
        Transform existing = canvasRoot.Find("LockedEnemyHealth_Panel");
        if (existing != null)
        {
            lockedEnemyTopPanel = existing.gameObject;
        }
        else
        {
            lockedEnemyTopPanel = new GameObject("LockedEnemyHealth_Panel", typeof(RectTransform), typeof(Image));
            lockedEnemyTopPanel.transform.SetParent(canvasRoot, false);
        }

        RectTransform panelRect = lockedEnemyTopPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -22f);
        panelRect.sizeDelta = new Vector2(460f, 58f);

        Image panelBg = lockedEnemyTopPanel.GetComponent<Image>();
        panelBg.sprite = modernPanelFrameSprite != null ? modernPanelFrameSprite : cachedSolidSprite;
        panelBg.color = new Color(0.12f, 0.04f, 0.06f, 0.90f);

        lockedEnemyTitleText = GetOrCreateText(
            lockedEnemyTopPanel.transform,
            "LockedEnemy_TitleText",
            uiFont,
            "TARGET LOCKED: INTERCEPTOR",
            13,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            new Color(1.0f, 0.45f, 0.45f, 1f),
            new Vector2(0f, 13f),
            new Vector2(440f, 22f)
        );

        // Slider inside Top-Center Panel
        Transform sliderTrans = lockedEnemyTopPanel.transform.Find("LockedEnemy_Slider");
        GameObject sliderObj;
        if (sliderTrans != null)
        {
            sliderObj = sliderTrans.gameObject;
        }
        else
        {
            sliderObj = new GameObject("LockedEnemy_Slider", typeof(RectTransform), typeof(Slider));
            sliderObj.transform.SetParent(lockedEnemyTopPanel.transform, false);
        }

        RectTransform sliderRect = sliderObj.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0.5f, 0.5f);
        sliderRect.anchorMax = new Vector2(0.5f, 0.5f);
        sliderRect.pivot = new Vector2(0.5f, 0.5f);
        sliderRect.anchoredPosition = new Vector2(0f, -11f);
        sliderRect.sizeDelta = new Vector2(420f, 18f);

        Transform bgTrans = sliderObj.transform.Find("Background");
        GameObject bgObj = bgTrans != null ? bgTrans.gameObject : new GameObject("Background", typeof(RectTransform), typeof(Image));
        if (bgTrans == null) bgObj.transform.SetParent(sliderObj.transform, false);
        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        Image bgImg = bgObj.GetComponent<Image>();
        bgImg.sprite = cachedSolidSprite;
        bgImg.color = new Color(0.22f, 0.05f, 0.06f, 0.95f);

        Transform fillAreaTrans = sliderObj.transform.Find("Fill Area");
        GameObject fillAreaObj = fillAreaTrans != null ? fillAreaTrans.gameObject : new GameObject("Fill Area", typeof(RectTransform));
        if (fillAreaTrans == null) fillAreaObj.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRect = fillAreaObj.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = new Vector2(2f, 2f);
        fillAreaRect.offsetMax = new Vector2(-2f, -2f);

        Transform fillTrans = fillAreaObj.transform.Find("Fill");
        GameObject fillObj = fillTrans != null ? fillTrans.gameObject : new GameObject("Fill", typeof(RectTransform), typeof(Image));
        if (fillTrans == null) fillObj.transform.SetParent(fillAreaObj.transform, false);
        RectTransform fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fillImg = fillObj.GetComponent<Image>();
        fillImg.sprite = cachedSolidSprite;
        fillImg.color = new Color(1.0f, 0.18f, 0.22f, 1f);

        lockedEnemyHealthSlider = sliderObj.GetComponent<Slider>();
        lockedEnemyHealthSlider.interactable = false;
        lockedEnemyHealthSlider.transition = Selectable.Transition.None;
        lockedEnemyHealthSlider.direction = Slider.Direction.LeftToRight;
        lockedEnemyHealthSlider.targetGraphic = bgImg;
        lockedEnemyHealthSlider.fillRect = fillRect;
        lockedEnemyHealthSlider.minValue = 0f;
        lockedEnemyHealthSlider.maxValue = 100f;
        lockedEnemyHealthSlider.value = 100f;

        // 3D Screen-Space Target Lock Reticle Bracket
        Transform reticleTrans = canvasRoot.Find("TargetLock_ScreenReticle");
        GameObject reticleObj;
        if (reticleTrans != null)
        {
            reticleObj = reticleTrans.gameObject;
        }
        else
        {
            reticleObj = new GameObject("TargetLock_ScreenReticle", typeof(RectTransform), typeof(Image));
            reticleObj.transform.SetParent(canvasRoot, false);
        }

        targetLockReticleRect = reticleObj.GetComponent<RectTransform>();
        targetLockReticleRect.anchorMin = new Vector2(0.5f, 0.5f);
        targetLockReticleRect.anchorMax = new Vector2(0.5f, 0.5f);
        targetLockReticleRect.pivot = new Vector2(0.5f, 0.5f);
        targetLockReticleRect.sizeDelta = new Vector2(68f, 68f);

        Image retImg = reticleObj.GetComponent<Image>();
        retImg.sprite = cachedReticleBracketSprite;
        retImg.color = new Color(1.0f, 0.22f, 0.25f, 0.92f);
        retImg.raycastTarget = false;

        targetLockReticleDistText = GetOrCreateText(
            reticleObj.transform,
            "Reticle_DistText",
            uiFont,
            "LOCKED",
            11,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            new Color(1.0f, 0.35f, 0.35f, 1f),
            new Vector2(0f, -44f),
            new Vector2(120f, 18f)
        );

        if (!Application.isPlaying)
        {
            lockedEnemyTopPanel.SetActive(false);
            reticleObj.SetActive(false);
        }
    }

    private void EnsureWaveCountdownBannerUI(Transform canvasRoot, Font uiFont)
    {
        // Ensure top-right MissionTelemetry_Panel is removed
        Transform teleTrans = canvasRoot.Find("MissionTelemetry_Panel");
        if (teleTrans != null)
        {
            DestroyImmediate(teleTrans.gameObject);
        }

        Transform bannerTrans = canvasRoot.Find("WaveAlert_Banner");
        if (bannerTrans != null)
        {
            waveBannerPanel = bannerTrans.gameObject;
            // Clean up legacy Banner_Text if upgrading from single-text banner
            Transform oldText = waveBannerPanel.transform.Find("Banner_Text");
            if (oldText != null) DestroyImmediate(oldText.gameObject);
        }
        else
        {
            waveBannerPanel = new GameObject("WaveAlert_Banner", typeof(RectTransform), typeof(Image));
            waveBannerPanel.transform.SetParent(canvasRoot, false);
        }

        RectTransform bannerRect = waveBannerPanel.GetComponent<RectTransform>();
        bannerRect.anchorMin = new Vector2(0.5f, 1f);
        bannerRect.anchorMax = new Vector2(0.5f, 1f);
        bannerRect.pivot = new Vector2(0.5f, 1f);
        bannerRect.anchoredPosition = new Vector2(0f, -26f);
        bannerRect.sizeDelta = new Vector2(560f, 68f);

        Image bannerBg = waveBannerPanel.GetComponent<Image>();
        bannerBg.sprite = modernPanelFrameSprite != null ? modernPanelFrameSprite : cachedSolidSprite;
        bannerBg.color = new Color(0.06f, 0.10f, 0.16f, 0.92f);
        bannerBg.raycastTarget = false;

        // 3D Modern Menu UI Corner Accents on the Wave Countdown Banner
        CreateOrUpdateCornerDetail(waveBannerPanel.transform, "Corner_TL", new Vector2(-266f, 20f), 26f, -90f, ModernGoldAccent);
        CreateOrUpdateCornerDetail(waveBannerPanel.transform, "Corner_BR", new Vector2(266f, -20f), 26f, 90f, ModernGoldAccent);

        waveBannerTitleText = GetOrCreateText(
            waveBannerPanel.transform,
            "Banner_TitleText",
            uiFont,
            "PREPARE FOR DEFENSIVE COMBAT — WAVE 1",
            14,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            ModernGoldAccent,
            new Vector2(0f, 13f),
            new Vector2(520f, 24f)
        );

        waveBannerTimerText = GetOrCreateText(
            waveBannerPanel.transform,
            "Banner_TimerText",
            uiFont,
            "NEXT WAVE IN  00:10.0",
            18,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            Color.white,
            new Vector2(0f, -9f),
            new Vector2(520f, 26f)
        );

        // Progress Bar Track & Fill at the bottom of the Wave Banner
        Transform trackTrans = waveBannerPanel.transform.Find("Banner_ProgressTrack");
        GameObject trackObj = trackTrans != null ? trackTrans.gameObject : new GameObject("Banner_ProgressTrack", typeof(RectTransform), typeof(Image));
        if (trackTrans == null) trackObj.transform.SetParent(waveBannerPanel.transform, false);

        RectTransform trackRect = trackObj.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0.5f, 0f);
        trackRect.anchorMax = new Vector2(0.5f, 0f);
        trackRect.pivot = new Vector2(0.5f, 0f);
        trackRect.anchoredPosition = new Vector2(0f, 4f);
        trackRect.sizeDelta = new Vector2(510f, 5f);

        Image trackImg = trackObj.GetComponent<Image>();
        trackImg.sprite = cachedSolidSprite;
        trackImg.color = new Color(0.15f, 0.22f, 0.30f, 0.75f);
        trackImg.raycastTarget = false;

        Transform fillTrans = trackObj.transform.Find("Banner_ProgressFill");
        GameObject fillObj = fillTrans != null ? fillTrans.gameObject : new GameObject("Banner_ProgressFill", typeof(RectTransform), typeof(Image));
        if (fillTrans == null) fillObj.transform.SetParent(trackObj.transform, false);

        RectTransform fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        waveBannerProgressFill = fillObj.GetComponent<Image>();
        waveBannerProgressFill.sprite = modernDetailDividerSprite != null ? modernDetailDividerSprite : cachedSolidSprite;
        waveBannerProgressFill.type = Image.Type.Filled;
        waveBannerProgressFill.fillMethod = Image.FillMethod.Horizontal;
        waveBannerProgressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        waveBannerProgressFill.fillAmount = 1f;
        waveBannerProgressFill.color = ModernGoldAccent;
        waveBannerProgressFill.raycastTarget = false;

        if (!Application.isPlaying)
        {
            waveBannerPanel.SetActive(false);
        }
    }

    // =========================================================================
    // 3D MODERN MENU UI — PAUSE MENU
    // =========================================================================

    private void EnsurePauseMenuUI(Transform canvasRoot, Font uiFont)
    {
        Transform pauseTrans = canvasRoot.Find("Pause_ModalPanel");
        if (pauseTrans != null)
        {
            pauseMenuPanel = pauseTrans.gameObject;
        }
        else
        {
            pauseMenuPanel = new GameObject("Pause_ModalPanel", typeof(RectTransform), typeof(Image));
            pauseMenuPanel.transform.SetParent(canvasRoot, false);
        }

        RectTransform overlayRect = pauseMenuPanel.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        Image overlayBg = pauseMenuPanel.GetComponent<Image>();
        overlayBg.sprite = cachedSolidSprite;
        overlayBg.color = new Color(0.02f, 0.03f, 0.05f, 0.80f);

        // Center 3D Modern Menu UI Window Frame
        GameObject windowObj = GetOrCreateWindowFrame(
            pauseMenuPanel.transform,
            "Pause_ModernWindow",
            new Vector2(620f, 470f),
            new Color(0.09f, 0.11f, 0.15f, 0.96f),
            ModernGoldAccent
        );

        GetOrCreateText(
            windowObj.transform,
            "Pause_Header",
            uiFont,
            "SYSTEM PAUSED",
            32,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            Color.white,
            new Vector2(0f, 168f),
            new Vector2(540f, 46f)
        );

        // 3D Modern Menu UI Divider Bar under Header
        CreateOrUpdateDivider(windowObj.transform, "Pause_Divider", new Vector2(0f, 134f), new Vector2(180f, 6f), ModernGoldAccent);

        pauseStatusText = GetOrCreateText(
            windowObj.transform,
            "Pause_SubInfo",
            uiFont,
            "WAVE 1   |   STATION SHIELD: 100%   |   TIME: 00:00",
            15,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            ModernGoldAccent,
            new Vector2(0f, 102f),
            new Vector2(540f, 28f)
        );

        // 3 Interactive 3D Modern Menu UI Buttons
        CreateOrUpdateModernButton(
            windowObj.transform,
            "Btn_Resume",
            "RESUME MISSION",
            uiFont,
            new Vector2(0f, 34f),
            new Vector2(350f, 56f),
            0,
            true,
            ResumeGame,
            out pauseButtons[0],
            out pauseButtonBgImages[0],
            out pauseButtonHighlights[0],
            out pauseButtonLabels[0]
        );

        CreateOrUpdateModernButton(
            windowObj.transform,
            "Btn_Restart",
            "RESTART MISSION",
            uiFont,
            new Vector2(0f, -36f),
            new Vector2(350f, 56f),
            1,
            true,
            RestartMission,
            out pauseButtons[1],
            out pauseButtonBgImages[1],
            out pauseButtonHighlights[1],
            out pauseButtonLabels[1]
        );

        CreateOrUpdateModernButton(
            windowObj.transform,
            "Btn_Quit",
            "EXIT MISSION",
            uiFont,
            new Vector2(0f, -106f),
            new Vector2(350f, 56f),
            2,
            true,
            QuitMission,
            out pauseButtons[2],
            out pauseButtonBgImages[2],
            out pauseButtonHighlights[2],
            out pauseButtonLabels[2]
        );

        pauseFooterHintText = GetOrCreateText(
            windowObj.transform,
            "Pause_FooterHint",
            uiFont,
            "[D-Pad / Stick] Navigate   |   [A] Confirm   |   [START / B] Resume",
            13,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            new Color(0.75f, 0.82f, 0.90f, 0.9f),
            new Vector2(0f, -186f),
            new Vector2(560f, 26f)
        );

        if (!IsPaused)
        {
            pauseMenuPanel.SetActive(false);
        }
    }

    // =========================================================================
    // 3D MODERN MENU UI — GAME OVER MENU
    // =========================================================================

    private void EnsureGameOverUI(Transform canvasRoot, Font uiFont)
    {
        Transform goTrans = canvasRoot.Find("GameOver_ModalPanel");
        if (goTrans != null)
        {
            gameOverPanel = goTrans.gameObject;
            // Clean up legacy non-ModernMenu direct children if upgrading
            Transform legacyHeader = gameOverPanel.transform.Find("GameOver_Header");
            if (legacyHeader != null)
            {
                for (int i = gameOverPanel.transform.childCount - 1; i >= 0; i--)
                {
                    DestroyImmediate(gameOverPanel.transform.GetChild(i).gameObject);
                }
            }
        }
        else
        {
            gameOverPanel = new GameObject("GameOver_ModalPanel", typeof(RectTransform), typeof(Image));
            gameOverPanel.transform.SetParent(canvasRoot, false);
        }

        RectTransform goRect = gameOverPanel.GetComponent<RectTransform>();
        goRect.anchorMin = Vector2.zero;
        goRect.anchorMax = Vector2.one;
        goRect.offsetMin = Vector2.zero;
        goRect.offsetMax = Vector2.zero;

        Image goBg = gameOverPanel.GetComponent<Image>();
        goBg.sprite = cachedSolidSprite;
        goBg.color = new Color(0.04f, 0.015f, 0.02f, 0.86f);

        // Center 3D Modern Menu UI Window Frame
        GameObject windowObj = GetOrCreateWindowFrame(
            gameOverPanel.transform,
            "GameOver_ModernWindow",
            new Vector2(660f, 520f),
            new Color(0.11f, 0.07f, 0.08f, 0.96f),
            ModernGoldAccent
        );

        GetOrCreateText(
            windowObj.transform,
            "GameOver_Header",
            uiFont,
            "MISSION FAILED",
            36,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            ModernCrimsonAccent,
            new Vector2(0f, 192f),
            new Vector2(580f, 48f)
        );

        gameOverReasonText = GetOrCreateText(
            windowObj.transform,
            "GameOver_Reason",
            uiFont,
            "SPACE STATION DESTROYED BY ENEMY FLEET",
            16,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            ModernGoldAccent,
            new Vector2(0f, 150f),
            new Vector2(580f, 28f)
        );

        CreateOrUpdateDivider(windowObj.transform, "GameOver_Divider", new Vector2(0f, 124f), new Vector2(220f, 6f), ModernGoldAccent);

        // Framed Telemetry Stats Card using 3D Modern Menu UI Panel Frame 512px
        Transform statsBoxTrans = windowObj.transform.Find("GameOver_StatsBox");
        GameObject statsBoxObj = statsBoxTrans != null ? statsBoxTrans.gameObject : new GameObject("GameOver_StatsBox", typeof(RectTransform), typeof(Image));
        if (statsBoxTrans == null) statsBoxObj.transform.SetParent(windowObj.transform, false);

        RectTransform statsBoxRect = statsBoxObj.GetComponent<RectTransform>();
        statsBoxRect.anchorMin = new Vector2(0.5f, 0.5f);
        statsBoxRect.anchorMax = new Vector2(0.5f, 0.5f);
        statsBoxRect.pivot = new Vector2(0.5f, 0.5f);
        statsBoxRect.anchoredPosition = new Vector2(0f, 48f);
        statsBoxRect.sizeDelta = new Vector2(460f, 108f);

        Image statsBoxImg = statsBoxObj.GetComponent<Image>();
        statsBoxImg.sprite = modernPanelFrameSprite != null ? modernPanelFrameSprite : cachedSolidSprite;
        statsBoxImg.color = new Color(0.05f, 0.07f, 0.11f, 0.88f);
        statsBoxImg.raycastTarget = false;

        gameOverStatsText = GetOrCreateText(
            statsBoxObj.transform,
            "GameOver_Stats",
            uiFont,
            "WAVES SURVIVED :  0\nENEMIES DESTROYED :  0\nSURVIVAL TIME :  00:00",
            17,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            Color.white,
            Vector2.zero,
            new Vector2(430f, 94f)
        );

        // 2 Interactive 3D Modern Menu UI Buttons
        CreateOrUpdateModernButton(
            windowObj.transform,
            "Btn_GameOverRestart",
            "RESTART MISSION",
            uiFont,
            new Vector2(0f, -64f),
            new Vector2(360f, 56f),
            0,
            false,
            RestartMission,
            out gameOverButtons[0],
            out gameOverButtonBgImages[0],
            out gameOverButtonHighlights[0],
            out gameOverButtonLabels[0]
        );

        CreateOrUpdateModernButton(
            windowObj.transform,
            "Btn_GameOverQuit",
            "EXIT MISSION",
            uiFont,
            new Vector2(0f, -134f),
            new Vector2(360f, 56f),
            1,
            false,
            QuitMission,
            out gameOverButtons[1],
            out gameOverButtonBgImages[1],
            out gameOverButtonHighlights[1],
            out gameOverButtonLabels[1]
        );

        gameOverFooterHintText = GetOrCreateText(
            windowObj.transform,
            "GameOver_FooterHint",
            uiFont,
            "[D-Pad / Stick] Navigate   |   [A] Confirm",
            13,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            new Color(0.78f, 0.82f, 0.88f, 0.9f),
            new Vector2(0f, -212f),
            new Vector2(560f, 26f)
        );

        if (!IsGameOver)
        {
            gameOverPanel.SetActive(false);
        }
    }

    // =========================================================================
    // 3D MODERN MENU UI HELPER BUILDERS
    // =========================================================================

    private GameObject GetOrCreateWindowFrame(
        Transform parent,
        string name,
        Vector2 size,
        Color backdropTint,
        Color accentColor)
    {
        Transform existing = parent.Find(name);
        GameObject winObj = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
        if (existing == null) winObj.transform.SetParent(parent, false);

        RectTransform winRect = winObj.GetComponent<RectTransform>();
        winRect.anchorMin = new Vector2(0.5f, 0.5f);
        winRect.anchorMax = new Vector2(0.5f, 0.5f);
        winRect.pivot = new Vector2(0.5f, 0.5f);
        winRect.anchoredPosition = Vector2.zero;
        winRect.sizeDelta = size;

        Image winBg = winObj.GetComponent<Image>();
        winBg.sprite = modernPanelBackdropSprite != null ? modernPanelBackdropSprite : cachedSolidSprite;
        winBg.color = backdropTint;

        // Inner Frame Overlay (Panel Frame 512px)
        Transform innerTrans = winObj.transform.Find("InnerFrame");
        GameObject innerObj = innerTrans != null ? innerTrans.gameObject : new GameObject("InnerFrame", typeof(RectTransform), typeof(Image));
        if (innerTrans == null) innerObj.transform.SetParent(winObj.transform, false);

        RectTransform innerRect = innerObj.GetComponent<RectTransform>();
        innerRect.anchorMin = Vector2.zero;
        innerRect.anchorMax = Vector2.one;
        innerRect.offsetMin = new Vector2(10f, 10f);
        innerRect.offsetMax = new Vector2(-10f, -10f);

        Image innerImg = innerObj.GetComponent<Image>();
        innerImg.sprite = modernPanelFrameSprite != null ? modernPanelFrameSprite : cachedSolidSprite;
        innerImg.color = new Color(0.14f, 0.16f, 0.22f, 0.65f);
        innerImg.raycastTarget = false;

        // Top Header Highlight Strip (LoadingHighlight 512px)
        Transform glowTrans = winObj.transform.Find("HeaderGlow");
        GameObject glowObj = glowTrans != null ? glowTrans.gameObject : new GameObject("HeaderGlow", typeof(RectTransform), typeof(Image));
        if (glowTrans == null) glowObj.transform.SetParent(winObj.transform, false);

        RectTransform glowRect = glowObj.GetComponent<RectTransform>();
        glowRect.anchorMin = new Vector2(0.5f, 1f);
        glowRect.anchorMax = new Vector2(0.5f, 1f);
        glowRect.pivot = new Vector2(0.5f, 1f);
        glowRect.anchoredPosition = new Vector2(0f, -12f);
        glowRect.sizeDelta = new Vector2(size.x - 28f, 64f);

        Image glowImg = glowObj.GetComponent<Image>();
        glowImg.sprite = modernLoadingHighlightSprite != null ? modernLoadingHighlightSprite : cachedSolidSprite;
        glowImg.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.22f);
        glowImg.raycastTarget = false;

        // 4 Corner Brackets (Corner Detail 256px)
        float halfW = size.x * 0.5f - 24f;
        float halfH = size.y * 0.5f - 24f;
        CreateOrUpdateCornerDetail(winObj.transform, "Corner_BL", new Vector2(-halfW, -halfH), 42f, 0f, accentColor);
        CreateOrUpdateCornerDetail(winObj.transform, "Corner_TL", new Vector2(-halfW, halfH), 42f, -90f, accentColor);
        CreateOrUpdateCornerDetail(winObj.transform, "Corner_TR", new Vector2(halfW, halfH), 42f, 180f, accentColor);
        CreateOrUpdateCornerDetail(winObj.transform, "Corner_BR", new Vector2(halfW, -halfH), 42f, 90f, accentColor);

        return winObj;
    }

    private void CreateOrUpdateCornerDetail(
        Transform parent,
        string name,
        Vector2 anchoredPos,
        float size,
        float rotZ,
        Color color)
    {
        Transform existing = parent.Find(name);
        GameObject obj = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
        if (existing == null) obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(size, size);
        rect.localRotation = Quaternion.Euler(0f, 0f, rotZ);

        Image img = obj.GetComponent<Image>();
        img.sprite = modernCornerDetailSprite != null ? modernCornerDetailSprite : cachedSolidSprite;
        img.color = color;
        img.raycastTarget = false;
    }

    private void CreateOrUpdateDivider(
        Transform parent,
        string name,
        Vector2 anchoredPos,
        Vector2 size,
        Color color)
    {
        Transform existing = parent.Find(name);
        GameObject obj = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
        if (existing == null) obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        Image img = obj.GetComponent<Image>();
        img.sprite = modernDetailDividerSprite != null ? modernDetailDividerSprite : cachedSolidSprite;
        img.color = color;
        img.raycastTarget = false;
    }

    private void CreateOrUpdateModernButton(
        Transform parent,
        string name,
        string label,
        Font uiFont,
        Vector2 anchoredPos,
        Vector2 size,
        int buttonIndex,
        bool isPauseMenuButton,
        UnityEngine.Events.UnityAction onClickAction,
        out Button btnComp,
        out Image bgImg,
        out GameObject highlightObj,
        out Text labelText)
    {
        Transform existing = parent.Find(name);
        GameObject btnObj = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        if (existing == null) btnObj.transform.SetParent(parent, false);

        RectTransform btnRect = btnObj.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.5f, 0.5f);
        btnRect.anchorMax = new Vector2(0.5f, 0.5f);
        btnRect.pivot = new Vector2(0.5f, 0.5f);
        btnRect.anchoredPosition = anchoredPos;
        btnRect.sizeDelta = size;

        bgImg = btnObj.GetComponent<Image>();
        bgImg.sprite = modernButtonNormalSprite != null ? modernButtonNormalSprite : cachedSolidSprite;
        bgImg.color = ModernGoldAccent;

        // Highlight Fill Overlay (Button Highlighted 256px)
        Transform hlTrans = btnObj.transform.Find("SelectedHighlight");
        highlightObj = hlTrans != null ? hlTrans.gameObject : new GameObject("SelectedHighlight", typeof(RectTransform), typeof(Image));
        if (hlTrans == null) highlightObj.transform.SetParent(btnObj.transform, false);

        RectTransform hlRect = highlightObj.GetComponent<RectTransform>();
        hlRect.anchorMin = Vector2.zero;
        hlRect.anchorMax = Vector2.one;
        hlRect.offsetMin = new Vector2(3f, 3f);
        hlRect.offsetMax = new Vector2(-3f, -3f);

        Image hlImg = highlightObj.GetComponent<Image>();
        hlImg.sprite = modernButtonHighlightSprite != null ? modernButtonHighlightSprite : cachedSolidSprite;
        hlImg.color = new Color(1.0f, 0.686f, 0.0f, 0.32f);
        hlImg.raycastTarget = false;

        btnComp = btnObj.GetComponent<Button>();
        btnComp.targetGraphic = bgImg;
        if (modernButtonHoverSprite != null && modernButtonPressSprite != null)
        {
            btnComp.transition = Selectable.Transition.SpriteSwap;
            SpriteState st = new SpriteState
            {
                highlightedSprite = modernButtonHoverSprite,
                selectedSprite = modernButtonHoverSprite,
                pressedSprite = modernButtonPressSprite
            };
            btnComp.spriteState = st;
        }

        btnComp.onClick.RemoveAllListeners();
        btnComp.onClick.AddListener(onClickAction);

        // Pointer Enter Hover Trigger so mouse hover selects this button and plays 3D Modern Menu UI hover sound
        EventTrigger trigger = btnObj.GetComponent<EventTrigger>();
        if (trigger == null) trigger = btnObj.AddComponent<EventTrigger>();
        trigger.triggers.Clear();

        EventTrigger.Entry enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enterEntry.callback.AddListener((_) =>
        {
            if (isPauseMenuButton)
            {
                if (selectedPauseButtonIndex != buttonIndex)
                {
                    selectedPauseButtonIndex = buttonIndex;
                    PlayUISound(modernHoverSfx, 0.65f);
                }
            }
            else
            {
                if (selectedGameOverButtonIndex != buttonIndex)
                {
                    selectedGameOverButtonIndex = buttonIndex;
                    PlayUISound(modernHoverSfx, 0.65f);
                }
            }
        });
        trigger.triggers.Add(enterEntry);

        labelText = GetOrCreateText(
            btnObj.transform,
            "Label",
            uiFont,
            label,
            16,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            Color.white,
            Vector2.zero,
            size - new Vector2(24f, 10f)
        );
    }

    private Text GetOrCreateText(
        Transform parent,
        string name,
        Font font,
        string defaultText,
        int fontSize,
        FontStyle style,
        TextAnchor alignment,
        Color color,
        Vector2 anchoredPos,
        Vector2 size)
    {
        Transform existing = parent.Find(name);
        GameObject txtObj = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Text));
        if (existing == null) txtObj.transform.SetParent(parent, false);

        RectTransform rect = txtObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        Text txt = txtObj.GetComponent<Text>();
        if (font != null) txt.font = font;
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.alignment = alignment;
        txt.color = color;
        if (existing == null) txt.text = defaultText;
        txt.raycastTarget = false;

        return txt;
    }

    // =========================================================================
    // LIVE HUD & MENU VISUAL UPDATES
    // =========================================================================

    private void UpdateMenuVisuals()
    {
        bool isGamepad = (SpaceshipController.Instance != null) ? SpaceshipController.Instance.isUsingGamepad : (Gamepad.current != null);

        if (IsPaused && pauseMenuPanel != null)
        {
            int mins = Mathf.FloorToInt(survivalTime / 60f);
            int secs = Mathf.FloorToInt(survivalTime % 60f);
            int shieldPct = Mathf.CeilToInt(Mathf.Clamp01(stationCurrentHealth / Mathf.Max(1f, stationMaxHealth)) * 100f);

            if (pauseStatusText != null)
            {
                string waveLabel = currentWave == 0 ? "PRE-WAVE 1" : $"WAVE {currentWave}";
                pauseStatusText.text = $"{waveLabel}   |   STATION SHIELD: {shieldPct}%   |   TIME: {mins:00}:{secs:00}";
            }

            if (pauseFooterHintText != null)
            {
                pauseFooterHintText.text = isGamepad
                    ? "[D-Pad / Stick] Navigate   |   [A] Confirm   |   [START / B] Resume"
                    : "[W/S or Mouse] Navigate   |   [ENTER / Click] Confirm   |   [ESC] Resume";
            }

            for (int i = 0; i < pauseButtons.Length; i++)
            {
                bool selected = (i == selectedPauseButtonIndex);
                if (pauseButtonHighlights[i] != null && pauseButtonHighlights[i].activeSelf != selected)
                {
                    pauseButtonHighlights[i].SetActive(selected);
                }
                if (pauseButtonBgImages[i] != null)
                {
                    pauseButtonBgImages[i].sprite = selected && modernButtonHoverSprite != null
                        ? modernButtonHoverSprite
                        : (modernButtonNormalSprite != null ? modernButtonNormalSprite : cachedSolidSprite);
                    pauseButtonBgImages[i].color = selected ? Color.white : ModernGoldAccent;
                }
                if (pauseButtonLabels[i] != null)
                {
                    pauseButtonLabels[i].color = selected ? ModernGoldAccent : Color.white;
                }
            }
        }

        if (IsGameOver && gameOverPanel != null)
        {
            if (gameOverReasonText != null)
            {
                gameOverReasonText.text = gameOverReason;
            }

            if (gameOverStatsText != null)
            {
                int mins = Mathf.FloorToInt(survivalTime / 60f);
                int secs = Mathf.FloorToInt(survivalTime % 60f);
                gameOverStatsText.text =
                    $"WAVES SURVIVED :  {Mathf.Max(0, currentWave - 1)}\n" +
                    $"ENEMIES DESTROYED :  {enemiesDestroyed}\n" +
                    $"SURVIVAL TIME :  {mins:00}:{secs:00}";
            }

            if (gameOverFooterHintText != null)
            {
                gameOverFooterHintText.text = isGamepad
                    ? "[D-Pad / Stick] Navigate   |   [A] Confirm"
                    : "[W/S or Mouse] Navigate   |   [ENTER / Click] Confirm";
            }

            for (int i = 0; i < gameOverButtons.Length; i++)
            {
                bool selected = (i == selectedGameOverButtonIndex);
                if (gameOverButtonHighlights[i] != null && gameOverButtonHighlights[i].activeSelf != selected)
                {
                    gameOverButtonHighlights[i].SetActive(selected);
                }
                if (gameOverButtonBgImages[i] != null)
                {
                    gameOverButtonBgImages[i].sprite = selected && modernButtonHoverSprite != null
                        ? modernButtonHoverSprite
                        : (modernButtonNormalSprite != null ? modernButtonNormalSprite : cachedSolidSprite);
                    gameOverButtonBgImages[i].color = selected ? Color.white : ModernGoldAccent;
                }
                if (gameOverButtonLabels[i] != null)
                {
                    gameOverButtonLabels[i].color = selected ? ModernGoldAccent : Color.white;
                }
            }
        }
    }

    private void UpdateHUDElements()
    {
        if (hudCanvas == null) EnsureHUDElements();

        // 1. Update Top-Left Circular Station Health Bar
        float stationNorm = Mathf.Clamp01(stationCurrentHealth / Mathf.Max(1f, stationMaxHealth));
        if (stationRadialFillImage != null)
        {
            stationRadialFillImage.fillAmount = stationNorm;

            bool underAttack = (Time.time - lastStationHitTime < 1.2f);
            if (underAttack)
            {
                float flash = 0.5f + 0.5f * Mathf.Sin(Time.time * 24f);
                stationRadialFillImage.color = Color.Lerp(new Color(1.0f, 0.15f, 0.20f, 1f), new Color(1.0f, 0.75f, 0.2f, 1f), flash);
            }
            else if (stationNorm > 0.5f)
            {
                stationRadialFillImage.color = Color.Lerp(new Color(0.95f, 0.75f, 0.15f, 1f), new Color(0.15f, 0.92f, 0.85f, 1f), (stationNorm - 0.5f) * 2f);
            }
            else
            {
                stationRadialFillImage.color = Color.Lerp(new Color(1.0f, 0.12f, 0.18f, 1f), new Color(0.95f, 0.75f, 0.15f, 1f), stationNorm * 2f);
            }

            if (stationPercentText != null)
            {
                int pct = Mathf.CeilToInt(stationNorm * 100f);
                stationPercentText.text = $"{pct}%";
                stationPercentText.color = underAttack ? new Color(1.0f, 0.35f, 0.35f, 1f) : Color.white;
            }

            if (stationStatusText != null)
            {
                stationStatusText.text = underAttack ? "ALERT!" : "STATION";
                stationStatusText.color = underAttack ? new Color(1.0f, 0.45f, 0.45f, 1f) : new Color(0.75f, 0.95f, 1.0f, 1f);
            }
        }

        // Determine if Gamepad or Keyboard is being used
        bool isGamepad = (SpaceshipController.Instance != null) ? SpaceshipController.Instance.isUsingGamepad : (Gamepad.current != null);

        // 2. Update Top-Center Locked Enemy Health Bar & Screen-Space Reticle
        EnemySpaceship lockedEnemy = (SpaceshipController.Instance != null) ? SpaceshipController.Instance.LockedEnemy : null;
        bool hasValidLock = (lockedEnemy != null && lockedEnemy.currentHealth > 0f && !IsGameOver && !IsPaused);

        if (lockedEnemyTopPanel != null)
        {
            if (lockedEnemyTopPanel.activeSelf != hasValidLock)
            {
                lockedEnemyTopPanel.SetActive(hasValidLock);
            }

            if (hasValidLock)
            {
                lockedEnemyHealthSlider.maxValue = Mathf.Max(1f, lockedEnemy.maxHealth);
                lockedEnemyHealthSlider.value = lockedEnemy.currentHealth;

                float dist = Vector3.Distance(
                    SpaceshipController.Instance.transform.position,
                    lockedEnemy.transform.position
                );
                if (lockedEnemyTitleText != null)
                {
                    int hpPct = Mathf.CeilToInt((lockedEnemy.currentHealth / Mathf.Max(1f, lockedEnemy.maxHealth)) * 100f);
                    string cycleHint = isGamepad ? "[LB] Cycle" : "[TAB] Cycle";
                    lockedEnemyTitleText.text = $"[ TARGET LOCKED ]  {lockedEnemy.enemyCallsign}  —  {hpPct}%  ({Mathf.RoundToInt(dist)}m)  |  {cycleHint}";
                }
            }
        }

        // Update 3D -> 2D Screen-Space Target Lock Reticle Bracket
        if (targetLockReticleRect != null)
        {
            Camera mainCam = Camera.main;
            if (hasValidLock && mainCam != null)
            {
                Vector3 screenPos = mainCam.WorldToScreenPoint(lockedEnemy.transform.position);
                if (screenPos.z > 0f)
                {
                    if (!targetLockReticleRect.gameObject.activeSelf)
                        targetLockReticleRect.gameObject.SetActive(true);

                    targetLockReticleRect.position = new Vector3(screenPos.x, screenPos.y, 0f);
                    float pulse = 1f + Mathf.Sin(Time.time * 12f) * 0.08f;
                    targetLockReticleRect.localScale = new Vector3(pulse, pulse, 1f);

                    if (targetLockReticleDistText != null)
                    {
                        float dist = Vector3.Distance(mainCam.transform.position, lockedEnemy.transform.position);
                        targetLockReticleDistText.text = $"LOCK [{Mathf.RoundToInt(dist)}m]";
                    }
                }
                else
                {
                    if (targetLockReticleRect.gameObject.activeSelf)
                        targetLockReticleRect.gameObject.SetActive(false);
                }
            }
            else
            {
                if (targetLockReticleRect.gameObject.activeSelf)
                    targetLockReticleRect.gameObject.SetActive(false);
            }
        }

        // 3. Update Wave Countdown Timer Banner (Before Wave 1, Between Waves, & Wave Arrival Alert)
        if (waveBannerPanel != null)
        {
            RectTransform bannerRect = waveBannerPanel.GetComponent<RectTransform>();
            if (bannerRect != null)
            {
                bannerRect.anchoredPosition = hasValidLock ? new Vector2(0f, -90f) : new Vector2(0f, -26f);
            }

            if (waitingForNextWave)
            {
                if (!waveBannerPanel.activeSelf)
                {
                    waveBannerPanel.SetActive(true);
                }

                float clampedTimer = Mathf.Max(0f, nextWaveCountdown);
                int wholeSecs = Mathf.FloorToInt(clampedTimer);
                int tenths = Mathf.FloorToInt((clampedTimer - wholeSecs) * 10f);

                if (waveBannerTitleText != null)
                {
                    if (currentWave == 0)
                    {
                        waveBannerTitleText.text = "PREPARE FOR DEFENSIVE COMBAT — WAVE 1";
                        waveBannerTitleText.color = ModernGoldAccent;
                    }
                    else
                    {
                        waveBannerTitleText.text = $"WAVE {currentWave} CLEARED! (+15% SHIELDS RECHARGED)";
                        waveBannerTitleText.color = ModernCyanAccent;
                    }
                }

                if (waveBannerTimerText != null)
                {
                    waveBannerTimerText.text = $"WAVE {currentWave + 1} INCOMING IN   00:{wholeSecs:00}.{tenths}";
                    waveBannerTimerText.color = clampedTimer <= 3.0f
                        ? Color.Lerp(Color.white, ModernCrimsonAccent, 0.5f + 0.5f * Mathf.Sin(Time.time * 18f))
                        : Color.white;
                }

                if (waveBannerProgressFill != null)
                {
                    waveBannerProgressFill.fillAmount = Mathf.Clamp01(clampedTimer / Mathf.Max(0.1f, currentWaveTotalCountdown));
                    waveBannerProgressFill.color = clampedTimer <= 3.0f ? ModernCrimsonAccent : ModernGoldAccent;
                }
            }
            else if (Time.time < waveBannerHideTime)
            {
                if (!waveBannerPanel.activeSelf)
                {
                    waveBannerPanel.SetActive(true);
                }

                if (waveBannerTitleText != null)
                {
                    waveBannerTitleText.text = activeWaveAlertHeader;
                    waveBannerTitleText.color = ModernCrimsonAccent;
                }

                if (waveBannerTimerText != null)
                {
                    waveBannerTimerText.text = activeWaveAlertSub;
                    waveBannerTimerText.color = Color.white;
                }

                if (waveBannerProgressFill != null)
                {
                    waveBannerProgressFill.fillAmount = 1f;
                    waveBannerProgressFill.color = ModernCrimsonAccent;
                }
            }
            else
            {
                if (waveBannerPanel.activeSelf)
                {
                    waveBannerPanel.SetActive(false);
                }
            }
        }
    }

    private static void EnsureProceduralSprites()
    {
        if (cachedRingSprite == null)
        {
            int size = 128;
            Texture2D ringTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            ringTex.wrapMode = TextureWrapMode.Clamp;
            float center = (size - 1) * 0.5f;
            float outerRadius = size * 0.46f;
            float innerRadius = size * 0.33f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // Smooth anti-aliased ring edges
                    float outerAlpha = Mathf.Clamp01((outerRadius - dist) / 1.5f);
                    float innerAlpha = Mathf.Clamp01((dist - innerRadius) / 1.5f);
                    float alpha = outerAlpha * innerAlpha;

                    ringTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            ringTex.Apply();
            cachedRingSprite = Sprite.Create(ringTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        if (cachedSolidSprite == null)
        {
            Texture2D solidTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    solidTex.SetPixel(x, y, Color.white);
            solidTex.Apply();
            cachedSolidSprite = Sprite.Create(solidTex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        }

        if (cachedReticleBracketSprite == null)
        {
            int size = 64;
            Texture2D retTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            retTex.wrapMode = TextureWrapMode.Clamp;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool borderX = (x >= 2 && x <= 5) || (x >= size - 6 && x <= size - 3);
                    bool borderY = (y >= 2 && y <= 5) || (y >= size - 6 && y <= size - 3);
                    bool cornerX = (x <= 16) || (x >= size - 17);
                    bool cornerY = (y <= 16) || (y >= size - 17);

                    bool isBracket = (borderX && cornerY) || (borderY && cornerX);
                    retTex.SetPixel(x, y, isBracket ? Color.white : Color.clear);
                }
            }
            retTex.Apply();
            cachedReticleBracketSprite = Sprite.Create(retTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>
    /// Ensures a GameManager GameObject with the GameManager script is attached in the active GamePlay scene.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void EnsureGameManagerInScene()
    {
        var activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.name != "GamePlay" && !string.IsNullOrEmpty(activeScene.name))
        {
            return;
        }

        GameManager existing = FindFirstObjectByType<GameManager>();
        if (existing == null)
        {
            GameObject gmObj = GameObject.Find("GameManager");
            if (gmObj == null)
            {
                gmObj = new GameObject("GameManager");
            }
            existing = gmObj.GetComponent<GameManager>();
            if (existing == null)
            {
                existing = gmObj.AddComponent<GameManager>();
            }
#if UNITY_EDITOR
            if (!Application.isPlaying && activeScene.IsValid())
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(activeScene);
            }
#endif
        }

        existing.EnsureEnemyPrefabLoaded();
        existing.EnsureModernMenuAssetsLoaded();
        existing.EnsureHUDElements();
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void EditorAutoSetupGameManager()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating) return;
            EnsureGameManagerInScene();
        };
    }
#endif
}
