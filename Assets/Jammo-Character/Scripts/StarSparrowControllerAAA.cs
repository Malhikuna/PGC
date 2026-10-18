using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// AAA Spaceship Flight Controller & Dynamic Lighting Rig untuk StarSparrow21.
/// Fitur:
/// - Tekan D-Pad Atas (Controller) atau Tombol T (Keyboard) untuk masuk/keluar Mode Pesawat.
/// - RT (Right Trigger / Left Shift): Gas Progresif & Afterburner Boost.
/// - LT (Left Trigger / Space / Ctrl): Airbrake / Rem Inersia & High-G Drift Turn.
/// - Analog Kiri (Left Stick / WASD): Pitch (Naik/Turun) & Yaw + Aerodynamic Bank/Roll (Belok Miring).
/// - Analog Kanan (Right Stick / Mouse): Mengarahkan Kamera Orbit 360 Derajat.
/// - Rig Pencahayaan Pesawat Otomatis (Hull Key Light, Rim Fill Light, Headlights, & Dynamic Thruster Glow).
/// </summary>
[ExecuteAlways]
public class StarSparrowControllerAAA : MonoBehaviour
{
    [Header("Mode Switching")]
    [Tooltip("Apakah pemain saat ini sedang mengendarai pesawat?")]
    public bool isPiloting = false;
    [Tooltip("Referensi ke karakter Jammo (otomatis dicari jika kosong).")]
    public MovementInput characterPlayer;
    [Tooltip("Referensi ke kamera utama AAA (otomatis dicari jika kosong).")]
    public ThirdPersonCameraAAA aaaCamera;
    [Tooltip("Tampilkan indikator HUD pesawat di layar.")]
    public bool showFlightHUD = true;

    [Header("AAA Flight Propulsion (RT = Gas, LT = Rem)")]
    [Tooltip("Kecepatan jelajah maksimal standar.")]
    public float maxCruiseSpeed = 42f;
    [Tooltip("Kecepatan maksimal saat RT ditekan penuh (Afterburner Boost).")]
    public float maxBoostSpeed = 72f;
    [Tooltip("Kecepatan mundur maksimal saat menahan LT dalam posisi berhenti.")]
    public float maxReverseSpeed = 12f;
    [Tooltip("Akselerasi mesin saat menekan RT.")]
    public float forwardAcceleration = 32f;
    [Tooltip("Kekuatan pengereman Airbrake saat menekan LT.")]
    public float brakeDeceleration = 48f;
    [Tooltip("Hambatan inersia alami saat lepas gas di angkasa.")]
    public float passiveSpaceDrag = 4.5f;

    [Header("AAA Flight Maneuvering & Banking (Analog L)")]
    [Tooltip("Kecepatan mendongak/menukik (Pitch) dalam derajat/detik.")]
    public float pitchSpeed = 78f;
    [Tooltip("Kecepatan belok kiri/kanan (Yaw) dalam derajat/detik.")]
    public float yawSpeed = 85f;
    [Tooltip("Sudut kemiringan sayap maksimal (Bank/Roll) saat berbelok tajam.")]
    public float maxBankAngle = 48f;
    [Tooltip("Kecepatan respons kemiringan sayap (Banking).")]
    public float bankSmoothSpeed = 6.5f;
    [Tooltip("Kecepatan sayap kembali rata otomatis saat terbang lurus.")]
    public float autoLevelRollSpeed = 4.0f;
    [Tooltip("Kelembutan akselerasi kemudi Analog Kiri (nilai kecil = lebih responsif).")]
    [Range(0.02f, 0.3f)]
    public float steeringSmoothTime = 0.09f;
    [Tooltip("Kecepatan geser samping (Strafe) halus saat bermanuver kecepatan rendah.")]
    public float lowSpeedStrafeFactor = 8f;
    [Tooltip("Balikkan kendali naik/turun (Invert Pitch) pada Analog Kiri.")]
    public bool invertFlightPitch = false;
    [Tooltip("Bantu arahkan hidung pesawat secara halus ke arah kamera saat sedang ngegas.")]
    public bool alignToCameraWhenThrusting = true;
    [Tooltip("Kekuatan penyelarasan arah pesawat ke arah pandang kamera.")]
    public float cameraAlignStrength = 1.6f;

