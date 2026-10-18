using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// AAA-Style Third-Person Camera Controller (New Input System).
/// Mendukung Mouse & Xbox Controller (Right Stick) dengan fitur:
/// - Smooth Orbit Rotation dengan Response Curve & Acceleration Damping
/// - Over-the-Shoulder Framing & Dynamic Pitch-Distance Curve (Low/Mid/High Rig)
/// - Asymmetric Follow Damping (Horizontal ketat, Vertikal halus saat melompat)
/// - Spring-Arm SphereCast Obstacle Collision (Anti tembus tembok, mengabaikan Trigger)
/// - Dynamic FOV & Distance Boost saat Sprint
/// - Camera Weight / Landing Impact Dip & Subtle Movement Bob
/// </summary>
public class ThirdPersonCameraAAA : MonoBehaviour
{
    [Header("Target & Framing (Over-The-Shoulder)")]
    [Tooltip("Transform karakter utama yang diikuti kamera.")]
    public Transform target;
    [Tooltip("Tinggi titik fokus kamera pada karakter (area dada/bahu).")]
    public float pivotHeight = 1.45f;
    [Tooltip("Pergeseran kamera ke samping bahu (Over-the-Shoulder). 0 = tepat di tengah.")]
    [Range(-1f, 1f)]
    public float shoulderOffset = 0.3f;
    [Tooltip("Sedikit mengangkat titik pandang (LookAt) saat berjalan/berlari.")]
    public float lookAtHeightOffset = 0.15f;

    [Header("Orbit Distance & Pitch Curve")]
    [Tooltip("Jarak kamera standar pada sudut pandang mendatar (Middle Rig).")]
    public float defaultDistance = 2.8f;
    [Tooltip("Jarak kamera saat mendongak dari bawah (Bottom Rig) agar tidak menabrak lantai.")]
    public float lookUpDistance = 1.85f;
    [Tooltip("Jarak kamera saat melihat dari atas ke bawah (Top Rig).")]
    public float lookDownDistance = 3.4f;
    [Tooltip("Batas sudut mendongak ke atas (derajat negatif).")]
    public float minPitch = -35f;
    [Tooltip("Batas sudut menunduk ke bawah (derajat positif).")]
    public float maxPitch = 65f;

    [Header("Input & Sensitivity (Mouse & Controller)")]
    [Tooltip("Sensitivitas pergerakan Mouse.")]
    public float mouseSensitivity = 0.14f;
    [Tooltip("Kecepatan putar Horizontal menggunakan Analog Kanan (derajat/detik).")]
    public float controllerYawSpeed = 190f;
    [Tooltip("Kecepatan putar Vertikal menggunakan Analog Kanan (derajat/detik).")]
    public float controllerPitchSpeed = 130f;
    [Tooltip("Kurva respons Analog Kanan (1 = Linear, 1.8 = Presisi tinggi di tengah & cepat di ujung).")]
    [Range(1f, 3f)]
    public float stickResponseCurve = 1.75f;
    [Tooltip("Deadzone untuk mencegah drift pada Analog Kanan.")]
    [Range(0.02f, 0.3f)]
    public float stickDeadzone = 0.1f;
    [Tooltip("Kelembutan akselerasi/deselerasi rotasi kamera (nilai kecil = lebih responsif).")]
    [Range(0.01f, 0.2f)]
    public float rotationSmoothTime = 0.045f;
    [Tooltip("Balikkan sumbu Y (Invert Y).")]
    public bool invertY = false;

