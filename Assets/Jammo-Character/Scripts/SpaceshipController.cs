using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Spaceship Flight Controller, Weapon System (RB), Dynamic Lighting Rig & Health Bar HUD.
/// Features:
/// - Press D-Pad Up (Controller) or T Key (Keyboard) to enter/exit Spaceship Mode.
/// - RB (Right Bumper / Left Mouse Click / F): Fire twin plasma lasers.
/// - RT (Right Trigger / Left Shift): Progressive throttle & afterburner boost.
/// - LT (Left Trigger / Space / Ctrl): Airbrake / inertial dampener & high-G drift turn.
/// - Left Stick / WASD: Pitch, yaw, and aerodynamic banking.
/// - Right Stick / Mouse: 360-degree orbit camera.
/// - Automatic lighting rig (key light, rim light, headlights, dynamic thruster glow).
/// - Bottom UI health sliders:
///   * Character Health (Red) - always visible.
///   * Spaceship Health (Blue) - visible only in spaceship mode.
/// </summary>
[ExecuteAlways]
public class SpaceshipController : MonoBehaviour
{
    public static SpaceshipController Instance { get; private set; }

    [Header("Mode Switching")]
    [Tooltip("Whether the player is currently piloting the spaceship.")]
    public bool isPiloting = false;
    [Tooltip("Reference to player character (auto-assigned if null).")]
    public MovementInput characterPlayer;
    [Tooltip("Reference to third person camera (auto-assigned if null).")]
    public ThirdPersonCamera thirdPersonCamera;
    [Tooltip("Show on-screen flight HUD.")]
    public bool showFlightHUD = true;

    [Header("Weapon & Target Lock-On System (RB = Fire, LB / Tab = Lock Enemy)")]
    [Tooltip("Rate of fire in shots per second when holding RB.")]
    public float fireRate = 8.5f;
    [Tooltip("Projectile muzzle velocity.")]
    public float laserBoltSpeed = 240f;
    [Tooltip("Laser projectile lifetime in seconds.")]
    public float laserBoltLifetime = 2.2f;
    [Tooltip("Damage dealt per player laser bolt.")]
    public float laserBoltDamage = 22f;
    [Tooltip("Currently locked enemy spaceship (displays health bar at top of camera).")]
    public EnemySpaceship LockedEnemy { get; private set; }
    [Tooltip("Whether the player is currently using an Xbox / Gamepad controller.")]
    public bool isUsingGamepad = true;

    [Header("Health System & Bottom Slider UI")]
    [Tooltip("Maximum character health (Red).")]
    public float characterMaxHealth = 100f;
    [Tooltip("Current character health (Red).")]
    public float characterCurrentHealth = 100f;
    [Tooltip("Maximum spaceship health (Blue).")]
    public float shipMaxHealth = 100f;
    [Tooltip("Current spaceship health (Blue).")]
    public float shipCurrentHealth = 100f;
    [Tooltip("Character health slider (Red) at the bottom.")]
    public Slider characterHealthSlider;
    [Tooltip("Spaceship health slider (Blue) at the bottom - active only in ship mode.")]
    public Slider shipHealthSlider;

    [Header("Flight Propulsion (RT = Throttle, LT = Brake)")]
    [Tooltip("Standard cruise top speed.")]
    public float maxCruiseSpeed = 42f;
    [Tooltip("Maximum boost speed when holding RT.")]
    public float maxBoostSpeed = 72f;
    [Tooltip("Maximum reverse speed when holding LT while stopped.")]
    public float maxReverseSpeed = 12f;
    [Tooltip("Forward engine acceleration.")]
    public float forwardAcceleration = 32f;
    [Tooltip("Airbrake deceleration force.")]
    public float brakeDeceleration = 48f;
    [Tooltip("Passive drag in space when releasing throttle.")]
    public float passiveSpaceDrag = 4.5f;

    [Header("Flight Maneuvering & Banking")]
    [Tooltip("Pitch speed in degrees per second.")]
    public float pitchSpeed = 78f;
    [Tooltip("Yaw speed in degrees per second.")]
    public float yawSpeed = 85f;
    [Tooltip("Maximum wing bank/roll angle in sharp turns.")]
    public float maxBankAngle = 48f;
    [Tooltip("Bank/roll transition speed.")]
    public float bankSmoothSpeed = 6.5f;
    [Tooltip("Auto-leveling roll speed when flying straight.")]
    public float autoLevelRollSpeed = 4.0f;
    [Tooltip("Steering smoothing time.")]
    [Range(0.02f, 0.3f)]
    public float steeringSmoothTime = 0.09f;
    [Tooltip("Low speed strafe responsiveness.")]
    public float lowSpeedStrafeFactor = 8f;
    [Tooltip("Invert pitch input.")]
    public bool invertFlightPitch = false;
    [Tooltip("Align ship forward vector toward camera direction while boosting.")]
    public bool alignToCameraWhenThrusting = true;
    [Tooltip("Camera alignment strength.")]
    public float cameraAlignStrength = 1.6f;