    [Header("Ship Lighting Rig (Agar Pesawat Terlihat Jelas di Ruang Angkasa)")]
    [Tooltip("Aktifkan pencahayaan otomatis pada badan pesawat.")]
    public bool enableShipLighting = true;
    [Tooltip("Intensitas lampu utama di atas badan pesawat (Key Light).")]
    public float hullKeyLightIntensity = 18f;
    [Tooltip("Intensitas lampu aksen biru di bawah/samping pesawat (Rim/Fill Light).")]
    public float hullRimLightIntensity = 10f;
    [Tooltip("Intensitas cahaya api mesin pendorong (Thruster Glow).")]
    public float thrusterBaseIntensity = 8f;
    [Tooltip("Intensitas maksimal cahaya mesin saat ngegas penuh (RT).")]
    public float thrusterBoostIntensity = 35f;

    // State penerbangan publik untuk dibaca oleh Kamera AAA
    public float CurrentSpeed { get; private set; }
    public float NormalizedSpeed => Mathf.Clamp01(Mathf.Abs(CurrentSpeed) / Mathf.Max(1f, maxBoostSpeed));
    public float CurrentThrottleInput { get; private set; }
    public float CurrentBrakeInput { get; private set; }
    public Vector2 CurrentSteerInput { get; private set; }
    public float CurrentBankAngle { get; private set; }
    public Vector3 CurrentVelocity { get; private set; }

    // Variabel internal fisika & rotasi
    private Vector2 smoothedSteer;
    private Vector2 steerVelocity;
    private float currentRoll;
    private float rollVelocity;
    private Quaternion baseOrientation;
    private Vector3 initialIdlePosition;
    private bool idleOriginRecorded;

    // Referensi lampu & efek visual thruster
    private Transform lightingRigRoot;
    private Light topKeyLight;
    private Light bottomRimLight;
    private Light frontRimLight;
    private Light thrusterLight;
    private Light leftHeadlight;
    private Light rightHeadlight;
    private Transform[] thrusterFlames;

    // Cache renderer karakter saat masuk ke kokpit
    private Renderer[] characterRenderers;

    private void OnEnable()
    {
        EnsureShipLightingRig();
        if (Application.isPlaying)
        {
            baseOrientation = transform.rotation;
            initialIdlePosition = transform.position;
            idleOriginRecorded = true;
        }
    }

    private void Start()
    {
        EnsureShipLightingRig();
        if (!Application.isPlaying) return;

        baseOrientation = transform.rotation;
        // Ratakan roll awal agar pesawat mulai dalam posisi stabil
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude > 0.001f)
        {
            baseOrientation = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
            transform.rotation = baseOrientation;
        }

        initialIdlePosition = transform.position;
        idleOriginRecorded = true;