    [Header("Follow Damping & Auto-Align")]
    [Tooltip("Kehalusan kamera mengikuti gerak kiri/kanan/maju karakter.")]
    [Range(0.01f, 0.3f)]
    public float horizontalFollowSmoothTime = 0.05f;
    [Tooltip("Kehalusan kamera mengikuti gerak vertikal saat di darat.")]
    [Range(0.01f, 0.4f)]
    public float verticalFollowSmoothTimeGround = 0.07f;
    [Tooltip("Kehalusan kamera mengikuti gerak vertikal saat melompat di udara.")]
    [Range(0.05f, 0.5f)]
    public float verticalFollowSmoothTimeAir = 0.16f;
    [Tooltip("Bantu arahkan kamera perlahan saat karakter bergerak menyamping tanpa input kamera.")]
    public bool enableGentleOrbitAssist = true;
    [Tooltip("Jeda waktu setelah input kamera terakhir sebelum Orbit Assist aktif (detik).")]
    public float orbitAssistDelay = 1.25f;
    [Tooltip("Kekuatan Orbit Assist saat berlari menyamping.")]
    public float orbitAssistStrength = 22f;

    [Header("Spring-Arm Collision (Anti-Clipping)")]
    [Tooltip("Radius bola pendeteksi tabrakan kamera dengan tembok.")]
    public float collisionRadius = 0.22f;
    [Tooltip("Jarak minimum kamera ke karakter saat terhimpit tembok.")]
    public float minCollisionDistance = 0.45f;
    [Tooltip("Kecepatan kamera maju saat terhalang tembok (harus cepat agar tidak tembus).")]
    public float collisionPullInSpeed = 28f;
    [Tooltip("Kecepatan kamera mundur kembali setelah tembok lewat.")]
    public float collisionPushOutSpeed = 6.5f;
    [Tooltip("Layer yang dianggap penghalang kamera.")]
    public LayerMask collisionLayers = ~0;

    [Header("AAA Dynamic Effects (FOV, Sprint & Landing)")]
    [Tooltip("Field of View (FOV) standar kamera.")]
    public float baseFOV = 50f;
    [Tooltip("Tambahan FOV saat karakter berlari kencang (Sprint).")]
    public float sprintFOVBoost = 8f;
    [Tooltip("Tambahan jarak kamera saat berlari kencang (Sprint).")]
    public float sprintDistanceBoost = 0.35f;
    [Tooltip("Kecepatan transisi perubahan FOV dan jarak Sprint.")]
    public float fovTransitionSpeed = 5.5f;
    [Tooltip("Intensitas ayunan langkah halus (Head/Camera Bob) saat berlari.")]
    public float movementBobAmount = 0.025f;

    [Header("AAA Spaceship Flight Camera Settings")]
    [Tooltip("Apakah kamera sedang berada dalam mode mengikuti pesawat?")]
    public bool isSpaceshipMode = false;
    [Tooltip("Referensi pesawat StarSparrow yang sedang dikendarai.")]
    public StarSparrowControllerAAA activeSpaceship;
    [Tooltip("Jarak kamera standar di belakang pesawat StarSparrow21.")]
    public float shipDefaultDistance = 13.0f;
    [Tooltip("Tinggi fokus kamera di atas bodi pesawat.")]
    public float shipPivotHeight = 2.0f;
    [Tooltip("Tambahan jarak kamera saat pesawat ngegas penuh (RT Boost G-Force).")]
    public float shipBoostDistanceAdd = 3.6f;
    [Tooltip("Pengurangan jarak kamera saat pesawat mengerem (LT Airbrake).")]
    public float shipBrakeDistanceOffset = -2.2f;
    [Tooltip("Tambahan FOV saat pesawat melaju kencang (Speed Sense AAA).")]
    public float shipBoostFOVAdd = 16f;
    [Tooltip("Pergeseran kamera ke samping saat pesawat bermanuver belok tajam (Lateral G-Force).")]
    public float shipLateralGForceOffset = 1.6f;
    [Tooltip("Kecepatan kamera otomatis kembali ke belakang ekor pesawat saat Analog R dilepas.")]
    public float shipAutoChaseSpeed = 3.2f;

    // State internal
    private Camera cam;
    private Transform characterTarget;
    private MovementInput playerMovement;
    private float targetYaw;
    private float targetPitch = 12f;
    private float currentYaw;
    private float currentPitch = 12f;
    private float yawVelocity;
    private float pitchVelocity;