    [Header("Ship Lighting Rig")]
    [Tooltip("Enable automatic ship lighting rig.")]
    public bool enableShipLighting = true;
    [Tooltip("Top key light intensity.")]
    public float hullKeyLightIntensity = 18f;
    [Tooltip("Bottom/rim fill light intensity.")]
    public float hullRimLightIntensity = 10f;
    [Tooltip("Base thruster engine glow intensity.")]
    public float thrusterBaseIntensity = 8f;
    [Tooltip("Full throttle boost thruster intensity.")]
    public float thrusterBoostIntensity = 35f;

    public float CurrentSpeed { get; private set; }
    public float NormalizedSpeed => Mathf.Clamp01(Mathf.Abs(CurrentSpeed) / Mathf.Max(1f, maxBoostSpeed));
    public float CurrentThrottleInput { get; private set; }
    public float CurrentBrakeInput { get; private set; }
    public Vector2 CurrentSteerInput { get; private set; }
    public float CurrentBankAngle { get; private set; }
    public Vector3 CurrentVelocity { get; private set; }

    private Vector2 smoothedSteer;
    private Vector2 steerVelocity;
    private float currentRoll;
    private float rollVelocity;
    private Quaternion baseOrientation;
    private Vector3 initialIdlePosition;
    private bool idleOriginRecorded;

    private float nextFireTime;
    private bool alternateWeaponSalvo;
    private Light muzzleFlashLight;
    private float muzzleFlashIntensity;

    private static readonly Vector3[] WingPlasmaMuzzleOffsets = new Vector3[]
    {
        new Vector3(-4.20f, -2.05f, 1.50f),
        new Vector3( 4.20f, -2.05f, 1.50f)
    };
    private static readonly Vector3[] BellyCannonMuzzleOffsets = new Vector3[]
    {
        new Vector3(-0.36f, -1.03f, 3.60f),
        new Vector3( 0.36f, -1.03f, 3.60f)
    };

    private Transform lightingRigRoot;
    private Light topKeyLight;
    private Light bottomRimLight;
    private Light frontRimLight;
    private Light thrusterLight;
    private Light leftHeadlight;
    private Light rightHeadlight;
    private Transform[] thrusterFlames;

    private Renderer[] characterRenderers;

    private void OnEnable()
    {
        Instance = this;
        EnsureShipLightingRig();
        EnsureBottomHealthBarsUI();
        SpaceshipNPC.EnsureAllAllyNPCsSetup();
        GameManager.EnsureGameManagerInScene();

        if (Application.isPlaying)
        {
            baseOrientation = transform.rotation;
            initialIdlePosition = transform.position;
            idleOriginRecorded = true;
        }
    }

    private void OnDisable()
    {
        StopRumble();
    }

    private void Start()
    {
        Instance = this;
        EnsureShipLightingRig();
        EnsureBottomHealthBarsUI();
        SpaceshipNPC.EnsureAllAllyNPCsSetup();
        GameManager.EnsureGameManagerInScene();

        if (!Application.isPlaying) return;

        baseOrientation = transform.rotation;
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude > 0.001f)
        {
            baseOrientation = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
            transform.rotation = baseOrientation;
        }

        if (characterPlayer == null)
        {
            characterPlayer = FindFirstObjectByType<MovementInput>();
        }

        if (thirdPersonCamera == null && Camera.main != null)
        {
            thirdPersonCamera = Camera.main.GetComponent<ThirdPersonCamera>();
        }

        if (!idleOriginRecorded)
        {
            initialIdlePosition = transform.position;
            idleOriginRecorded = true;
        }

