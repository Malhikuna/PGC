using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemy Spaceship AI Controller (StarSparrow17) spawned in waves by GameManager.
/// Features:
/// - Dramatic Jumpspace / Hyperspace arrival effect (warp portal flash + stretched hyper-streak deceleration).
/// - Identical Ship Lighting Rig as Player & Ally NPC ships.
/// - Thin Red Light Lines along the hull/wings + thin red wingtip light trails to clearly identify enemies.
/// - Dynamic AI combat: primary objective is attacking the Space Station, while also engaging Player & Ally NPCs.
/// </summary>
public class EnemySpaceship : MonoBehaviour
{
    public static readonly List<EnemySpaceship> ActiveEnemies = new List<EnemySpaceship>();

    public enum TargetType
    {
        Station,
        Player,
        AllyNPC
    }

    [Header("Enemy Identity & Health")]
    public string enemyCallsign = "RAIDER-17";
    public float maxHealth = 100f;
    public float currentHealth = 100f;

    [Header("Combat & Flight Settings")]
    public float cruiseSpeed = 34f;
    public float attackRunSpeed = 42f;
    public float turnResponsiveness = 3.2f;
    public float maxBankAngle = 46f;
    public float bankSmoothSpeed = 5.5f;
    public float fireRate = 2.4f;
    public float laserSpeed = 195f;
    public float laserDamage = 10f;
    public float attackRange = 135f;

    [Header("Ship Lighting Rig (Same as Player & Ally NPC)")]
    public bool enableShipLighting = true;
    public float hullKeyLightIntensity = 18f;
    public float hullRimLightIntensity = 10f;
    public float thrusterBaseIntensity = 8f;
    public float thrusterBoostIntensity = 35f;

    // Jumpspace arrival state
    private bool isArrivingFromJumpspace = false;
    private float jumpspaceTimer = 0f;
    private float jumpspaceDuration = 0.90f;
    private Vector3 jumpspaceStartPos;
    private Vector3 jumpspaceExitPos;
    private Quaternion jumpspaceRotation;
    private GameObject jumpspacePortalEffect;

    // Flight & AI state
    private TargetType currentTargetType = TargetType.Station;
    private SpaceshipNPC targetAlly;
    private float nextTargetEvalTime;
    private float nextFireTime;
    private bool isEvading = false;
    private float evadeEndTime;
    private Vector3 evadeDirection;
    private Quaternion smoothHeading;
    private float currentBankAngle;
    private float currentSpeed;
    private bool alternateMuzzle;
    private Transform lastAttacker;

    // Lighting rig & Thin Red Light Lines
    private Transform lightingRigRoot;
    private Light topKeyLight;
    private Light bottomRimLight;
    private Light frontRimLight;
    private Light thrusterLight;
    private Light leftHeadlight;
    private Light rightHeadlight;
    private Light muzzleFlashLight;
    private float muzzleFlashIntensity;
    private Transform[] thrusterFlames;

    private static Material sharedRedLineMat;
    private static Material sharedWarpPortalMat;

    private static readonly Vector3[] EnemyMuzzleOffsets = new Vector3[]
    {
        new Vector3(-2.6f, -0.45f, 3.2f),
        new Vector3( 2.6f, -0.45f, 3.2f)
    };

    private void OnEnable()
    {
        if (!ActiveEnemies.Contains(this))
        {
            ActiveEnemies.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveEnemies.Remove(this);
    }

    private void OnDestroy()
    {
        ActiveEnemies.Remove(this);
        if (jumpspacePortalEffect != null)
        {
            Destroy(jumpspacePortalEffect);
        }
    }

    /// <summary>
    /// Initializes the enemy ship and triggers its Jumpspace / Hyperspace arrival sequence.
    /// </summary>
    public void InitializeWithJumpspace(Vector3 exitPosition, Quaternion exitRotation, int waveNumber, int unitIndex)
    {
        enemyCallsign = $"INTERCEPTOR W{waveNumber}-{unitIndex + 1:00}";
        maxHealth = 90f + (waveNumber - 1) * 15f;
        currentHealth = maxHealth;

        EnsureShipLightingRig();
        EnsureRedEnemyLightLines();

        jumpspaceExitPos = exitPosition;
        jumpspaceRotation = exitRotation;
        Vector3 forwardDir = exitRotation * Vector3.forward;

        // Start 210m further back in deep space for the hyperspace jump-in streak
        jumpspaceStartPos = exitPosition - forwardDir * 210f;
        transform.position = jumpspaceStartPos;
        transform.rotation = exitRotation;
        transform.localScale = new Vector3(0.28f, 0.28f, 7.8f);

        smoothHeading = exitRotation;
        currentSpeed = attackRunSpeed * 2.2f;

        isArrivingFromJumpspace = true;
        jumpspaceTimer = 0f;
        jumpspaceDuration = 0.88f;

        // Spawn Jumpspace Warp Portal flash at exit position
        SpawnJumpspacePortal(exitPosition, exitRotation);

        // Disable trail emission briefly during jump start so it doesn't draw from origin (0,0,0)
        TrailRenderer[] trails = GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            trails[i].Clear();
        }
    }