    private Vector3 smoothedPivotPos;
    private Vector2 horizontalPosVelocity;
    private float verticalPosVelocity;
    private Vector3 shipPivotVelocity;
    private float currentShipLateralOffset;
    private float currentShipCameraRoll;

    private float currentDistance;
    private float currentSprintBlend;
    private float lastManualLookTime = -10f;

    // Efek pendaratan (Landing Impact Dip) & Step Bob
    private float landingDipOffset;
    private float landingDipVelocity;
    private float bobTimer;

    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    private void Awake()
    {
        cam = GetComponent<Camera>();
        DisableConflictingCinemachine();
    }

    private void Start()
    {
        if (target == null)
        {
            MovementInput foundPlayer = FindFirstObjectByType<MovementInput>();
            if (foundPlayer != null)
            {
                target = foundPlayer.transform;
                characterTarget = foundPlayer.transform;
                playerMovement = foundPlayer;
            }
        }
        else
        {
            characterTarget = target;
            playerMovement = target.GetComponent<MovementInput>();
        }

        DisableConflictingCinemachine();

        if (target != null)
        {
            targetYaw = target.eulerAngles.y;
            currentYaw = targetYaw;
            smoothedPivotPos = GetTargetPivotPosition();
        }
        else
        {
            targetYaw = transform.eulerAngles.y;
            currentYaw = targetYaw;
        }

        currentDistance = defaultDistance;
        if (cam != null)
        {
            cam.fieldOfView = baseFOV;
            cam.nearClipPlane = 0.08f;
        }

        LockCursor(true);
    }

    /// <summary>
    /// Mengaktifkan atau menonaktifkan Mode Kamera Pesawat AAA.
    /// </summary>
    public void SetSpaceshipMode(bool spaceshipActive, StarSparrowControllerAAA ship)
    {
        if (characterTarget == null && playerMovement != null)
        {
            characterTarget = playerMovement.transform;
        }

        isSpaceshipMode = spaceshipActive && ship != null;
        activeSpaceship = ship;

        if (isSpaceshipMode)
        {
            target = ship.transform;
            Vector3 shipEuler = ship.transform.eulerAngles;
            targetYaw = shipEuler.y;
            currentYaw = targetYaw;
            targetPitch = 10f;
            currentPitch = 10f;
            smoothedPivotPos = ship.transform.position + ship.transform.up * shipPivotHeight;
        }
        else if (characterTarget != null)
        {
            target = characterTarget;
            targetYaw = characterTarget.eulerAngles.y;
            currentYaw = targetYaw;
            targetPitch = 12f;
            currentPitch = 12f;
            smoothedPivotPos = GetTargetPivotPosition();
        }
    }

    public Quaternion GetOrbitRotation()
    {
        return Quaternion.Euler(currentPitch, currentYaw, 0f);
    }