        FindReferences();
    }

    private void FindReferences()
    {
        if (characterPlayer == null)
        {
            characterPlayer = FindFirstObjectByType<MovementInput>();
        }
        if (characterPlayer != null && (characterRenderers == null || characterRenderers.Length == 0))
        {
            characterRenderers = characterPlayer.GetComponentsInChildren<Renderer>(true);
        }

        if (aaaCamera == null && Camera.main != null)
        {
            aaaCamera = Camera.main.GetComponent<ThirdPersonCameraAAA>();
        }
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            if (enableShipLighting && lightingRigRoot == null)
            {
                EnsureShipLightingRig();
            }
            return;
        }

        FindReferences();
        HandleModeToggleInput();

        if (isPiloting)
        {
            ReadFlightInputs();
            SimulateAAAFlight(Time.deltaTime);
        }
        else
        {
            // Mode parkir: matikan input gas, redam kecepatan, dan beri efek melayang halus (Idle Hover)
            CurrentThrottleInput = 0f;
            CurrentBrakeInput = 0f;
            CurrentSteerInput = Vector2.zero;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, 0f, brakeDeceleration * Time.deltaTime);
            CurrentBankAngle = Mathf.SmoothDamp(CurrentBankAngle, 0f, ref rollVelocity, 0.25f);

            if (CurrentSpeed > 0.01f)
            {
                transform.position += transform.forward * (CurrentSpeed * Time.deltaTime);
            }
            else if (idleOriginRecorded)
            {
                float hoverOffset = Mathf.Sin(Time.time * 1.8f) * 0.12f;
                Vector3 pos = transform.position;
                pos.y = Mathf.Lerp(pos.y, initialIdlePosition.y + hoverOffset, Time.deltaTime * 2f);
                transform.position = pos;
            }
        }

        UpdateDynamicLightingAndThrusters(Time.deltaTime);
    }

    private void HandleModeToggleInput()
    {
        bool togglePressed = false;

        // Tekan Tombol Panah Atas (D-Pad Up) di Controller Xbox
        if (Gamepad.current != null && Gamepad.current.dpad.up.wasPressedThisFrame)
        {
            togglePressed = true;
        }

        // Alternatif di Keyboard: Tombol T (atau Up Arrow jika sedang tidak jalan)
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
        {
            togglePressed = true;
        }

        if (togglePressed)
        {
            SetPilotingMode(!isPiloting);
        }
    }

    /// <summary>
    /// Berpindah antara Mode Karakter (Jammo) dan Mode Pesawat (StarSparrow21).
    /// </summary>
    public void SetPilotingMode(bool pilotActive)
    {
        isPiloting = pilotActive;
        FindReferences();

        if (characterPlayer != null)
        {
            // Aktifkan/nonaktifkan kendali karakter Jammo agar tidak ikut berjalan saat mengemudikan pesawat
            characterPlayer.enabled = !isPiloting;
            if (characterPlayer.controller != null)
            {
                characterPlayer.controller.enabled = !isPiloting;
            }

            // Sembunyikan/tampilkan visual Jammo (seolah masuk ke dalam kokpit pesawat)
            if (characterRenderers != null)
            {
                for (int i = 0; i < characterRenderers.Length; i++)
                {
                    if (characterRenderers[i] != null)
                        characterRenderers[i].enabled = !isPiloting;
                }
            }
        }

        if (isPiloting)
        {
            // Sinkronkan orientasi dasar dengan rotasi pesawat saat ini tanpa kemiringan roll
            Vector3 forwardDir = transform.forward;
            if (forwardDir.sqrMagnitude > 0.001f)
            {
                baseOrientation = Quaternion.LookRotation(forwardDir, Vector3.up);
            }
        }
        else
        {
            // Simpan titik parkir baru saat keluar dari mode pesawat
            initialIdlePosition = transform.position;
            idleOriginRecorded = true;
        }

        // Beritahu Kamera AAA untuk berganti target & profil ke Pesawat atau Karakter
        if (aaaCamera != null)
        {
            aaaCamera.SetSpaceshipMode(isPiloting, this);
        }
    }

    private void ReadFlightInputs()
    {
        float throttle = 0f;
        float brake = 0f;
        Vector2 steer = Vector2.zero;

        // 1. Input Xbox Controller (RT = Gas, LT = Rem, Analog L = Gerak)
        if (Gamepad.current != null)
        {
            throttle = Mathf.Max(throttle, Gamepad.current.rightTrigger.ReadValue());
            brake = Mathf.Max(brake, Gamepad.current.leftTrigger.ReadValue());

            Vector2 leftStick = Gamepad.current.leftStick.ReadValue();
            if (leftStick.magnitude > 0.1f)
            {
                // Kurva respons halus untuk Analog Kiri pesawat
                float normMag = Mathf.Clamp01((leftStick.magnitude - 0.1f) / 0.9f);
                steer += leftStick.normalized * Mathf.Pow(normMag, 1.4f);
            }
        }

        // 2. Input Keyboard Fallback (Shift/W = Gas, Space/Ctrl = Rem, A/D & Panah = Kemudi)
        if (Keyboard.current != null)
        {
            if (Keyboard.current.leftShiftKey.isPressed) throttle = Mathf.Max(throttle, 1f);
            if (Keyboard.current.ctrlKey.isPressed || Keyboard.current.spaceKey.isPressed) brake = Mathf.Max(brake, 1f);

            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) steer.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) steer.x += 1f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) steer.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) steer.y -= 1f;
        }

        CurrentThrottleInput = Mathf.Clamp01(throttle);
        CurrentBrakeInput = Mathf.Clamp01(brake);
        CurrentSteerInput = Vector2.ClampMagnitude(steer, 1f);
    }

    private void SimulateAAAFlight(float dt)
    {
        // 1. Haluskan input kemudi Analog L (Pitch & Yaw)
        smoothedSteer = Vector2.SmoothDamp(smoothedSteer, CurrentSteerInput, ref steerVelocity, steeringSmoothTime);

        // 2. Hitung Kecepatan Maju / Rem (RT untuk Gas, LT untuk Rem)
        if (CurrentThrottleInput > 0.01f && CurrentBrakeInput < 0.2f)
        {
            // Target kecepatan berdasarkan bukaan gas RT (Cruise hingga Boost penuh)
            float targetMaxSpeed = Mathf.Lerp(maxCruiseSpeed * 0.35f, maxBoostSpeed, CurrentThrottleInput);
            float accel = forwardAcceleration * Mathf.Lerp(0.6f, 1.35f, CurrentThrottleInput);
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetMaxSpeed, accel * dt);
        }
        else if (CurrentBrakeInput > 0.01f)
        {
            // Pengereman kuat (Airbrake), dan jika sudah berhenti bisa mundur perlahan
            float targetReverse = CurrentSpeed <= 0.5f ? -maxReverseSpeed * CurrentBrakeInput : 0f;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetReverse, brakeDeceleration * CurrentBrakeInput * dt);
        }
        else
        {
            // Inersia ruang angkasa saat lepas gas & rem
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, 0f, passiveSpaceDrag * dt);
        }

        // 3. Manuver High-G Drift Turn: Saat mengerem (LT) sambil belok di kecepatan tinggi, putaran lebih tajam
        float highGBonus = (CurrentBrakeInput > 0.2f && CurrentSpeed > 10f) ? 1.35f : 1.0f;

        float pitchDir = invertFlightPitch ? 1f : -1f;
        float pitchDelta = smoothedSteer.y * pitchDir * pitchSpeed * highGBonus * dt;
        float yawDelta = smoothedSteer.x * yawSpeed * highGBonus * dt;

        // Terapkan Pitch (lokal) dan Yaw pada orientasi dasar
        Quaternion yawRot = Quaternion.AngleAxis(yawDelta, Vector3.up);
        Quaternion pitchRot = Quaternion.AngleAxis(pitchDelta, baseOrientation * Vector3.right);
        baseOrientation = (yawRot * pitchRot * baseOrientation).normalized;

        // 4. Penyelarasan halus ke arah kamera saat sedang ngegas (seperti Star Wars Battlefront / Chorus)
        if (alignToCameraWhenThrusting && aaaCamera != null && CurrentThrottleInput > 0.15f && CurrentSteerInput.sqrMagnitude < 0.05f)
        {
            Quaternion camAimRotation = aaaCamera.GetOrbitRotation();
            baseOrientation = Quaternion.Slerp(baseOrientation, camAimRotation, cameraAlignStrength * CurrentThrottleInput * dt);
        }

        // Ratakan horizon sumbu Z pada baseOrientation agar tidak terpelintir (anti gimbal-lock)
        Vector3 forwardVec = baseOrientation * Vector3.forward;
        if (Mathf.Abs(Vector3.Dot(forwardVec, Vector3.up)) < 0.96f)
        {
            baseOrientation = Quaternion.LookRotation(forwardVec, Vector3.up);
        }

        // 5. Aerodynamic Banking (Roll): Miringkan badan pesawat secara dinamis saat belok kiri/kanan
        float speedFactor = Mathf.Lerp(0.45f, 1.0f, NormalizedSpeed);
        float targetBank = -smoothedSteer.x * maxBankAngle * speedFactor * highGBonus;
        float bankRate = Mathf.Abs(smoothedSteer.x) > 0.05f ? bankSmoothSpeed : autoLevelRollSpeed;
        CurrentBankAngle = Mathf.Lerp(CurrentBankAngle, targetBank, 1f - Mathf.Exp(-bankRate * dt));

        Quaternion bankRotation = Quaternion.AngleAxis(CurrentBankAngle, Vector3.forward);
        transform.rotation = baseOrientation * bankRotation;

        // 6. Pergerakan Translasi (Maju + Strafe halus saat kecepatan rendah + Hover)
        Vector3 forwardDisplacement = transform.forward * CurrentSpeed;
        float strafeWeight = Mathf.Clamp01(1f - (Mathf.Abs(CurrentSpeed) / maxCruiseSpeed));
        Vector3 strafeDisplacement = (baseOrientation * Vector3.right) * (smoothedSteer.x * lowSpeedStrafeFactor * strafeWeight);
        Vector3 verticalHoverDisplacement = Vector3.up * (-smoothedSteer.y * lowSpeedStrafeFactor * 0.6f * strafeWeight);

        CurrentVelocity = forwardDisplacement + strafeDisplacement + verticalHoverDisplacement;
        Vector3 deltaMove = CurrentVelocity * dt;

        // Deteksi tabrakan lembut (Soft Collision Deflection) agar pesawat memantul/meluncur jika menyerempet stasiun
        if (deltaMove.sqrMagnitude > 0.0001f)
        {
            float moveDist = deltaMove.magnitude;
            if (Physics.SphereCast(transform.position, 2.2f, deltaMove.normalized, out RaycastHit hit, moveDist + 1.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(transform))
                {
                    Vector3 slideDir = Vector3.ProjectOnPlane(deltaMove, hit.normal);
                    deltaMove = slideDir + hit.normal * 0.15f;
                    CurrentSpeed *= 0.75f;
                }
            }
        }

        transform.position += deltaMove;
    }

    /// <summary>
    /// Membuat rig lampu secara otomatis pada StarSparrow21 agar bodi pesawat terlihat jelas & sinematik di ruang angkasa.
    /// </summary>
    public void EnsureShipLightingRig()
    {
        if (!enableShipLighting) return;

        Transform existingRig = transform.Find("StarSparrow_LightingRig");
        if (existingRig != null)
        {
            lightingRigRoot = existingRig;
        }
        else
        {
            GameObject rigObj = new GameObject("StarSparrow_LightingRig");
            rigObj.transform.SetParent(transform, false);
            rigObj.transform.localPosition = Vector3.zero;
            rigObj.transform.localRotation = Quaternion.identity;
            lightingRigRoot = rigObj.transform;
        }

        // 1. Top Key Light (Menyinari seluruh punggung pesawat, kokpit, dan sayap dari atas)
        topKeyLight = GetOrCreateLight(
            "Hull_TopKeyLight",
            LightType.Point,
            new Vector3(0f, 5.5f, 0.5f),
            Quaternion.identity,
            new Color(0.92f, 0.96f, 1.0f),
            hullKeyLightIntensity,
            28f
        );

        // 2. Front Nose Fill Light (Menyinari bagian moncong dan kaca kokpit depan)
        frontRimLight = GetOrCreateLight(
            "Hull_FrontFillLight",
            LightType.Point,
            new Vector3(0f, 2.2f, 5.5f),
            Quaternion.identity,
            new Color(0.75f, 0.88f, 1.0f),
            hullRimLightIntensity * 1.1f,
            22f
        );

        // 3. Bottom Cyan Rim Light (Memberi siluet sci-fi biru pada bagian bawah sayap & senjata)
        bottomRimLight = GetOrCreateLight(
            "Hull_BottomRimLight",
            LightType.Point,
            new Vector3(0f, -3.8f, -0.5f),
            Quaternion.identity,
            new Color(0.25f, 0.7f, 1.0f),
            hullRimLightIntensity,
            24f
        );

        // 4. Thruster Engine Glow Light (Di belakang 3 knalpot mesin pada z = -5.2)
        thrusterLight = GetOrCreateLight(
            "Thruster_EngineGlow",
            LightType.Point,
            new Vector3(0f, -0.15f, -5.3f),
            Quaternion.identity,
            new Color(0.15f, 0.75f, 1.0f),
            thrusterBaseIntensity,
            18f
        );

        // 5. Dual Forward Headlights (Lampu sorot depan pesawat)
        leftHeadlight = GetOrCreateLight(
            "Headlight_Left",
            LightType.Spot,
            new Vector3(-1.6f, -0.2f, 3.8f),
            Quaternion.Euler(4f, 0f, 0f),
            new Color(0.85f, 0.95f, 1.0f),
            20f,
            65f,
            55f
        );

        rightHeadlight = GetOrCreateLight(
            "Headlight_Right",
            LightType.Spot,
            new Vector3(1.6f, -0.2f, 3.8f),
            Quaternion.Euler(4f, 0f, 0f),
            new Color(0.85f, 0.95f, 1.0f),
            20f,
            65f,
            55f
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
        // Posisi ke-3 knalpot mesin StarSparrow21 berdasarkan prefab StarSparrow21
        Vector3[] nozzlePositions = new Vector3[]
        {
            new Vector3(0f, 0.117f, -4.95f),
            new Vector3(0.36f, -0.486f, -4.90f),
            new Vector3(-0.36f, -0.486f, -4.90f)
        };
        float[] nozzleScales = new float[] { 0.36f, 0.24f, 0.24f };

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

        // Efek cahaya & api mesin membesar saat RT ditekan
        float thrustFactor = isPiloting ? Mathf.Clamp01(CurrentThrottleInput * 0.7f + NormalizedSpeed * 0.5f) : 0.15f;
        float flicker = 1f + Mathf.Sin(Time.time * 42f) * 0.08f;

        if (thrusterLight != null)
        {
            thrusterLight.intensity = Mathf.Lerp(thrusterBaseIntensity, thrusterBoostIntensity, thrustFactor) * flicker;
            thrusterLight.color = Color.Lerp(new Color(0.15f, 0.65f, 1.0f), new Color(0.55f, 0.92f, 1.0f), thrustFactor);
        }

        if (thrusterFlames != null)
        {
            float[] baseScales = new float[] { 0.36f, 0.24f, 0.24f };
            for (int i = 0; i < thrusterFlames.Length; i++)
            {
                if (thrusterFlames[i] == null) continue;
                float s = baseScales[i] * Mathf.Lerp(0.65f, 1.35f, thrustFactor);
                float lengthZ = s * Mathf.Lerp(1.4f, 4.8f, thrustFactor) * flicker;
                thrusterFlames[i].localScale = new Vector3(s, s, lengthZ);
            }
        }
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || !showFlightHUD) return;

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.fontSize = 13;
        boxStyle.alignment = TextAnchor.MiddleLeft;
        boxStyle.normal.textColor = Color.white;

        if (!isPiloting)
        {
            GUI.Box(new Rect(16, 16, 350, 34), "  [D-Pad Atas / Tombol T] : Masuk Mode Pesawat StarSparrow", boxStyle);
        }
        else
        {
            int speedKmh = Mathf.RoundToInt(CurrentSpeed * 12f);
            int throttlePct = Mathf.RoundToInt(CurrentThrottleInput * 100f);
            string status = CurrentBrakeInput > 0.2f ? "AIRBRAKE (LT)" : (CurrentThrottleInput > 0.85f ? "AFTERBURNER BOOST (RT)" : "FLIGHT MODE");
            string hudText = $"  MODE PESAWAT: {status}\n  Kecepatan: {speedKmh} km/h  |  Gas (RT): {throttlePct}%\n  [D-Pad Atas / T]: Kembali ke Karakter Jammo";
            GUI.Box(new Rect(16, 16, 370, 64), hudText, boxStyle);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void AutoAttachToStarSparrowInScene()
    {
        GameObject shipObj = GameObject.Find("StarSparrow21");
        if (shipObj == null)
        {
            Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                if (allTransforms[i] != null && allTransforms[i].parent == null && allTransforms[i].name.StartsWith("StarSparrow"))
                {
                    shipObj = allTransforms[i].gameObject;
                    break;
                }
            }
        }

        if (shipObj != null)
        {
            StarSparrowControllerAAA ctrl = shipObj.GetComponent<StarSparrowControllerAAA>();
            if (ctrl == null)
            {
                ctrl = shipObj.AddComponent<StarSparrowControllerAAA>();
            }
            ctrl.EnsureShipLightingRig();
        }
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void EditorAutoSetupStarSparrow()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if ( thisIsQuitting ) return;
            AutoAttachToStarSparrowInScene();
        };
    }

    private static bool thisIsQuitting => UnityEditor.EditorApplication.isCompiling;
#endif
}
