using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-Person Camera Controller (New Input System).
/// Supports Mouse & Gamepad Controller (Right Stick) with:
/// - Smooth Orbit Rotation with Response Curve & Acceleration Damping
/// - Over-the-Shoulder Framing & Dynamic Pitch-Distance Curve
/// - Asymmetric Follow Damping (Horizontal tight, Vertical smooth during jumps)
/// - Spring-Arm SphereCast Obstacle Collision (Anti-clipping through walls)
/// - Dynamic FOV & Distance Boost during Sprint / Boost
/// - Camera Weight / Landing Impact Dip & Movement Bob
/// - Dual Mode: Follows character or smoothly tracks active spaceship in flight
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target & Framing (Over-The-Shoulder)")]
    [Tooltip("Target transform followed by the camera.")]
    public Transform target;
    [Tooltip("Camera focus pivot height on character (chest/shoulder area).")]
    public float pivotHeight = 1.45f;
    [Tooltip("Over-the-shoulder lateral offset. 0 = centered.")]
    [Range(-1f, 1f)]
    public float shoulderOffset = 0.3f;
    [Tooltip("Slight look-at vertical height offset.")]
    public float lookAtHeightOffset = 0.15f;

    [Header("Orbit Distance & Pitch Curve")]
    [Tooltip("Default orbit distance on horizontal view.")]
    public float defaultDistance = 2.8f;
    [Tooltip("Orbit distance when looking up.")]
    public float lookUpDistance = 1.85f;
    [Tooltip("Orbit distance when looking down.")]
    public float lookDownDistance = 3.4f;
    [Tooltip("Minimum pitch angle looking up.")]
    public float minPitch = -35f;
    [Tooltip("Maximum pitch angle looking down.")]
    public float maxPitch = 65f;

    [Header("Input & Sensitivity (Mouse & Controller)")]
    [Tooltip("Mouse look sensitivity.")]
    public float mouseSensitivity = 0.14f;
    [Tooltip("Horizontal rotation speed using gamepad right stick.")]
    public float controllerYawSpeed = 190f;
    [Tooltip("Vertical rotation speed using gamepad right stick.")]
    public float controllerPitchSpeed = 130f;
    [Tooltip("Right stick response curve exponent.")]
    [Range(1f, 3f)]
    public float stickResponseCurve = 1.75f;
    [Tooltip("Right stick deadzone.")]
    [Range(0.02f, 0.3f)]
    public float stickDeadzone = 0.1f;
    [Tooltip("Rotation smoothing time.")]
    [Range(0.01f, 0.2f)]
    public float rotationSmoothTime = 0.045f;
    [Tooltip("Invert vertical look.")]
    public bool invertY = false;

    [Header("Follow Damping & Auto-Align")]
    [Tooltip("Horizontal follow smooth time.")]
    [Range(0.01f, 0.3f)]
    public float horizontalFollowSmoothTime = 0.05f;
    [Tooltip("Grounded vertical follow smooth time.")]
    [Range(0.01f, 0.4f)]
    public float verticalFollowSmoothTimeGround = 0.07f;
    [Tooltip("Airborne vertical follow smooth time.")]
    [Range(0.05f, 0.5f)]
    public float verticalFollowSmoothTimeAir = 0.16f;
    [Tooltip("Enable gentle orbit assist when moving sideways without camera input.")]
    public bool enableGentleOrbitAssist = true;
    [Tooltip("Delay before orbit assist activates.")]
    public float orbitAssistDelay = 1.25f;
    [Tooltip("Orbit assist strength.")]
    public float orbitAssistStrength = 22f;

    [Header("Spring-Arm Collision (Anti-Clipping)")]
    [Tooltip("Sphere cast radius for collision detection.")]
    public float collisionRadius = 0.22f;
    [Tooltip("Minimum allowed distance when enclosed by geometry.")]
    public float minCollisionDistance = 0.45f;
    [Tooltip("Pull-in speed when hitting geometry.")]
    public float collisionPullInSpeed = 28f;
    [Tooltip("Push-out speed when clearing obstacles.")]
    public float collisionPushOutSpeed = 6.5f;
    [Tooltip("Layers considered obstacles for the camera.")]
    public LayerMask collisionLayers = ~0;

    [Header("Dynamic Effects (FOV, Sprint & Landing)")]
    [Tooltip("Base field of view.")]
    public float baseFOV = 50f;
    [Tooltip("Additional FOV during sprint.")]
    public float sprintFOVBoost = 8f;
    [Tooltip("Additional camera distance during sprint.")]
    public float sprintDistanceBoost = 0.35f;
    [Tooltip("FOV and distance transition speed.")]
    public float fovTransitionSpeed = 5.5f;
    [Tooltip("Subtle head bob intensity.")]
    public float movementBobAmount = 0.025f;

    [Header("Spaceship Flight Camera Settings")]
    [Tooltip("Whether the camera is currently in spaceship tracking mode.")]
    public bool isSpaceshipMode = false;
    [Tooltip("Reference to the active spaceship.")]
    public SpaceshipController activeSpaceship;
    [Tooltip("Default distance behind the spaceship.")]
    public float shipDefaultDistance = 13.0f;
    [Tooltip("Pivot height above the spaceship.")]
    public float shipPivotHeight = 2.0f;
    [Tooltip("Additional distance added during boost.")]
    public float shipBoostDistanceAdd = 3.6f;
    [Tooltip("Distance reduction when airbraking.")]
    public float shipBrakeDistanceOffset = -2.2f;
    [Tooltip("FOV increase when flying at high speed.")]
    public float shipBoostFOVAdd = 16f;
    [Tooltip("Lateral camera shift during sharp turns.")]
    public float shipLateralGForceOffset = 1.6f;
    [Tooltip("Auto-chase return speed behind the ship tail.")]
    public float shipAutoChaseSpeed = 3.2f;

    // Internal state
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
    /// Switches the camera mode between character and spaceship.
    /// </summary>
    public void SetSpaceshipMode(bool spaceshipActive, SpaceshipController ship)
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
    /// Disables any conflicting Cinemachine components on the camera.
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
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        {
            LockCursor(true);
        }
    }

    /// <summary>
    /// Triggers a camera dip upon landing from high jumps.
    /// </summary>
    public void TriggerLandingImpact(float impactStrength)
    {
        landingDipVelocity -= Mathf.Clamp(impactStrength, 0.5f, 3.5f);
    }

    private float explosionShakeIntensity;

    /// <summary>
    /// Triggers a camera shake impulse when a spaceship explodes nearby.
    /// </summary>
    public void TriggerExplosionShake(float strength)
    {
        explosionShakeIntensity = Mathf.Clamp(explosionShakeIntensity + strength, 0f, 2.2f);
    }

    private void LateUpdate()
    {
        if (target == null) return;
        if (GameManager.Instance != null && (GameManager.Instance.IsPaused || GameManager.Instance.IsGameOver))
        {
            return;
        }

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

        if (explosionShakeIntensity > 0.005f)
        {
            Vector3 shakeOffset = Random.insideUnitSphere * (explosionShakeIntensity * 0.35f);
            transform.position += shakeOffset;
            explosionShakeIntensity = Mathf.MoveTowards(explosionShakeIntensity, 0f, 3.2f * Time.deltaTime);
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

        Vector3 rawShipPivot = shipTransform.position + shipTransform.up * shipPivotHeight;
        smoothedPivotPos = Vector3.SmoothDamp(smoothedPivotPos, rawShipPivot, ref shipPivotVelocity, 0.035f);

        float targetLateral = -activeSpaceship.CurrentSteerInput.x * shipLateralGForceOffset;
        currentShipLateralOffset = Mathf.Lerp(currentShipLateralOffset, targetLateral, 1f - Mathf.Exp(-5.5f * Time.deltaTime));

        float targetCamRoll = activeSpaceship.CurrentBankAngle * 0.22f;
        currentShipCameraRoll = Mathf.Lerp(currentShipCameraRoll, targetCamRoll, 1f - Mathf.Exp(-6f * Time.deltaTime));

        Quaternion orbitRot = Quaternion.Euler(currentPitch, currentYaw, currentShipCameraRoll);

        float desiredShipDist = shipDefaultDistance
            + (activeSpaceship.CurrentThrottleInput * shipBoostDistanceAdd)
            + (activeSpaceship.CurrentBrakeInput * shipBrakeDistanceOffset);

        Vector3 pivotWithGForce = smoothedPivotPos + (orbitRot * Vector3.right) * currentShipLateralOffset;
        Vector3 backDir = orbitRot * Vector3.back;

        float safeDist = ResolveCameraCollision(pivotWithGForce, backDir, desiredShipDist);
        currentDistance = Mathf.Lerp(currentDistance, safeDist, 1f - Mathf.Exp(-8f * Time.deltaTime));

        Vector3 boostShake = Vector3.zero;
        if (activeSpaceship.CurrentThrottleInput > 0.75f && activeSpaceship.NormalizedSpeed > 0.6f)
        {
            float shakeAmt = (activeSpaceship.CurrentThrottleInput - 0.75f) * 0.08f;
            boostShake = (orbitRot * Vector3.right) * (Mathf.Sin(Time.time * 55f) * shakeAmt)
                       + (orbitRot * Vector3.up) * (Mathf.Cos(Time.time * 63f) * shakeAmt);
        }

        Vector3 finalPos = pivotWithGForce + backDir * currentDistance + boostShake;
        transform.SetPositionAndRotation(finalPos, orbitRot);

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

        if (hasManualInput)
        {
            lastManualLookTime = Time.unscaledTime;
        }
        else if (enableGentleOrbitAssist && playerMovement != null && Time.unscaledTime - lastManualLookTime > orbitAssistDelay)
        {
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

        Vector2 currentXZ = new Vector2(smoothedPivotPos.x, smoothedPivotPos.z);
        Vector2 targetXZ = new Vector2(rawPivot.x, rawPivot.z);
        currentXZ = Vector2.SmoothDamp(currentXZ, targetXZ, ref horizontalPosVelocity, horizontalFollowSmoothTime);

        bool grounded = playerMovement == null || playerMovement.isGrounded;
        float verticalSmoothTime = grounded ? verticalFollowSmoothTimeGround : verticalFollowSmoothTimeAir;
        float currentY = Mathf.SmoothDamp(smoothedPivotPos.y, rawPivot.y, ref verticalPosVelocity, verticalSmoothTime);

        landingDipOffset += landingDipVelocity * Time.deltaTime;
        landingDipVelocity = Mathf.Lerp(landingDipVelocity, -landingDipOffset * 28f, Time.deltaTime * 12f);
        landingDipOffset = Mathf.Clamp(landingDipOffset, -0.28f, 0.1f);

        smoothedPivotPos = new Vector3(currentXZ.x, currentY + landingDipOffset, currentXZ.y);
    }

    private void UpdateCameraTransform()
    {
        Quaternion orbitRotation = Quaternion.Euler(currentPitch, currentYaw, 0f);

        bool isSprinting = playerMovement != null && playerMovement.IsSprintingActive();
        float targetSprintBlend = isSprinting ? 1f : 0f;
        currentSprintBlend = Mathf.Lerp(currentSprintBlend, targetSprintBlend, Time.deltaTime * fovTransitionSpeed);

        float desiredDistance = CalculateOrbitDistanceForPitch(currentPitch) + (sprintDistanceBoost * currentSprintBlend);

        Vector3 rightDir = orbitRotation * Vector3.right;
        Vector3 shoulderPivot = smoothedPivotPos + rightDir * shoulderOffset;

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

        Vector3 backDir = orbitRotation * Vector3.back;
        float targetSafeDistance = ResolveCameraCollision(shoulderPivot, backDir, desiredDistance);

        float distLerpSpeed = targetSafeDistance < currentDistance ? collisionPullInSpeed : collisionPushOutSpeed;
        currentDistance = Mathf.Lerp(currentDistance, targetSafeDistance, 1f - Mathf.Exp(-distLerpSpeed * Time.deltaTime));

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

        Vector3 finalCameraPos = shoulderPivot + backDir * currentDistance + bobOffset;
        Quaternion finalRotation = orbitRotation;

        transform.SetPositionAndRotation(finalCameraPos, finalRotation);

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