    /// <summary>
    /// Menonaktifkan CinemachineBrain bawaan lama agar tidak membajak rotasi Main Camera.
    /// </summary>
    public void DisableConflictingCinemachine()
    {
        Behaviour[] behaviours = GetComponents<Behaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null && behaviours[i] != this)
            {
                string typeName = behaviours[i].GetType().Name;
                if (typeName.Contains("Cinemachine"))
                {
                    behaviours[i].enabled = false;
                }
            }
        }
    }

    private void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void HandleCursorToggle()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            LockCursor(Cursor.lockState != CursorLockMode.Locked);
        }
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        {
            LockCursor(true);
        }
    }

    /// <summary>
    /// Dipanggil saat karakter mendarat di lantai untuk memberikan efek kamera sedikit membal (weighty landing).
    /// </summary>
    public void TriggerLandingImpact(float impactStrength)
    {
        landingDipVelocity -= Mathf.Clamp(impactStrength, 0.5f, 3.5f);
    }

    private void LateUpdate()
    {
        if (target == null) return;

        HandleCursorToggle();

        if (isSpaceshipMode && activeSpaceship != null)
        {
            ReadLookInputSpaceship();
            UpdateSpaceshipCameraTransform();
        }
        else
        {
            ReadLookInput();
            UpdatePivotFollow();
            UpdateCameraTransform();
        }
    }

    private void ReadLookInputSpaceship()
    {
        float lookX = 0f;
        float lookY = 0f;
        bool hasManualInput = false;

        if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            if (mouseDelta.sqrMagnitude > 0.001f)
            {
                lookX += mouseDelta.x * mouseSensitivity;
                lookY += mouseDelta.y * mouseSensitivity;
                hasManualInput = true;
            }
        }

        if (Gamepad.current != null)
        {
            Vector2 rightStick = Gamepad.current.rightStick.ReadValue();
            float magnitude = rightStick.magnitude;

            if (magnitude > stickDeadzone)
            {
                float normalizedMag = Mathf.Clamp01((magnitude - stickDeadzone) / (1f - stickDeadzone));
                float curvedMag = Mathf.Pow(normalizedMag, stickResponseCurve);
                Vector2 curvedStick = rightStick.normalized * curvedMag;

                lookX += curvedStick.x * controllerYawSpeed * Time.unscaledDeltaTime;
                lookY += curvedStick.y * controllerPitchSpeed * Time.unscaledDeltaTime;
                hasManualInput = true;
            }
        }

        float yDirection = invertY ? 1f : -1f;
        targetYaw += lookX;
        targetPitch = Mathf.Clamp(targetPitch + lookY * yDirection, -72f, 72f);

        if (hasManualInput)
        {
            lastManualLookTime = Time.unscaledTime;
        }
        else if (Time.unscaledTime - lastManualLookTime > 0.65f)
        {
            // Saat Analog R tidak disentuh, kamera secara halus mengikuti arah terbang pesawat (Chase Camera AAA)
            Vector3 shipForward = activeSpaceship.transform.forward;
            float shipYaw = Mathf.Atan2(shipForward.x, shipForward.z) * Mathf.Rad2Deg;
            float shipPitch = -Mathf.Asin(Mathf.Clamp(shipForward.y, -0.95f, 0.95f)) * Mathf.Rad2Deg + 8f;

            float chaseFactor = 1f - Mathf.Exp(-shipAutoChaseSpeed * Time.deltaTime);
            targetYaw = Mathf.LerpAngle(targetYaw, shipYaw, chaseFactor);
            targetPitch = Mathf.Lerp(targetPitch, Mathf.Clamp(shipPitch, -65f, 65f), chaseFactor);
        }

        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, 1f - Mathf.Exp(-14f * Time.deltaTime));
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, 1f - Mathf.Exp(-14f * Time.deltaTime));
    }

    private void UpdateSpaceshipCameraTransform()
    {
        Transform shipTransform = activeSpaceship.transform;

        // 1. Ikuti titik fokus pesawat dengan peredaman (Spring Damping)
        Vector3 rawShipPivot = shipTransform.position + shipTransform.up * shipPivotHeight;
        smoothedPivotPos = Vector3.SmoothDamp(smoothedPivotPos, rawShipPivot, ref shipPivotVelocity, 0.035f);

        // 2. Efek Lateral G-Force & Camera Roll saat pesawat bermanuver belok (Banking)
        float targetLateral = -activeSpaceship.CurrentSteerInput.x * shipLateralGForceOffset;
        currentShipLateralOffset = Mathf.Lerp(currentShipLateralOffset, targetLateral, 1f - Mathf.Exp(-5.5f * Time.deltaTime));

        float targetCamRoll = activeSpaceship.CurrentBankAngle * 0.22f;
        currentShipCameraRoll = Mathf.Lerp(currentShipCameraRoll, targetCamRoll, 1f - Mathf.Exp(-6f * Time.deltaTime));

        Quaternion orbitRot = Quaternion.Euler(currentPitch, currentYaw, currentShipCameraRoll);

        // 3. Hitung Jarak Dinamis berdasarkan Gas (RT) dan Rem (LT)
        float desiredShipDist = shipDefaultDistance
            + (activeSpaceship.CurrentThrottleInput * shipBoostDistanceAdd)
            + (activeSpaceship.CurrentBrakeInput * shipBrakeDistanceOffset);

        Vector3 pivotWithGForce = smoothedPivotPos + (orbitRot * Vector3.right) * currentShipLateralOffset;
        Vector3 backDir = orbitRot * Vector3.back;

        // Cegah kamera menembus struktur stasiun luar angkasa
        float safeDist = ResolveCameraCollision(pivotWithGForce, backDir, desiredShipDist);
        currentDistance = Mathf.Lerp(currentDistance, safeDist, 1f - Mathf.Exp(-8f * Time.deltaTime));

        // 4. Getaran mesin super halus (Micro-Shake) saat Afterburner Boost (RT penuh)
        Vector3 boostShake = Vector3.zero;
        if (activeSpaceship.CurrentThrottleInput > 0.75f && activeSpaceship.NormalizedSpeed > 0.6f)
        {
            float shakeAmt = (activeSpaceship.CurrentThrottleInput - 0.75f) * 0.08f;
            boostShake = (orbitRot * Vector3.right) * (Mathf.Sin(Time.time * 55f) * shakeAmt)
                       + (orbitRot * Vector3.up) * (Mathf.Cos(Time.time * 63f) * shakeAmt);
        }

        Vector3 finalPos = pivotWithGForce + backDir * currentDistance + boostShake;
        transform.SetPositionAndRotation(finalPos, orbitRot);

        // 5. Dynamic FOV melebar saat kecepatan tinggi (AAA Speed Sense)
        if (cam != null)
        {
            float speedBlend = Mathf.Clamp01(activeSpaceship.NormalizedSpeed * 0.75f + activeSpaceship.CurrentThrottleInput * 0.35f);
            float targetFOV = baseFOV + 4f + (shipBoostFOVAdd * speedBlend);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, 1f - Mathf.Exp(-fovTransitionSpeed * Time.deltaTime));
        }
    }

    private void ReadLookInput()
    {
        float lookX = 0f;
        float lookY = 0f;
        bool hasManualInput = false;

        // 1. Input Mouse (aktif ketika kursor terkunci)
        if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            if (mouseDelta.sqrMagnitude > 0.001f)
            {
                lookX += mouseDelta.x * mouseSensitivity;
                lookY += mouseDelta.y * mouseSensitivity;
                hasManualInput = true;
            }
        }

        // 2. Input Controller (Analog Kanan / Right Stick) dengan Deadzone & Response Curve AAA
        if (Gamepad.current != null)
        {
            Vector2 rightStick = Gamepad.current.rightStick.ReadValue();
            float magnitude = rightStick.magnitude;

            if (magnitude > stickDeadzone)
            {
                // Normalisasi ulang nilai di atas deadzone menjadi 0..1 lalu terapkan kurva eksponensial
                float normalizedMag = Mathf.Clamp01((magnitude - stickDeadzone) / (1f - stickDeadzone));
                float curvedMag = Mathf.Pow(normalizedMag, stickResponseCurve);
                Vector2 curvedStick = rightStick.normalized * curvedMag;

                lookX += curvedStick.x * controllerYawSpeed * Time.unscaledDeltaTime;
                lookY += curvedStick.y * controllerPitchSpeed * Time.unscaledDeltaTime;
                hasManualInput = true;
            }
        }

        if (hasManualInput)
        {
            lastManualLookTime = Time.unscaledTime;
        }
        else if (enableGentleOrbitAssist && playerMovement != null && Time.unscaledTime - lastManualLookTime > orbitAssistDelay)
        {
            // Orbit Assist halus saat karakter bergerak menyamping (seperti game AAA konsol)
            float horizInput = playerMovement.InputX;
            float moveSpeed = playerMovement.Speed;
            if (moveSpeed > 0.15f && Mathf.Abs(horizInput) > 0.2f)
            {
                lookX += horizInput * orbitAssistStrength * Mathf.Clamp01(moveSpeed) * Time.deltaTime;
            }
        }

        float yDirection = invertY ? 1f : -1f;
        targetYaw += lookX;
        targetPitch = Mathf.Clamp(targetPitch + lookY * yDirection, minPitch, maxPitch);

        // Interpolasi halus untuk rotasi kamera bebas patah-patah
        currentYaw = Mathf.SmoothDamp(currentYaw, targetYaw, ref yawVelocity, rotationSmoothTime);
        currentPitch = Mathf.SmoothDamp(currentPitch, targetPitch, ref pitchVelocity, rotationSmoothTime);
    }

    private Vector3 GetTargetPivotPosition()
    {
        return target.position + Vector3.up * (pivotHeight + lookAtHeightOffset);
    }

    private void UpdatePivotFollow()
    {
        Vector3 rawPivot = GetTargetPivotPosition();

        // Damping Horizontal (X & Z) ketat agar karakter tetap presisi di layar
        Vector2 currentXZ = new Vector2(smoothedPivotPos.x, smoothedPivotPos.z);
        Vector2 targetXZ = new Vector2(rawPivot.x, rawPivot.z);
        currentXZ = Vector2.SmoothDamp(currentXZ, targetXZ, ref horizontalPosVelocity, horizontalFollowSmoothTime);

        // Damping Vertikal (Y) lebih lembut saat di udara agar lompatan terlihat sinematik
        bool grounded = playerMovement == null || playerMovement.isGrounded;
        float verticalSmoothTime = grounded ? verticalFollowSmoothTimeGround : verticalFollowSmoothTimeAir;
        float currentY = Mathf.SmoothDamp(smoothedPivotPos.y, rawPivot.y, ref verticalPosVelocity, verticalSmoothTime);

        // Simulasi pegas untuk efek Landing Impact Dip
        landingDipOffset += landingDipVelocity * Time.deltaTime;
        landingDipVelocity = Mathf.Lerp(landingDipVelocity, -landingDipOffset * 28f, Time.deltaTime * 12f);
        landingDipOffset = Mathf.Clamp(landingDipOffset, -0.28f, 0.1f);

        smoothedPivotPos = new Vector3(currentXZ.x, currentY + landingDipOffset, currentXZ.y);
    }

    private void UpdateCameraTransform()
    {
        Quaternion orbitRotation = Quaternion.Euler(currentPitch, currentYaw, 0f);

        // Hitung blend Sprint (0 = jalan/diam, 1 = sprint penuh)
        bool isSprinting = playerMovement != null && playerMovement.IsSprintingActive();
        float targetSprintBlend = isSprinting ? 1f : 0f;
        currentSprintBlend = Mathf.Lerp(currentSprintBlend, targetSprintBlend, Time.deltaTime * fovTransitionSpeed);

        // Hitung jarak ideal berdasarkan kurva Pitch (mendongak = lebih dekat, menunduk = lebih jauh)
        float desiredDistance = CalculateOrbitDistanceForPitch(currentPitch) + (sprintDistanceBoost * currentSprintBlend);

        // Titik pusat bahu (Over-the-Shoulder) dengan pengecekan agar bahu tidak menembus tembok samping
        Vector3 rightDir = orbitRotation * Vector3.right;
        Vector3 shoulderPivot = smoothedPivotPos + rightDir * shoulderOffset;

        // Cek apakah offset bahu menabrak tembok di samping karakter
        if (Mathf.Abs(shoulderOffset) > 0.01f)
        {
            Vector3 toShoulder = shoulderPivot - smoothedPivotPos;
            float shoulderDist = toShoulder.magnitude;
            if (Physics.SphereCast(smoothedPivotPos, collisionRadius * 0.75f, toShoulder.normalized, out RaycastHit sideHit, shoulderDist, collisionLayers, QueryTriggerInteraction.Ignore))
            {
                if (!IsTargetCollider(sideHit.collider))
                {
                    shoulderPivot = smoothedPivotPos + toShoulder.normalized * Mathf.Max(0f, sideHit.distance - 0.05f);
                }
            }
        }

        // Raycast/SphereCast ke belakang untuk mendeteksi tembok/pintu
        Vector3 backDir = orbitRotation * Vector3.back;
        float targetSafeDistance = ResolveCameraCollision(shoulderPivot, backDir, desiredDistance);

        // Transisi jarak asimetris: Cepat saat maju menghindari tembok, halus saat mundur kembali
        float distLerpSpeed = targetSafeDistance < currentDistance ? collisionPullInSpeed : collisionPushOutSpeed;
        currentDistance = Mathf.Lerp(currentDistance, targetSafeDistance, 1f - Mathf.Exp(-distLerpSpeed * Time.deltaTime));

        // Efek ayunan langkah halus (Subtle Camera Bob) saat bergerak di tanah
        Vector3 bobOffset = Vector3.zero;
        if (playerMovement != null && playerMovement.isGrounded && playerMovement.Speed > 0.1f)
        {
            float bobFreq = Mathf.Lerp(7.5f, 12.5f, currentSprintBlend);
            bobTimer += Time.deltaTime * bobFreq;
            float bobAmp = movementBobAmount * Mathf.Clamp01(playerMovement.Speed);
            bobOffset = (orbitRotation * Vector3.up) * (Mathf.Sin(bobTimer) * bobAmp) +
                        (orbitRotation * Vector3.right) * (Mathf.Cos(bobTimer * 0.5f) * bobAmp * 0.5f);
        }
        else
        {
            bobTimer = 0f;
        }

        // Posisi akhir kamera sepanjang garis pandang orbit (mencegah kamera mendadak menunduk saat dekat tembok)
        Vector3 finalCameraPos = shoulderPivot + backDir * currentDistance + bobOffset;
        Quaternion finalRotation = orbitRotation;

        transform.SetPositionAndRotation(finalCameraPos, finalRotation);

        // Update Dynamic FOV saat Sprint
        if (cam != null)
        {
            float targetFOV = baseFOV + (sprintFOVBoost * currentSprintBlend);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, 1f - Mathf.Exp(-fovTransitionSpeed * Time.deltaTime));
        }
    }

    private float CalculateOrbitDistanceForPitch(float pitch)
    {
        if (pitch < 0f)
        {
            float t = Mathf.InverseLerp(0f, minPitch, pitch);
            // SmoothStep agar transisi lengkungan orbit halus
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(defaultDistance, lookUpDistance, t);
        }
        else
        {
            float t = Mathf.InverseLerp(0f, maxPitch, pitch);
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(defaultDistance, lookDownDistance, t);
        }
    }

    private float ResolveCameraCollision(Vector3 origin, Vector3 direction, float maxDistance)
    {
        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            collisionRadius,
            direction,
            hitBuffer,
            maxDistance,
            collisionLayers,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance = maxDistance;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hitBuffer[i];
            if (hit.collider == null || IsTargetCollider(hit.collider))
                continue;

            // Abaikan jika jarak hit 0 (overlap di titik awal) kecuali benar-benar objek luar
            if (hit.distance > 0.001f && hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
            }
        }

        return Mathf.Clamp(closestDistance, minCollisionDistance, maxDistance);
    }

    private bool IsTargetCollider(Collider col)
    {
        if (target == null || col == null) return false;
        return col.transform == target || col.transform.IsChildOf(target) || col.transform.root == target.root;
    }
}