    private void Start()
    {
        EnsureShipLightingRig();
        EnsureRedEnemyLightLines();

        if (!isArrivingFromJumpspace)
        {
            smoothHeading = transform.rotation;
            currentSpeed = cruiseSpeed;
        }
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;

        float dt = Time.deltaTime;

        if (isArrivingFromJumpspace)
        {
            SimulateJumpspaceArrival(dt);
            UpdateDynamicLightingAndThrusters(dt, 1.0f);
            return;
        }

        SimulateCombatAI(dt);
        UpdateDynamicLightingAndThrusters(dt, 0.78f);
    }

    /// <summary>
    /// Animates the ship streaking in from hyperspace/jumpspace and snapping out of warp at the exit point.
    /// </summary>
    private void SimulateJumpspaceArrival(float dt)
    {
        jumpspaceTimer += dt;
        float t = Mathf.Clamp01(jumpspaceTimer / jumpspaceDuration);

        // Exponential ease-out deceleration from hyper-speed
        float posT = 1f - Mathf.Pow(1f - t, 3.8f);
        transform.position = Vector3.Lerp(jumpspaceStartPos, jumpspaceExitPos, posT);
        transform.rotation = jumpspaceRotation;

        // Stretch deformation snapping back to normal ship proportions (1, 1, 1)
        float scaleT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.15f) / 0.85f));
        Vector3 stretchedScale = new Vector3(0.28f, 0.28f, 7.8f);
        transform.localScale = Vector3.Lerp(stretchedScale, Vector3.one, scaleT);

        // Animate jumpspace portal ring
        if (jumpspacePortalEffect != null)
        {
            float portalScale = Mathf.Sin(t * Mathf.PI) * 14.5f;
            jumpspacePortalEffect.transform.localScale = new Vector3(portalScale, portalScale, 0.35f);
            jumpspacePortalEffect.transform.Rotate(0f, 0f, 360f * dt, Space.Self);
        }

        if (t >= 1f)
        {
            isArrivingFromJumpspace = false;
            transform.position = jumpspaceExitPos;
            transform.localScale = Vector3.one;
            smoothHeading = jumpspaceRotation;
            currentSpeed = attackRunSpeed;

            if (jumpspacePortalEffect != null)
            {
                Destroy(jumpspacePortalEffect);
            }

            // Spawn a brief exit shockwave flash when dropping out of jumpspace
            SpawnJumpspaceExitFlash(jumpspaceExitPos);
            nextFireTime = Time.time + Random.Range(0.4f, 1.2f);
        }
    }

    private void SpawnJumpspacePortal(Vector3 pos, Quaternion rot)
    {
        EnsureSharedMaterials();

        jumpspacePortalEffect = new GameObject("Jumpspace_WarpPortal");
        jumpspacePortalEffect.transform.position = pos;
        jumpspacePortalEffect.transform.rotation = rot;

        // Outer glowing warp disc
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "WarpRing";
        ring.transform.SetParent(jumpspacePortalEffect.transform, false);
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ring.transform.localScale = new Vector3(1f, 0.04f, 1f);
        Collider col = ring.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Renderer rend = ring.GetComponent<Renderer>();
        if (rend != null && sharedWarpPortalMat != null)
        {
            rend.sharedMaterial = sharedWarpPortalMat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }

        // Core red/cyan warp flash light
        Light warpLight = jumpspacePortalEffect.AddComponent<Light>();
        warpLight.type = LightType.Point;
        warpLight.color = new Color(1.0f, 0.25f, 0.45f);
        warpLight.intensity = 45f;
        warpLight.range = 42f;
        warpLight.shadows = LightShadows.None;
    }

    private void SpawnJumpspaceExitFlash(Vector3 pos)
    {
        EnsureSharedMaterials();

        GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flash.name = "Jumpspace_ExitFlash";
        flash.transform.position = pos;
        flash.transform.localScale = Vector3.one * 6.5f;
        Collider col = flash.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Renderer rend = flash.GetComponent<Renderer>();
        if (rend != null && sharedRedLineMat != null)
        {
            rend.sharedMaterial = sharedRedLineMat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Light flashLight = flash.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = new Color(1.0f, 0.2f, 0.25f);
        flashLight.intensity = 55f;
        flashLight.range = 48f;
        flashLight.shadows = LightShadows.None;

        Destroy(flash, 0.22f);
    }

    /// <summary>
    /// Simulates enemy dogfighting and station assault behavior.
    /// Primary objective: Attack Space Station, while dynamically engaging Player & Ally NPCs.
    /// </summary>
    private void SimulateCombatAI(float dt)
    {
        if (Time.time >= nextTargetEvalTime)
        {
            nextTargetEvalTime = Time.time + Random.Range(2.0f, 3.8f);
            EvaluateTacticalTarget();
        }

        Vector3 targetPos = GetCurrentTargetPosition();
        Vector3 toTarget = targetPos - transform.position;
        float distToTarget = toTarget.magnitude;

        // Check if we need to peel off / evade to avoid colliding with the station or target
        float minSafeDist = (currentTargetType == TargetType.Station) ? 34f : 18f;
        if (!isEvading && distToTarget < minSafeDist)
        {
            isEvading = true;
            evadeEndTime = Time.time + Random.Range(2.2f, 3.4f);
            Vector3 sideDir = (Random.value > 0.5f ? transform.right : -transform.right) + Vector3.up * Random.Range(-0.4f, 0.65f);
            evadeDirection = ((-toTarget.normalized * 0.6f) + sideDir.normalized * 0.8f).normalized;
        }

        if (isEvading && Time.time >= evadeEndTime)
        {
            isEvading = false;
        }

        Vector3 desiredDir = isEvading ? evadeDirection : (distToTarget > 0.01f ? toTarget / distToTarget : transform.forward);

        // Obstacle avoidance using SphereCast
        if (Physics.SphereCast(transform.position, 2.5f, transform.forward, out RaycastHit hit, 24f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.transform.IsChildOf(transform))
            {
                Vector3 avoidDir = Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized;
                if (avoidDir.sqrMagnitude < 0.01f) avoidDir = Vector3.up;
                desiredDir = Vector3.Slerp(desiredDir, (avoidDir + hit.normal * 0.7f + Vector3.up * 0.3f).normalized, 0.85f).normalized;
            }
        }

        // Smooth heading & aerodynamic wing banking
        Vector3 currentRight = smoothHeading * Vector3.right;
        float lateralTurn = Vector3.Dot(desiredDir, currentRight);

        float targetSpd = isEvading ? attackRunSpeed * 1.1f : Mathf.Lerp(attackRunSpeed, cruiseSpeed * 0.85f, Mathf.Clamp01(Mathf.Abs(lateralTurn)));
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpd, 22f * dt);

        if (desiredDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetHeading = Quaternion.LookRotation(desiredDir, Vector3.up);
            smoothHeading = Quaternion.Slerp(smoothHeading, targetHeading, 1f - Mathf.Exp(-turnResponsiveness * dt));
        }

        Vector3 cleanForward = smoothHeading * Vector3.forward;
        if (Mathf.Abs(Vector3.Dot(cleanForward, Vector3.up)) < 0.96f)
        {
            smoothHeading = Quaternion.LookRotation(cleanForward, Vector3.up);
        }

        float targetBank = -Mathf.Clamp(lateralTurn * 2.5f, -1f, 1f) * maxBankAngle;
        currentBankAngle = Mathf.Lerp(currentBankAngle, targetBank, 1f - Mathf.Exp(-bankSmoothSpeed * dt));

        Quaternion bankRot = Quaternion.AngleAxis(currentBankAngle, Vector3.forward);
        transform.rotation = smoothHeading * bankRot;
        transform.position += transform.forward * (currentSpeed * dt);

        // Weapon firing logic when aligned toward target
        if (!isEvading && distToTarget <= attackRange && distToTarget >= minSafeDist * 0.75f)
        {
            float aimDot = Vector3.Dot(transform.forward, toTarget.normalized);
            if (aimDot > 0.82f && Time.time >= nextFireTime)
            {
                nextFireTime = Time.time + (1f / Mathf.Max(0.5f, fireRate)) * Random.Range(0.85f, 1.2f);
                FireEnemyLaser(targetPos);
            }
        }
    }

    private void EvaluateTacticalTarget()
    {
        // Primary objective (60% priority): Attack the Space Station
        // Secondary objective (40% priority): Attack nearby Player or Ally NPCs
        SpaceshipController playerShip = SpaceshipController.Instance;
        float distToPlayer = float.MaxValue;
        if (playerShip != null)
        {
            Vector3 playerPos = playerShip.isPiloting
                ? playerShip.transform.position
                : (playerShip.characterPlayer != null ? playerShip.characterPlayer.transform.position : playerShip.transform.position);
            distToPlayer = Vector3.Distance(transform.position, playerPos);
        }

        SpaceshipNPC nearestAlly = SpaceshipNPC.FindNearestAlly(transform.position, out float distToAlly);

        float roll = Random.value;
        if (distToPlayer < 85f && roll < 0.38f)
        {
            currentTargetType = TargetType.Player;
        }
        else if (nearestAlly != null && distToAlly < 90f && roll < 0.65f)
        {
            currentTargetType = TargetType.AllyNPC;
            targetAlly = nearestAlly;
        }
        else
        {
            currentTargetType = TargetType.Station;
        }
    }

    private Vector3 GetCurrentTargetPosition()
    {
        if (currentTargetType == TargetType.Player && SpaceshipController.Instance != null)
        {
            SpaceshipController player = SpaceshipController.Instance;
            if (player.isPiloting)
            {
                return player.transform.position + player.CurrentVelocity * 0.25f;
            }
            if (player.characterPlayer != null)
            {
                return player.characterPlayer.transform.position + Vector3.up * 1.2f;
            }
        }
        else if (currentTargetType == TargetType.AllyNPC && targetAlly != null)
        {
            return targetAlly.transform.position + targetAlly.transform.forward * 4f;
        }

        if (GameManager.Instance != null)
        {
            return GameManager.Instance.GetStationTargetPosition();
        }

        return new Vector3(0f, 4f, 0f);
    }

    private void FireEnemyLaser(Vector3 targetPosition)
    {
        Vector3 offset = EnemyMuzzleOffsets[alternateMuzzle ? 0 : 1];
        alternateMuzzle = !alternateMuzzle;

        Vector3 muzzlePos = transform.TransformPoint(offset);
        Vector3 aimDir = (targetPosition - muzzlePos).normalized;
        // Blend between ship forward and exact target direction for fair accuracy
        Vector3 fireDir = Vector3.Slerp(transform.forward, aimDir, 0.65f).normalized;
        Quaternion boltRot = Quaternion.LookRotation(fireDir, Vector3.up);

        bool targetingStation = (currentTargetType == TargetType.Station);
        LaserBolt.SpawnBolt(
            muzzlePos,
            boltRot,
            laserSpeed + currentSpeed * 0.5f,
            2.4f,
            transform,
            LaserBolt.Faction.Enemy,
            laserDamage,
            targetingStation
        );

        muzzleFlashIntensity = 22f;
        if (muzzleFlashLight != null)
        {
            muzzleFlashLight.intensity = muzzleFlashIntensity;
        }
    }

    public void TakeDamage(float amount, Transform attacker = null)
    {
        if (currentHealth <= 0f) return;

        if (attacker != null)
        {
            lastAttacker = attacker;
        }

        currentHealth = Mathf.Max(0f, currentHealth - amount);

        // If attacked by player or ally, chance to retaliate and dogfight the attacker
        if (attacker != null && Random.value < 0.45f)
        {
            if (attacker.GetComponent<SpaceshipController>() != null)
            {
                currentTargetType = TargetType.Player;
            }
            else
            {
                SpaceshipNPC ally = attacker.GetComponent<SpaceshipNPC>();
                if (ally != null)
                {
                    currentTargetType = TargetType.AllyNPC;
                    targetAlly = ally;
                }
            }
        }

        if (currentHealth <= 0f)
        {
            ExplodeAndDestroy();
        }
    }

    private void ExplodeAndDestroy()
    {
        SpawnDestructionExplosion(transform.position);

        // Check if player dealt the fatal blow to trigger controller vibration
        bool killedByPlayer = false;
        if (lastAttacker != null)
        {
            if (lastAttacker.GetComponentInParent<SpaceshipController>() != null ||
                lastAttacker.GetComponentInParent<MovementInput>() != null ||
                (SpaceshipController.Instance != null && (lastAttacker == SpaceshipController.Instance.transform || lastAttacker == SpaceshipController.Instance.characterPlayer?.transform)))
            {
                killedByPlayer = true;
            }
        }
        else if (SpaceshipController.Instance != null && SpaceshipController.Instance.LockedEnemy == this)
        {
            killedByPlayer = true;
        }

        if (killedByPlayer)
        {
            SpaceshipController.TriggerGamepadRumble(0.95f, 1.0f, 0.45f);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnEnemyDestroyed(this);
        }

        Destroy(gameObject);
    }

    private void SpawnDestructionExplosion(Vector3 pos)
    {
        SpaceshipExplosion.Spawn(pos, 1.15f);
    }

    /// <summary>
    /// Creates the exact same lighting rig as the Player and Ally NPC ships.
    /// </summary>
    public void EnsureShipLightingRig()
    {
        if (!enableShipLighting) return;

        Transform existingRig = transform.Find("Spaceship_LightingRig");
        if (existingRig != null)
        {
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

        // 1. Top Key Light (Identical to Player & NPC)
        topKeyLight = GetOrCreateLight(
            "Hull_TopKeyLight",
            LightType.Point,
            new Vector3(0f, 5.5f, 0.5f),
            Quaternion.identity,
            new Color(0.92f, 0.96f, 1.0f),
            hullKeyLightIntensity,
            28f
        );

        // 2. Front Nose Fill Light (Identical to Player & NPC)
        frontRimLight = GetOrCreateLight(
            "Hull_FrontFillLight",
            LightType.Point,
            new Vector3(0f, 2.2f, 5.5f),
            Quaternion.identity,
            new Color(0.75f, 0.88f, 1.0f),
            hullRimLightIntensity * 1.1f,
            22f
        );

        // 3. Bottom Rim Light (Identical to Player & NPC)
        bottomRimLight = GetOrCreateLight(
            "Hull_BottomRimLight",
            LightType.Point,
            new Vector3(0f, -3.8f, -0.5f),
            Quaternion.identity,
            new Color(0.25f, 0.7f, 1.0f),
            hullRimLightIntensity,
            24f
        );

        // 4. Thruster Engine Glow Light
        thrusterLight = GetOrCreateLight(
            "Thruster_EngineGlow",
            LightType.Point,
            new Vector3(0f, -0.15f, -5.2f),
            Quaternion.identity,
            new Color(0.15f, 0.75f, 1.0f),
            thrusterBaseIntensity,
            18f
        );

        // 5. Dual Forward Headlights
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

        // 6. Weapon Muzzle Flash Light
        muzzleFlashLight = GetOrCreateLight(
            "Weapon_MuzzleFlashLight",
            LightType.Point,
            new Vector3(0f, -0.5f, 3.5f),
            Quaternion.identity,
            new Color(1.0f, 0.15f, 0.2f),
            0f,
            20f
        );

        EnsureThrusterFlames();
    }

    /// <summary>
    /// Adds thin glowing red light lines along the enemy ship's wings and hull + thin red wingtip trails
    /// so the player can immediately identify enemy ships in space.
    /// </summary>
    public void EnsureRedEnemyLightLines()
    {
        EnsureSharedMaterials();

        Transform redLinesRoot = transform.Find("Enemy_RedLightLines");
        if (redLinesRoot != null) return;

        GameObject rootObj = new GameObject("Enemy_RedLightLines");
        rootObj.transform.SetParent(transform, false);
        rootObj.transform.localPosition = Vector3.zero;
        rootObj.transform.localRotation = Quaternion.identity;
        redLinesRoot = rootObj.transform;

        // 1. Thin Red Wing Contour Line (Left Wing -> Nose -> Right Wing)
        CreateThinRedLineRenderer(
            redLinesRoot,
            "RedWingLine_Front",
            new Vector3[]
            {
                new Vector3(-4.3f, -0.15f, -1.2f),
                new Vector3(-2.4f,  0.05f,  0.8f),
                new Vector3( 0.0f,  0.30f,  4.4f),
                new Vector3( 2.4f,  0.05f,  0.8f),
                new Vector3( 4.3f, -0.15f, -1.2f)
            },
            0.09f
        );

        // 2. Thin Red Dorsal Spine & Tail Accent Line
        CreateThinRedLineRenderer(
            redLinesRoot,
            "RedHullSpine_Line",
            new Vector3[]
            {
                new Vector3(0.0f, 0.35f,  4.2f),
                new Vector3(0.0f, 0.95f,  1.2f),
                new Vector3(0.0f, 0.85f, -1.8f),
                new Vector3(0.0f, 0.45f, -4.6f)
            },
            0.08f
        );

        // 3. Thin Red Rear Wing Trailing Edge Line
        CreateThinRedLineRenderer(
            redLinesRoot,
            "RedWingLine_Rear",
            new Vector3[]
            {
                new Vector3(-4.3f, -0.15f, -1.2f),
                new Vector3(-1.8f,  0.10f, -3.8f),
                new Vector3( 1.8f,  0.10f, -3.8f),
                new Vector3( 4.3f, -0.15f, -1.2f)
            },
            0.075f
        );

        // 4. Dual Thin Red Wingtip Light Trails (leaves thin red light ribbons in space during maneuvers)
        CreateThinRedWingtipTrail(redLinesRoot, "RedWingtipTrail_Left", new Vector3(-4.25f, -0.15f, -1.3f));
        CreateThinRedWingtipTrail(redLinesRoot, "RedWingtipTrail_Right", new Vector3(4.25f, -0.15f, -1.3f));

        // 5. Subtle Red Identification Accent Light
        GameObject redAccentObj = new GameObject("Enemy_RedAccentLight");
        redAccentObj.transform.SetParent(redLinesRoot, false);
        redAccentObj.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        Light redAccent = redAccentObj.AddComponent<Light>();
        redAccent.type = LightType.Point;
        redAccent.color = new Color(1.0f, 0.08f, 0.12f);
        redAccent.intensity = 7.5f;
        redAccent.range = 16f;
        redAccent.shadows = LightShadows.None;
    }

    private void CreateThinRedLineRenderer(Transform parent, string name, Vector3[] localPoints, float width)
    {
        GameObject lineObj = new GameObject(name);
        lineObj.transform.SetParent(parent, false);

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = localPoints.Length;
        lr.SetPositions(localPoints);
        lr.startWidth = width;
        lr.endWidth = width;
        lr.numCapVertices = 3;
        lr.numCornerVertices = 3;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        if (sharedRedLineMat != null)
        {
            lr.sharedMaterial = sharedRedLineMat;
        }
        lr.startColor = new Color(1.0f, 0.08f, 0.12f, 1f);
        lr.endColor = new Color(1.0f, 0.08f, 0.12f, 1f);
    }

    private void CreateThinRedWingtipTrail(Transform parent, string name, Vector3 localPos)
    {
        GameObject trailObj = new GameObject(name);
        trailObj.transform.SetParent(parent, false);
        trailObj.transform.localPosition = localPos;

        TrailRenderer tr = trailObj.AddComponent<TrailRenderer>();
        tr.time = 0.65f;
        tr.minVertexDistance = 0.25f;
        tr.startWidth = 0.085f;
        tr.endWidth = 0.0f;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;
        if (sharedRedLineMat != null)
        {
            tr.sharedMaterial = sharedRedLineMat;
        }
        tr.startColor = new Color(1.0f, 0.10f, 0.14f, 0.95f);
        tr.endColor = new Color(1.0f, 0.05f, 0.08f, 0.0f);
    }

    private static void EnsureSharedMaterials()
    {
        if (sharedRedLineMat != null && sharedWarpPortalMat != null) return;

        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");

        if (unlitShader != null)
        {
            sharedRedLineMat = new Material(unlitShader);
            Color crimson = new Color(1.0f, 0.10f, 0.14f, 1f);
            sharedRedLineMat.color = crimson;
            if (sharedRedLineMat.HasProperty("_BaseColor"))
                sharedRedLineMat.SetColor("_BaseColor", crimson);

            sharedWarpPortalMat = new Material(unlitShader);
            Color warpColor = new Color(0.45f, 0.85f, 1.0f, 1f);
            sharedWarpPortalMat.color = warpColor;
            if (sharedWarpPortalMat.HasProperty("_BaseColor"))
                sharedWarpPortalMat.SetColor("_BaseColor", warpColor);
        }
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
            new Vector3(0f, -0.22f, -4.55f),
            new Vector3(0.92f, -0.42f, -4.45f),
            new Vector3(-0.92f, -0.42f, -4.45f)
        };
        float[] nozzleScales = new float[] { 0.36f, 0.28f, 0.28f };

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

    private void UpdateDynamicLightingAndThrusters(float dt, float thrustPower)
    {
        if (topKeyLight != null) topKeyLight.intensity = hullKeyLightIntensity;
        if (bottomRimLight != null) bottomRimLight.intensity = hullRimLightIntensity;
        if (frontRimLight != null) frontRimLight.intensity = hullRimLightIntensity * 1.1f;

        float flicker = 1f + Mathf.Sin(Time.time * 42f + transform.position.x * 0.5f) * 0.08f;

        if (thrusterLight != null)
        {
            thrusterLight.intensity = Mathf.Lerp(thrusterBaseIntensity, thrusterBoostIntensity, thrustPower) * flicker;
            thrusterLight.color = Color.Lerp(new Color(0.15f, 0.65f, 1.0f), new Color(0.55f, 0.92f, 1.0f), thrustPower);
        }

        if (thrusterFlames != null)
        {
            float[] baseScales = new float[] { 0.36f, 0.28f, 0.28f };
            for (int i = 0; i < thrusterFlames.Length; i++)
            {
                if (thrusterFlames[i] == null) continue;
                float s = baseScales[i] * Mathf.Lerp(0.65f, 1.35f, thrustPower);
                float lengthZ = s * Mathf.Lerp(1.4f, 4.2f, thrustPower) * flicker;
                thrusterFlames[i].localScale = new Vector3(s, s, lengthZ);
            }
        }

        if (muzzleFlashLight != null && muzzleFlashIntensity > 0.01f)
        {
            muzzleFlashIntensity = Mathf.MoveTowards(muzzleFlashIntensity, 0f, 160f * dt);
            muzzleFlashLight.intensity = muzzleFlashIntensity;
        }
    }

    public static EnemySpaceship FindEnemyHitBySegment(Vector3 segStart, Vector3 segEnd, float hitRadius)
    {
        float radiusSq = hitRadius * hitRadius;
        for (int i = 0; i < ActiveEnemies.Count; i++)
        {
            EnemySpaceship enemy = ActiveEnemies[i];
            if (enemy == null || enemy.currentHealth <= 0f) continue;

            float distSq = LaserBolt.DistancePointToSegmentSq(enemy.transform.position, segStart, segEnd);
            if (distSq <= radiusSq)
            {
                return enemy;
            }
        }
        return null;
    }

    public static EnemySpaceship FindNearestEnemy(Vector3 fromPos, out float minDistance)
    {
        minDistance = float.MaxValue;
        EnemySpaceship best = null;
        for (int i = 0; i < ActiveEnemies.Count; i++)
        {
            EnemySpaceship enemy = ActiveEnemies[i];
            if (enemy == null || enemy.currentHealth <= 0f) continue;
            float d = Vector3.Distance(fromPos, enemy.transform.position);
            if (d < minDistance)
            {
                minDistance = d;
                best = enemy;
            }
        }
        return best;
    }
}