        EnsureBottomHealthBarsUI();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            if (enableShipLighting && lightingRigRoot == null)
            {
                EnsureShipLightingRig();
            }
            EnsureBottomHealthBarsUI();
            return;
        }

        DetectActiveInputDevice();

        if (GameManager.Instance != null && (GameManager.Instance.IsGameOver || GameManager.Instance.IsPaused))
        {
            return;
        }

        HandleModeToggleInput();
        HandleTargetLockInput();

        if (isPiloting)
        {
            HandleWeaponShooting();
        }

        float dt = Time.deltaTime;
        if (isPiloting)
        {
            SimulateActiveFlight(dt);
        }
        else
        {
            SimulateStationaryHover(dt);
        }

        UpdateDynamicLightingAndThrusters(dt);
        UpdateBottomHealthBarsUI();
    }

    private void DetectActiveInputDevice()
    {
        if (Gamepad.current != null)
        {
            if (Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.04f ||
                Gamepad.current.rightStick.ReadValue().sqrMagnitude > 0.04f ||
                Gamepad.current.dpad.up.wasPressedThisFrame ||
                Gamepad.current.dpad.down.wasPressedThisFrame ||
                Gamepad.current.dpad.left.wasPressedThisFrame ||
                Gamepad.current.dpad.right.wasPressedThisFrame ||
                Gamepad.current.buttonSouth.wasPressedThisFrame ||
                Gamepad.current.buttonEast.wasPressedThisFrame ||
                Gamepad.current.buttonWest.wasPressedThisFrame ||
                Gamepad.current.buttonNorth.wasPressedThisFrame ||
                Gamepad.current.rightShoulder.wasPressedThisFrame ||
                Gamepad.current.leftShoulder.wasPressedThisFrame ||
                Gamepad.current.rightTrigger.ReadValue() > 0.1f ||
                Gamepad.current.leftTrigger.ReadValue() > 0.1f)
            {
                isUsingGamepad = true;
            }
        }

        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
        {
            isUsingGamepad = false;
        }

        if (Mouse.current != null && (
            Mouse.current.leftButton.wasPressedThisFrame ||
            Mouse.current.rightButton.wasPressedThisFrame ||
            Mouse.current.middleButton.wasPressedThisFrame ||
            Mouse.current.delta.ReadValue().sqrMagnitude > 25f))
        {
            isUsingGamepad = false;
        }
    }

    /// <summary>
    /// Handles locking onto enemy spaceships when pressing LB / Right Stick Click (Gamepad) or Tab / Middle Mouse (PC).
    /// When locked, the enemy's health bar appears at the top of the camera.
    /// </summary>
    private void HandleTargetLockInput()
    {
        if (LockedEnemy != null && LockedEnemy.currentHealth <= 0f)
        {
            LockedEnemy = null;
        }

        bool lockPressed = false;

        if (Gamepad.current != null)
        {
            if (Gamepad.current.leftShoulder.wasPressedThisFrame || Gamepad.current.rightStickButton.wasPressedThisFrame)
            {
                lockPressed = true;
            }
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.tabKey.wasPressedThisFrame || Keyboard.current.cKey.wasPressedThisFrame)
            {
                lockPressed = true;
            }
        }

        if (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame)
        {
            lockPressed = true;
        }

        if (lockPressed)
        {
            ToggleOrCycleTargetLock();
        }
    }

    public void ToggleOrCycleTargetLock()
    {
        if (EnemySpaceship.ActiveEnemies.Count == 0)
        {
            LockedEnemy = null;
            return;
        }

        Camera cam = Camera.main;
        Vector3 refPos = isPiloting ? transform.position : (cam != null ? cam.transform.position : transform.position);
        Vector3 refFwd = (cam != null) ? cam.transform.forward : transform.forward;

        // If no enemy is currently locked, pick the best enemy in front of the camera (or nearest overall)
        if (LockedEnemy == null)
        {
            EnemySpaceship bestFront = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < EnemySpaceship.ActiveEnemies.Count; i++)
            {
                EnemySpaceship candidate = EnemySpaceship.ActiveEnemies[i];
                if (candidate == null || candidate.currentHealth <= 0f) continue;

                Vector3 toEnemy = candidate.transform.position - refPos;
                float dist = toEnemy.magnitude;
                float angle = Vector3.Angle(refFwd, toEnemy);

                // Prioritize enemies near the center of the camera view
                float score = angle * 2.5f + dist * 0.25f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestFront = candidate;
                }
            }

            LockedEnemy = bestFront;
        }
        else
        {
            // Cycle to the next living enemy, or unlock if cycling wraps around/only one enemy
            int currentIndex = EnemySpaceship.ActiveEnemies.IndexOf(LockedEnemy);
            if (EnemySpaceship.ActiveEnemies.Count == 1)
            {
                LockedEnemy = null;
            }
            else
            {
                int nextIdx = (currentIndex + 1) % EnemySpaceship.ActiveEnemies.Count;
                LockedEnemy = EnemySpaceship.ActiveEnemies[nextIdx];
            }
        }
    }

    public void OnLockedEnemyDestroyed(EnemySpaceship destroyed)
    {
        if (LockedEnemy == destroyed)
        {
            LockedEnemy = EnemySpaceship.FindNearestEnemy(transform.position, out _);
        }
    }

    public void TakeShipDamage(float amount)
    {
        if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;

        shipCurrentHealth = Mathf.Max(0f, shipCurrentHealth - amount);
        UpdateBottomHealthBarsUI();

        if (shipCurrentHealth <= 0f)
        {
            SpaceshipExplosion.Spawn(transform.position, 1.45f);
            if (GameManager.Instance != null)
            {
                GameManager.Instance.TriggerGameOver("PLAYER SPACESHIP DESTROYED IN COMBAT");
            }
        }
    }

    public void TakeCharacterDamage(float amount)
    {
        if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;

        characterCurrentHealth = Mathf.Max(0f, characterCurrentHealth - amount);
        UpdateBottomHealthBarsUI();

        if (characterCurrentHealth <= 0f && GameManager.Instance != null)
        {
            GameManager.Instance.TriggerGameOver("PILOT KILLED IN ACTION");
        }
    }

    private void HandleWeaponShooting()
    {
        bool fireTriggered = false;

        if (Gamepad.current != null && Gamepad.current.rightShoulder.isPressed)
        {
            fireTriggered = true;
        }

        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            fireTriggered = true;
        }

        if (Keyboard.current != null && Keyboard.current.fKey.isPressed)
        {
            fireTriggered = true;
        }

        if (fireTriggered && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + (1f / Mathf.Max(1f, fireRate));
            FirePlasmaSalvo();
        }
    }

    private void FirePlasmaSalvo()
    {
        Vector3[] activeOffsets = alternateWeaponSalvo ? WingPlasmaMuzzleOffsets : BellyCannonMuzzleOffsets;
        alternateWeaponSalvo = !alternateWeaponSalvo;

        float boltSpeed = laserBoltSpeed + Mathf.Max(0f, CurrentSpeed);

        for (int i = 0; i < activeOffsets.Length; i++)
        {
            Vector3 muzzleWorldPos = transform.TransformPoint(activeOffsets[i]);
            Quaternion boltRotation = transform.rotation;

            // Target Lock-On Aim Assist: converge lasers toward the locked enemy if within forward firing arc
            if (LockedEnemy != null && LockedEnemy.currentHealth > 0f)
            {
                Vector3 toTarget = (LockedEnemy.transform.position - muzzleWorldPos).normalized;
                if (Vector3.Angle(transform.forward, toTarget) <= 28f)
                {
                    Vector3 assistedDir = Vector3.Slerp(transform.forward, toTarget, 0.85f).normalized;
                    boltRotation = Quaternion.LookRotation(assistedDir, Vector3.up);
                }
            }

            LaserBolt.SpawnBolt(
                muzzleWorldPos,
                boltRotation,
                boltSpeed,
                laserBoltLifetime,
                transform,
                LaserBolt.Faction.Friendly,
                laserBoltDamage,
                false
            );
        }

        muzzleFlashIntensity = 26f;
        if (muzzleFlashLight != null)
        {
            muzzleFlashLight.intensity = muzzleFlashIntensity;
        }
    }

    private void HandleModeToggleInput()
    {
        bool togglePressed = false;

        if (Gamepad.current != null && Gamepad.current.dpad.up.wasPressedThisFrame)
        {
            togglePressed = true;
        }

        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
        {
            togglePressed = true;
        }

        if (togglePressed)
        {
            SetPilotingMode(!isPiloting);
        }
    }

    public void SetPilotingMode(bool active)
    {
        isPiloting = active;

        if (characterPlayer == null)
        {
            characterPlayer = FindFirstObjectByType<MovementInput>();
        }

        if (thirdPersonCamera == null && Camera.main != null)
        {
            thirdPersonCamera = Camera.main.GetComponent<ThirdPersonCamera>();
        }

        if (characterPlayer != null)
        {
            if (characterRenderers == null || characterRenderers.Length == 0)
            {
                characterRenderers = characterPlayer.GetComponentsInChildren<Renderer>(true);
            }

            for (int i = 0; i < characterRenderers.Length; i++)
            {
                if (characterRenderers[i] != null)
                {
                    characterRenderers[i].enabled = !isPiloting;
                }
            }

            CharacterController charController = characterPlayer.GetComponent<CharacterController>();
            if (charController != null) charController.enabled = !isPiloting;
            characterPlayer.enabled = !isPiloting;

            if (isPiloting)
            {
                characterPlayer.transform.position = transform.position - transform.up * 1.5f;
            }
            else
            {
                Vector3 exitPos = transform.position - transform.right * 4.5f - transform.up * 0.8f;
                if (Physics.Raycast(exitPos + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore))
                {
                    exitPos = hit.point + Vector3.up * 0.05f;
                }
                characterPlayer.transform.position = exitPos;
                characterPlayer.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized, Vector3.up);
            }
        }

        if (thirdPersonCamera != null)
        {
            thirdPersonCamera.SetSpaceshipMode(isPiloting, this);
        }

        if (isPiloting)
        {
            baseOrientation = transform.rotation;
        }

        UpdateBottomHealthBarsUI();
    }

    private void SimulateActiveFlight(float dt)
    {
        PollFlightInputs(out float throttleInput, out float brakeInput, out Vector2 steerInput);
        CurrentThrottleInput = throttleInput;
        CurrentBrakeInput = brakeInput;
        CurrentSteerInput = steerInput;

        smoothedSteer = Vector2.SmoothDamp(smoothedSteer, steerInput, ref steerVelocity, steeringSmoothTime);

        float targetSpeed = 0f;
        if (throttleInput > 0.01f)
        {
            targetSpeed = Mathf.Lerp(maxCruiseSpeed, maxBoostSpeed, Mathf.InverseLerp(0.5f, 1.0f, throttleInput));
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetSpeed, forwardAcceleration * dt * Mathf.Max(0.5f, throttleInput));
        }
        else if (brakeInput > 0.01f)
        {
            if (CurrentSpeed > 0.5f)
            {
                CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, 0f, brakeDeceleration * dt * brakeInput);
            }
            else
            {
                CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, -maxReverseSpeed, forwardAcceleration * 0.5f * dt * brakeInput);
            }
        }
        else
        {
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, 0f, passiveSpaceDrag * dt);
        }

        float yawDelta = smoothedSteer.x * yawSpeed * dt;
        float pitchDelta = smoothedSteer.y * pitchSpeed * dt;

        float targetBank = -smoothedSteer.x * maxBankAngle;
        currentRoll = Mathf.SmoothDamp(currentRoll, targetBank, ref rollVelocity, 1f / bankSmoothSpeed);

        Quaternion yawRot = Quaternion.AngleAxis(yawDelta, Vector3.up);
        Quaternion pitchRot = Quaternion.AngleAxis(pitchDelta, Vector3.right);
        baseOrientation = yawRot * baseOrientation * pitchRot;

        if (alignToCameraWhenThrusting && thirdPersonCamera != null && throttleInput > 0.25f)
        {
            Vector3 camForward = thirdPersonCamera.transform.forward;
            Quaternion camHeading = Quaternion.LookRotation(camForward, Vector3.up);
            baseOrientation = Quaternion.Slerp(baseOrientation, camHeading, cameraAlignStrength * throttleInput * dt);
        }

        Quaternion bankRot = Quaternion.AngleAxis(currentRoll, Vector3.forward);
        transform.rotation = baseOrientation * bankRot;
        CurrentBankAngle = currentRoll;

        Vector3 moveDirection = transform.forward * CurrentSpeed;

        if (Mathf.Abs(CurrentSpeed) < maxCruiseSpeed * 0.4f)
        {
            Vector3 strafe = transform.right * (smoothedSteer.x * lowSpeedStrafeFactor);
            moveDirection += strafe;
        }

        transform.position += moveDirection * dt;
        CurrentVelocity = moveDirection;
    }

    private void SimulateStationaryHover(float dt)
    {
        CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, 0f, passiveSpaceDrag * 2f * dt);
        CurrentThrottleInput = 0f;
        CurrentBrakeInput = 0f;
        CurrentSteerInput = Vector2.zero;

        currentRoll = Mathf.MoveTowards(currentRoll, 0f, autoLevelRollSpeed * 8f * dt);
        transform.rotation = Quaternion.Slerp(transform.rotation, baseOrientation, autoLevelRollSpeed * dt);
        CurrentBankAngle = currentRoll;

        if (idleOriginRecorded)
        {
            float hoverY = Mathf.Sin(Time.time * 1.2f) * 0.15f;
            float hoverX = Mathf.Cos(Time.time * 0.8f) * 0.08f;
            transform.position = initialIdlePosition + new Vector3(hoverX, hoverY, 0f);
        }
        CurrentVelocity = Vector3.zero;
    }

    private void PollFlightInputs(out float throttle, out float brake, out Vector2 steer)
    {
        throttle = 0f;
        brake = 0f;
        steer = Vector2.zero;

        if (Gamepad.current != null)
        {
            throttle = Gamepad.current.rightTrigger.ReadValue();
            brake = Gamepad.current.leftTrigger.ReadValue();
            Vector2 stick = Gamepad.current.leftStick.ReadValue();
            steer.x = stick.x;
            steer.y = invertFlightPitch ? stick.y : -stick.y;
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.leftShiftKey.isPressed) throttle = 1.0f;
            else if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) throttle = Mathf.Max(throttle, 0.75f);

            if (Keyboard.current.spaceKey.isPressed || Keyboard.current.leftCtrlKey.isPressed) brake = 1.0f;
            else if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
            {
                if (CurrentSpeed > 1f) brake = Mathf.Max(brake, 0.85f);
                else throttle = -0.5f;
            }

            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) steer.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) steer.x += 1f;

            if (Keyboard.current.qKey.isPressed) steer.y += (invertFlightPitch ? -1f : 1f);
            if (Keyboard.current.eKey.isPressed) steer.y += (invertFlightPitch ? 1f : -1f);
        }

        steer.x = Mathf.Clamp(steer.x, -1f, 1f);
        steer.y = Mathf.Clamp(steer.y, -1f, 1f);
    }

    public void EnsureBottomHealthBarsUI()
    {
        GameObject existingCanvasObj = GameObject.Find("Gameplay_HealthHUD_Canvas");
        Canvas hudCanvas = null;
        if (existingCanvasObj != null)
        {
            hudCanvas = existingCanvasObj.GetComponent<Canvas>();
        }

        if (hudCanvas == null)
        {
            existingCanvasObj = new GameObject("Gameplay_HealthHUD_Canvas");
            hudCanvas = existingCanvasObj.AddComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hudCanvas.sortingOrder = 50;

            CanvasScaler scaler = existingCanvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            existingCanvasObj.AddComponent<GraphicRaycaster>();
        }

        Transform bottomPanel = existingCanvasObj.transform.Find("BottomHealthHUD_Panel");
        if (bottomPanel == null)
        {
            GameObject panelObj = new GameObject("BottomHealthHUD_Panel", typeof(RectTransform));
            panelObj.transform.SetParent(existingCanvasObj.transform, false);
            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, 26f);
            panelRect.sizeDelta = new Vector2(460f, 75f);
            bottomPanel = panelObj.transform;
        }

        // 1. Character Health Slider (Red - always visible)
        characterHealthSlider = GetOrCreateHealthSlider(
            bottomPanel,
            "CharacterHealth_Slider",
            "CHARACTER HP",
            new Vector2(0f, 0f),
            new Vector2(440f, 28f),
            new Color(0.90f, 0.16f, 0.20f, 1f),
            new Color(0.22f, 0.05f, 0.06f, 0.90f)
        );

        // 2. Spaceship Health Slider (Blue - visible only when piloting)
        shipHealthSlider = GetOrCreateHealthSlider(
            bottomPanel,
            "SpaceshipHealth_Slider",
            "SPACESHIP HP",
            new Vector2(0f, 36f),
            new Vector2(440f, 28f),
            new Color(0.12f, 0.58f, 1.00f, 1f),
            new Color(0.05f, 0.14f, 0.26f, 0.90f)
        );

        UpdateBottomHealthBarsUI();
    }

    private Slider GetOrCreateHealthSlider(Transform parent, string sliderName, string labelTitle, Vector2 anchoredPos, Vector2 size, Color fillColor, Color bgColor)
    {
        Transform existing = parent.Find(sliderName);
        GameObject sliderRoot;
        if (existing != null)
        {
            sliderRoot = existing.gameObject;
        }
        else
        {
            sliderRoot = new GameObject(sliderName, typeof(RectTransform));
            sliderRoot.transform.SetParent(parent, false);
        }

        RectTransform rootRect = sliderRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0f);
        rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.anchoredPosition = anchoredPos;
        rootRect.sizeDelta = size;

        // Background Frame
        Transform bgTrans = sliderRoot.transform.Find("Background");
        GameObject bgObj;
        if (bgTrans == null)
        {
            bgObj = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgObj.transform.SetParent(sliderRoot.transform, false);
        }
        else
        {
            bgObj = bgTrans.gameObject;
        }

        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        Image bgImg = bgObj.GetComponent<Image>();
        bgImg.color = bgColor;

        // Fill Area
        Transform fillAreaTrans = sliderRoot.transform.Find("Fill Area");
        GameObject fillAreaObj;
        if (fillAreaTrans == null)
        {
            fillAreaObj = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaObj.transform.SetParent(sliderRoot.transform, false);
        }
        else
        {
            fillAreaObj = fillAreaTrans.gameObject;
        }

        RectTransform fillAreaRect = fillAreaObj.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = new Vector2(3f, 3f);
        fillAreaRect.offsetMax = new Vector2(-3f, -3f);

        // Fill Image
        Transform fillTrans = fillAreaObj.transform.Find("Fill");
        GameObject fillObj;
        if (fillTrans == null)
        {
            fillObj = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObj.transform.SetParent(fillAreaObj.transform, false);
        }
        else
        {
            fillObj = fillTrans.gameObject;
        }

        RectTransform fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        Image fillImg = fillObj.GetComponent<Image>();
        fillImg.color = fillColor;

        // Label Text
        Transform labelTrans = sliderRoot.transform.Find("Label");
        GameObject labelObj;
        if (labelTrans == null)
        {
            labelObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObj.transform.SetParent(sliderRoot.transform, false);
        }
        else
        {
            labelObj = labelTrans.gameObject;
        }

        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12f, 0f);
        labelRect.offsetMax = new Vector2(-12f, 0f);

        Text labelText = labelObj.GetComponent<Text>();
        Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (defaultFont != null) labelText.font = defaultFont;
        labelText.fontSize = 14;
        labelText.fontStyle = FontStyle.Bold;
        labelText.alignment = TextAnchor.MiddleLeft;
        labelText.color = Color.white;
        labelText.text = labelTitle;

        // Slider Component
        Slider slider = sliderRoot.GetComponent<Slider>();
        if (slider == null) slider = sliderRoot.AddComponent<Slider>();

        slider.interactable = false;
        slider.transition = Selectable.Transition.None;
        slider.direction = Slider.Direction.LeftToRight;
        slider.targetGraphic = bgImg;
        slider.fillRect = fillRect;
        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.value = 100f;

        return slider;
    }

    public void UpdateBottomHealthBarsUI()
    {
        if (characterHealthSlider != null)
        {
            characterMaxHealth = Mathf.Max(1f, characterMaxHealth);
            characterCurrentHealth = Mathf.Clamp(characterCurrentHealth, 0f, characterMaxHealth);
            characterHealthSlider.maxValue = characterMaxHealth;
            characterHealthSlider.value = characterCurrentHealth;

            Text label = characterHealthSlider.transform.Find("Label")?.GetComponent<Text>();
            if (label != null && label.text != "CHARACTER HP")
            {
                label.text = "CHARACTER HP";
            }

            if (!characterHealthSlider.gameObject.activeSelf)
            {
                characterHealthSlider.gameObject.SetActive(true);
            }
        }

        if (shipHealthSlider != null)
        {
            shipMaxHealth = Mathf.Max(1f, shipMaxHealth);
            shipCurrentHealth = Mathf.Clamp(shipCurrentHealth, 0f, shipMaxHealth);
            shipHealthSlider.maxValue = shipMaxHealth;
            shipHealthSlider.value = shipCurrentHealth;

            Text label = shipHealthSlider.transform.Find("Label")?.GetComponent<Text>();
            if (label != null && label.text != "SPACESHIP HP")
            {
                label.text = "SPACESHIP HP";
            }

            if (shipHealthSlider.gameObject.activeSelf != isPiloting)
            {
                shipHealthSlider.gameObject.SetActive(isPiloting);
            }
        }
    }

    public void EnsureShipLightingRig()
    {
        if (!enableShipLighting) return;

        Transform existingRig = transform.Find("Spaceship_LightingRig");
        if (existingRig == null) existingRig = transform.Find("StarSparrow_LightingRig");

        if (existingRig != null)
        {
            existingRig.name = "Spaceship_LightingRig";
            lightingRigRoot = existingRig;
        }
        else
        {
            GameObject rigObj = new GameObject("Spaceship_LightingRig");
            rigObj.transform.SetParent(transform, false);
            rigObj.transform.localPosition = Vector3.zero;
            rigObj.transform.localRotation = Quaternion.identity;
            lightingRigRoot = rigObj.transform;
        }

        // 1. Top Key Light
        topKeyLight = GetOrCreateLight(
            "Hull_TopKeyLight",
            LightType.Point,
            new Vector3(0f, 6.0f, 0.5f),
            Quaternion.identity,
            new Color(0.92f, 0.96f, 1.0f),
            hullKeyLightIntensity,
            28f
        );

        // 2. Front Nose Fill Light
        frontRimLight = GetOrCreateLight(
            "Hull_FrontFillLight",
            LightType.Point,
            new Vector3(0f, 2.2f, 5.8f),
            Quaternion.identity,
            new Color(0.75f, 0.88f, 1.0f),
            hullRimLightIntensity * 1.1f,
            22f
        );

        // 3. Bottom Cyan Rim Light
        bottomRimLight = GetOrCreateLight(
            "Hull_BottomRimLight",
            LightType.Point,
            new Vector3(0f, -4.0f, -0.5f),
            Quaternion.identity,
            new Color(0.25f, 0.7f, 1.0f),
            hullRimLightIntensity,
            24f
        );

        // 4. Thruster Engine Glow Light
        thrusterLight = GetOrCreateLight(
            "Thruster_EngineGlow",
            LightType.Point,
            new Vector3(0f, -0.15f, -5.5f),
            Quaternion.identity,
            new Color(0.15f, 0.75f, 1.0f),
            thrusterBaseIntensity,
            18f
        );

        // 5. Dual Forward Headlights
        leftHeadlight = GetOrCreateLight(
            "Headlight_Left",
            LightType.Spot,
            new Vector3(-1.6f, -0.2f, 4.0f),
            Quaternion.Euler(4f, 0f, 0f),
            new Color(0.85f, 0.95f, 1.0f),
            20f,
            65f,
            55f
        );

        rightHeadlight = GetOrCreateLight(
            "Headlight_Right",
            LightType.Spot,
            new Vector3(1.6f, -0.2f, 4.0f),
            Quaternion.Euler(4f, 0f, 0f),
            new Color(0.85f, 0.95f, 1.0f),
            20f,
            65f,
            55f
        );

        // 6. Weapon Muzzle Flash Light
        muzzleFlashLight = GetOrCreateLight(
            "Weapon_MuzzleFlashLight",
            LightType.Point,
            new Vector3(0f, -1.0f, 3.2f),
            Quaternion.identity,
            new Color(0.25f, 0.85f, 1.0f),
            0f,
            22f
        );

        EnsureThrusterFlames();
    }

    private Light GetOrCreateLight(string lightName, LightType type, Vector3 localPos, Quaternion localRot, Color color, float intensity, float range, float spotAngle = 45f)
    {
        Transform child = lightingRigRoot.Find(lightName);
        GameObject lightObj;
        if (child == null)
        {
            lightObj = new GameObject(lightName);
            lightObj.transform.SetParent(lightingRigRoot, false);
        }
        else
        {
            lightObj = child.gameObject;
        }

        lightObj.transform.localPosition = localPos;
        lightObj.transform.localRotation = localRot;

        Light l = lightObj.GetComponent<Light>();
        if (l == null) l = lightObj.AddComponent<Light>();

        l.type = type;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        if (type == LightType.Spot)
        {
            l.spotAngle = spotAngle;
            l.innerSpotAngle = spotAngle * 0.6f;
        }
        l.shadows = LightShadows.None;
        return l;
    }

    private void EnsureThrusterFlames()
    {
        Vector3[] nozzlePositions = new Vector3[]
        {
            new Vector3(0f, -0.28f, -4.95f),
            new Vector3(0.97f, -0.50f, -4.85f),
            new Vector3(-0.97f, -0.50f, -4.85f)
        };
        float[] nozzleScales = new float[] { 0.38f, 0.30f, 0.30f };

        thrusterFlames = new Transform[3];

        for (int i = 0; i < 3; i++)
        {
            string flameName = "Thruster_Plume_" + i;
            Transform existing = lightingRigRoot.Find(flameName);
            if (existing == null)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = flameName;
                sphere.transform.SetParent(lightingRigRoot, false);
                Collider col = sphere.GetComponent<Collider>();
                if (col != null)
                {
                    if (Application.isPlaying) Destroy(col);
                    else DestroyImmediate(col);
                }

                Renderer rend = sphere.GetComponent<Renderer>();
                if (rend != null)
                {
                    Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");
                    if (unlitShader != null)
                    {
                        Material flameMat = new Material(unlitShader);
                        flameMat.color = new Color(0.2f, 0.85f, 1.0f, 1.0f);
                        if (flameMat.HasProperty("_BaseColor"))
                            flameMat.SetColor("_BaseColor", new Color(0.25f, 0.9f, 1.0f, 1f));
                        rend.sharedMaterial = flameMat;
                    }
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    rend.receiveShadows = false;
                }
                existing = sphere.transform;
            }

            existing.localPosition = nozzlePositions[i];
            float s = nozzleScales[i];
            existing.localScale = new Vector3(s, s, s * 2.2f);
            thrusterFlames[i] = existing;
        }
    }

    private void UpdateDynamicLightingAndThrusters(float dt)
    {
        if (topKeyLight != null) topKeyLight.intensity = hullKeyLightIntensity;
        if (bottomRimLight != null) bottomRimLight.intensity = hullRimLightIntensity;
        if (frontRimLight != null) frontRimLight.intensity = hullRimLightIntensity * 1.1f;

        float thrustFactor = isPiloting ? Mathf.Max(0.25f, CurrentThrottleInput) : 0.12f;
        float speedFactor = NormalizedSpeed;
        float combinedPower = Mathf.Clamp01(thrustFactor * 0.7f + speedFactor * 0.3f);

        float flicker = 1f + Mathf.Sin(Time.time * 45f) * 0.06f;

        if (thrusterLight != null)
        {
            thrusterLight.intensity = Mathf.Lerp(thrusterBaseIntensity, thrusterBoostIntensity, combinedPower) * flicker;
            thrusterLight.color = Color.Lerp(new Color(0.15f, 0.65f, 1.0f), new Color(0.55f, 0.92f, 1.0f), combinedPower);
        }

        if (thrusterFlames != null)
        {
            float[] baseScales = new float[] { 0.38f, 0.30f, 0.30f };
            for (int i = 0; i < thrusterFlames.Length; i++)
            {
                if (thrusterFlames[i] == null) continue;
                float s = baseScales[i] * Mathf.Lerp(0.5f, 1.4f, combinedPower);
                float lengthZ = s * Mathf.Lerp(1.2f, 4.5f, combinedPower) * flicker;
                thrusterFlames[i].localScale = new Vector3(s, s, lengthZ);
            }
        }

        if (muzzleFlashLight != null && muzzleFlashIntensity > 0.01f)
        {
            muzzleFlashIntensity = Mathf.MoveTowards(muzzleFlashIntensity, 0f, 180f * dt);
            muzzleFlashLight.intensity = muzzleFlashIntensity;
        }
    }

    private Coroutine activeRumbleCoroutine;

    /// <summary>
    /// Triggers physical haptic rumble on the controller when an enemy ship explodes from player attacks.
    /// </summary>
    public static void TriggerGamepadRumble(float lowFreq = 0.85f, float highFreq = 1.0f, float duration = 0.45f)
    {
        if (Instance != null)
        {
            Instance.StartRumble(lowFreq, highFreq, duration);
        }
    }

    public void StartRumble(float lowFreq, float highFreq, float duration)
    {
        if (Gamepad.current == null) return;
        if (activeRumbleCoroutine != null)
        {
            StopCoroutine(activeRumbleCoroutine);
            activeRumbleCoroutine = null;
        }
        activeRumbleCoroutine = StartCoroutine(RumbleCoroutine(lowFreq, highFreq, duration));
    }

    public void StopRumble()
    {
        if (activeRumbleCoroutine != null)
        {
            StopCoroutine(activeRumbleCoroutine);
            activeRumbleCoroutine = null;
        }
        if (Gamepad.current != null)
        {
            Gamepad.current.SetMotorSpeeds(0f, 0f);
        }
    }

    private System.Collections.IEnumerator RumbleCoroutine(float lowFreq, float highFreq, float duration)
    {
        if (Gamepad.current == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (Gamepad.current == null) yield break;

            float t = elapsed / duration;
            // Punchy initial impact with exponential decay
            float curve = (1f - t) * (1f - t);
            Gamepad.current.SetMotorSpeeds(lowFreq * curve, highFreq * curve);

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (Gamepad.current != null)
        {
            Gamepad.current.SetMotorSpeeds(0f, 0f);
        }
        activeRumbleCoroutine = null;
    }

    private void OnGUI()
    {
        // No OnGUI boxes rendered per user request (removed control/status box and top-left box)
    }


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void AutoAttachToSpaceshipInScene()
    {
        GameObject shipObj = GameObject.Find("StarSparrow21");
        if (shipObj == null)
        {
            Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                if (allTransforms[i] != null && allTransforms[i].parent == null && (allTransforms[i].name.StartsWith("StarSparrow21") || allTransforms[i].name.StartsWith("SpaceshipPlayer")))
                {
                    shipObj = allTransforms[i].gameObject;
                    break;
                }
            }
        }

        if (shipObj != null)
        {
            SpaceshipController ctrl = shipObj.GetComponent<SpaceshipController>();
            if (ctrl == null)
            {
                ctrl = shipObj.AddComponent<SpaceshipController>();
            }
            ctrl.EnsureShipLightingRig();
            ctrl.EnsureBottomHealthBarsUI();
        }

        SpaceshipNPC.EnsureAllAllyNPCsSetup();
        GameManager.EnsureGameManagerInScene();
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void EditorAutoSetupSpaceship()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (thisIsQuitting) return;
            AutoAttachToSpaceshipInScene();
        };
    }

    private static bool thisIsQuitting => UnityEditor.EditorApplication.isCompiling;
#endif
}
