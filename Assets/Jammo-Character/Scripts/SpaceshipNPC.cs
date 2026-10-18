using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI Controller and lighting rig for ally/NPC spaceships (StarSparrow29) guarding the space station.
/// Features:
/// - Smooth 3D orbital guard maneuvers when the sector is clear.
/// - Active Interceptor Combat Mode: pursues and shoots cyan plasma lasers at invading enemy ships (StarSparrow17) during waves.
/// - Identical lighting rig and dynamic engine thrusters as the player's ship.
/// </summary>
[ExecuteAlways]
public class SpaceshipNPC : MonoBehaviour
{
    public static readonly List<SpaceshipNPC> ActiveAllies = new List<SpaceshipNPC>();

    [Header("NPC Guard Patrol & Combat Settings")]
    [Tooltip("Index of the NPC ship unit (0 or 1) to determine unique patrol trajectories.")]
    public int allyUnitIndex = 0;
    [Tooltip("Cruise speed when patrolling around the station.")]
    public float patrolCruiseSpeed = 28f;
    [Tooltip("Combat intercept speed when pursuing enemies.")]
    public float combatInterceptSpeed = 38f;
    [Tooltip("Orbit radius along X-axis around the station.")]
    public float patrolRadiusX = 55f;
    [Tooltip("Orbit radius along Z-axis around the station.")]
    public float patrolRadiusZ = 68f;
    [Tooltip("Vertical wave amplitude for dynamic 3D undulating flight.")]
    public float verticalWaveAmplitude = 8.5f;
    [Tooltip("Steering responsiveness when tracking patrol waypoints.")]
    public float turnResponsiveness = 2.6f;
    [Tooltip("Maximum aerodynamic wing banking/roll angle when turning.")]
    public float maxBankAngle = 42f;
    [Tooltip("Smooth speed for roll transitions.")]
    public float bankSmoothSpeed = 5.0f;
    [Tooltip("Rate of fire when shooting at enemy ships.")]
    public float combatFireRate = 3.2f;
    [Tooltip("Damage per laser bolt fired by Ally NPC.")]
    public float combatLaserDamage = 14f;

    [Header("Ally Shield / Hull")]
    public float maxShieldHealth = 200f;
    public float currentShieldHealth = 200f;

    [Header("Ship Lighting Rig (Identical to Player Spaceship)")]
    [Tooltip("Enable automatic lighting rig identical to the player's ship.")]
    public bool enableShipLighting = true;
    [Tooltip("Key light intensity illuminating the upper hull and cockpit.")]
    public float hullKeyLightIntensity = 18f;
    [Tooltip("Fill/rim light intensity under the hull and wings.")]
    public float hullRimLightIntensity = 10f;
    [Tooltip("Base thruster engine glow intensity.")]
    public float thrusterBaseIntensity = 8f;
    [Tooltip("Boost thruster glow intensity during acceleration.")]
    public float thrusterBoostIntensity = 35f;

    // Internal patrol & combat state
    private Vector3 patrolAnchorPosition;
    private bool anchorRecorded = false;
    private float orbitPhaseAngle;
    private float currentBankAngle;
    private float currentSpeed;
    private Quaternion smoothHeading;
    private float nextFireTime;
    private bool alternateMuzzle;

    // Lighting rig references
    private Transform lightingRigRoot;
    private Light topKeyLight;
    private Light bottomRimLight;
    private Light frontRimLight;
    private Light thrusterLight;
    private Light leftHeadlight;
    private Light rightHeadlight;
    private Transform[] thrusterFlames;

    private static readonly Vector3[] AllyMuzzleOffsets = new Vector3[]
    {
        new Vector3(-3.2f, -1.2f, 2.5f),
        new Vector3( 3.2f, -1.2f, 2.5f)
    };

    private void OnEnable()
    {
        if (!ActiveAllies.Contains(this))
        {
            ActiveAllies.Add(this);
        }

        EnsureShipLightingRig();
        if (Application.isPlaying)
        {
            InitializePatrolState();
        }
    }

    private void OnDisable()
    {
        ActiveAllies.Remove(this);
    }

    private void OnDestroy()
    {
        ActiveAllies.Remove(this);
    }

    private void Start()
    {
        EnsureShipLightingRig();
        if (!Application.isPlaying) return;

        InitializePatrolState();
    }

    private void InitializePatrolState()
    {
        if (!anchorRecorded)
        {
            patrolAnchorPosition = transform.position;
            anchorRecorded = true;
        }

        Vector3 flatFwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatFwd.sqrMagnitude < 0.001f) flatFwd = Vector3.forward;
        smoothHeading = Quaternion.LookRotation(flatFwd.normalized, Vector3.up);

        orbitPhaseAngle = (allyUnitIndex % 2 == 0) ? 0f : Mathf.PI;
        currentSpeed = patrolCruiseSpeed;
        currentShieldHealth = maxShieldHealth;
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

        if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;

        float dt = Time.deltaTime;

        // Auto-regenerate ally shields slowly over time
        if (currentShieldHealth < maxShieldHealth)
        {
            currentShieldHealth = Mathf.Min(maxShieldHealth, currentShieldHealth + 6f * dt);
        }

        // Engage invading enemies if any are active, otherwise patrol station
        if (EnemySpaceship.ActiveEnemies.Count > 0)
        {
            SimulateCombatInterceptManeuver(dt);
        }
        else
        {
            SimulateStationGuardManeuver(dt);
        }

        UpdateDynamicLightingAndThrusters(dt);
    }

    /// <summary>
    /// Actively intercepts and dogfights invading enemy ships to help the player defend the station.
    /// </summary>
    private void SimulateCombatInterceptManeuver(float dt)
    {
        EnemySpaceship targetEnemy = SelectTargetEnemy();
        if (targetEnemy == null)
        {
            SimulateStationGuardManeuver(dt);
            return;
        }

        Vector3 toEnemy = targetEnemy.transform.position - transform.position;
        float dist = toEnemy.magnitude;

        Vector3 desiredDir;
        if (dist < 20f)
        {
            // Peel off slightly when too close so we don't ram the enemy
            desiredDir = (transform.forward + Vector3.up * 0.45f + transform.right * (allyUnitIndex % 2 == 0 ? 0.6f : -0.6f)).normalized;
        }
        else
        {
            desiredDir = toEnemy.normalized;
        }

        // Obstacle avoidance against station structure
        if (Physics.SphereCast(transform.position, 2.8f, transform.forward, out RaycastHit hit, 26f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.transform.IsChildOf(transform))
            {
                Vector3 avoidDir = Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized;
                if (avoidDir.sqrMagnitude < 0.01f) avoidDir = Vector3.up;
                desiredDir = Vector3.Slerp(desiredDir, (avoidDir + hit.normal * 0.65f + Vector3.up * 0.35f).normalized, 0.85f).normalized;
            }
        }

        Vector3 currentRight = smoothHeading * Vector3.right;
        float lateralTurnAmount = Vector3.Dot(desiredDir, currentRight);

        float turnIntensity = Mathf.Clamp01(Mathf.Abs(lateralTurnAmount) * 1.8f);
        float targetSpd = Mathf.Lerp(combatInterceptSpeed, patrolCruiseSpeed * 0.88f, turnIntensity);
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpd, 22f * dt);

        if (desiredDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetHeading = Quaternion.LookRotation(desiredDir, Vector3.up);
            smoothHeading = Quaternion.Slerp(smoothHeading, targetHeading, 1f - Mathf.Exp(-(turnResponsiveness * 1.35f) * dt));
        }

        Vector3 cleanForward = smoothHeading * Vector3.forward;
        if (Mathf.Abs(Vector3.Dot(cleanForward, Vector3.up)) < 0.96f)
        {
            smoothHeading = Quaternion.LookRotation(cleanForward, Vector3.up);
        }

        float targetBank = -Mathf.Clamp(lateralTurnAmount * 2.4f, -1f, 1f) * maxBankAngle;
        currentBankAngle = Mathf.Lerp(currentBankAngle, targetBank, 1f - Mathf.Exp(-bankSmoothSpeed * dt));

        Quaternion bankRot = Quaternion.AngleAxis(currentBankAngle, Vector3.forward);
        transform.rotation = smoothHeading * bankRot;
        transform.position += transform.forward * (currentSpeed * dt);

        // Fire cyan plasma lasers at the enemy when in range & cone
        if (dist <= 145f && dist >= 16f)
        {
            float aimDot = Vector3.Dot(transform.forward, toEnemy.normalized);
            if (aimDot > 0.80f && Time.time >= nextFireTime)
            {
                nextFireTime = Time.time + (1f / Mathf.Max(0.5f, combatFireRate)) * Random.Range(0.85f, 1.15f);
                FireAllyLaser(targetEnemy.transform.position);
            }
        }
    }

    private EnemySpaceship SelectTargetEnemy()
    {
        if (EnemySpaceship.ActiveEnemies.Count == 0) return null;

        // Ally 0 prefers nearest enemy, Ally 1 prefers second or opposite index so they split targets
        int count = EnemySpaceship.ActiveEnemies.Count;
        int preferredIdx = (allyUnitIndex % count);
        EnemySpaceship candidate = EnemySpaceship.ActiveEnemies[preferredIdx];
        if (candidate != null && candidate.currentHealth > 0f)
        {
            return candidate;
        }

        return EnemySpaceship.FindNearestEnemy(transform.position, out _);
    }

    private void FireAllyLaser(Vector3 enemyPosition)
    {
        Vector3 offset = AllyMuzzleOffsets[alternateMuzzle ? 0 : 1];
        alternateMuzzle = !alternateMuzzle;

        Vector3 muzzlePos = transform.TransformPoint(offset);
        Vector3 toEnemy = (enemyPosition - muzzlePos).normalized;
        Vector3 fireDir = Vector3.Slerp(transform.forward, toEnemy, 0.72f).normalized;
        Quaternion boltRot = Quaternion.LookRotation(fireDir, Vector3.up);

        LaserBolt.SpawnBolt(
            muzzlePos,
            boltRot,
            220f + currentSpeed,
            2.1f,
            transform,
            LaserBolt.Faction.Friendly,
            combatLaserDamage,
            false
        );
    }

    public void TakeDamage(float amount)
    {
        currentShieldHealth = Mathf.Max(20f, currentShieldHealth - amount);
    }

    /// <summary>
    /// Simulates AI station patrol with 3D maneuvers, wing banking, and obstacle avoidance.
    /// </summary>
    private void SimulateStationGuardManeuver(float dt)
    {
        if (!anchorRecorded)
        {
            patrolAnchorPosition = transform.position;
            anchorRecorded = true;
        }

        float angularRate = (patrolCruiseSpeed / Mathf.Max(25f, (patrolRadiusX + patrolRadiusZ) * 0.5f));
        float directionSign = (allyUnitIndex % 2 == 0) ? 1f : -1f;
        orbitPhaseAngle += angularRate * directionSign * dt;

        float lookAheadPhase = orbitPhaseAngle + directionSign * 0.48f;
        Vector3 targetWaypoint = EvaluatePatrolPoint(lookAheadPhase);

        Vector3 toTarget = targetWaypoint - transform.position;
        if (toTarget.sqrMagnitude < 1f)
        {
            toTarget = transform.forward * 10f;
        }

        Vector3 desiredDir = toTarget.normalized;

        // Obstacle avoidance
        if (Physics.SphereCast(transform.position, 2.8f, transform.forward, out RaycastHit hit, 26f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.transform.IsChildOf(transform))
            {
                Vector3 avoidDir = Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized;
                if (avoidDir.sqrMagnitude < 0.01f) avoidDir = Vector3.up;
                desiredDir = Vector3.Slerp(desiredDir, (avoidDir + hit.normal * 0.65f + Vector3.up * 0.35f).normalized, 0.85f).normalized;
            }
        }

        Vector3 currentRight = smoothHeading * Vector3.right;
        float lateralTurnAmount = Vector3.Dot(desiredDir, currentRight);

        float turnIntensity = Mathf.Clamp01(Mathf.Abs(lateralTurnAmount) * 1.8f);
        float targetSpeed = Mathf.Lerp(patrolCruiseSpeed * 1.15f, patrolCruiseSpeed * 0.82f, turnIntensity);
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, 18f * dt);

        Quaternion targetHeading = Quaternion.LookRotation(desiredDir, Vector3.up);
        smoothHeading = Quaternion.Slerp(smoothHeading, targetHeading, 1f - Mathf.Exp(-turnResponsiveness * dt));

        Vector3 cleanForward = smoothHeading * Vector3.forward;
        if (Mathf.Abs(Vector3.Dot(cleanForward, Vector3.up)) < 0.96f)
        {
            smoothHeading = Quaternion.LookRotation(cleanForward, Vector3.up);
        }

        // Aerodynamic banking
        float targetBank = -Mathf.Clamp(lateralTurnAmount * 2.4f, -1f, 1f) * maxBankAngle;
        currentBankAngle = Mathf.Lerp(currentBankAngle, targetBank, 1f - Mathf.Exp(-bankSmoothSpeed * dt));

        Quaternion bankRot = Quaternion.AngleAxis(currentBankAngle, Vector3.forward);
        transform.rotation = smoothHeading * bankRot;

        transform.position += transform.forward * (currentSpeed * dt);
    }

    private Vector3 EvaluatePatrolPoint(float phase)
    {
        float offsetX = Mathf.Cos(phase) * patrolRadiusX;
        float offsetZ = Mathf.Sin(phase) * patrolRadiusZ;
        float offsetY = Mathf.Sin(phase * 2.0f + allyUnitIndex * 1.5f) * verticalWaveAmplitude;

        if (allyUnitIndex % 2 == 1)
        {
            offsetX = Mathf.Sin(phase) * patrolRadiusX;
            offsetZ = Mathf.Sin(phase * 2.0f) * (patrolRadiusZ * 0.75f);
        }

        return patrolAnchorPosition + new Vector3(offsetX, offsetY, offsetZ);
    }

    /// <summary>
    /// Creates and configures the ship lighting rig.
    /// </summary>
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
            new Vector3(0f, 5.5f, 0.5f),
            Quaternion.identity,
            new Color(0.92f, 0.96f, 1.0f),
            hullKeyLightIntensity,
            28f
        );

        // 2. Front Nose Fill Light
        frontRimLight = GetOrCreateLight(
            "Hull_FrontFillLight",
            LightType.Point,
            new Vector3(0f, 2.2f, 5.5f),
            Quaternion.identity,
            new Color(0.75f, 0.88f, 1.0f),
            hullRimLightIntensity * 1.1f,
            22f
        );

        // 3. Bottom Cyan Rim Light
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
            new Vector3(0f, -0.15f, -5.3f),
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
            new Vector3(0f, -0.28f, -4.85f),
            new Vector3(0.97f, -0.50f, -4.75f),
            new Vector3(-0.97f, -0.50f, -4.75f)
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

    private void UpdateDynamicLightingAndThrusters(float dt)
    {
        if (topKeyLight != null) topKeyLight.intensity = hullKeyLightIntensity;
        if (bottomRimLight != null) bottomRimLight.intensity = hullRimLightIntensity;
        if (frontRimLight != null) frontRimLight.intensity = hullRimLightIntensity * 1.1f;

        float thrustFactor = (EnemySpaceship.ActiveEnemies.Count > 0) ? 0.88f : (0.65f + Mathf.Sin(Time.time * 2.5f + allyUnitIndex) * 0.2f);
        float flicker = 1f + Mathf.Sin(Time.time * 40f + allyUnitIndex * 3f) * 0.08f;

        if (thrusterLight != null)
        {
            thrusterLight.intensity = Mathf.Lerp(thrusterBaseIntensity, thrusterBoostIntensity, thrustFactor) * flicker;
            thrusterLight.color = Color.Lerp(new Color(0.15f, 0.65f, 1.0f), new Color(0.55f, 0.92f, 1.0f), thrustFactor);
        }

        if (thrusterFlames != null)
        {
            float[] baseScales = new float[] { 0.36f, 0.28f, 0.28f };
            for (int i = 0; i < thrusterFlames.Length; i++)
            {
                if (thrusterFlames[i] == null) continue;
                float s = baseScales[i] * Mathf.Lerp(0.65f, 1.35f, thrustFactor);
                float lengthZ = s * Mathf.Lerp(1.4f, 4.2f, thrustFactor) * flicker;
                thrusterFlames[i].localScale = new Vector3(s, s, lengthZ);
            }
        }
    }

    public static SpaceshipNPC FindAllyHitBySegment(Vector3 segStart, Vector3 segEnd, float hitRadius)
    {
        float radiusSq = hitRadius * hitRadius;
        for (int i = 0; i < ActiveAllies.Count; i++)
        {
            SpaceshipNPC ally = ActiveAllies[i];
            if (ally == null) continue;
            float distSq = LaserBolt.DistancePointToSegmentSq(ally.transform.position, segStart, segEnd);
            if (distSq <= radiusSq)
            {
                return ally;
            }
        }
        return null;
    }

    public static SpaceshipNPC FindNearestAlly(Vector3 fromPos, out float minDistance)
    {
        minDistance = float.MaxValue;
        SpaceshipNPC best = null;
        for (int i = 0; i < ActiveAllies.Count; i++)
        {
            SpaceshipNPC ally = ActiveAllies[i];
            if (ally == null) continue;
            float d = Vector3.Distance(fromPos, ally.transform.position);
            if (d < minDistance)
            {
                minDistance = d;
                best = ally;
            }
        }
        return best;
    }

    /// <summary>
    /// Finds all NPC ships in the scene and ensures they have SpaceshipNPC and lighting rigs attached.
    /// </summary>
    public static void EnsureAllAllyNPCsSetup()
    {
        Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        List<GameObject> allyShips = new List<GameObject>();

        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform t = allTransforms[i];
            if (t != null && t.parent == null && (t.name.StartsWith("StarSparrow29") || t.name.StartsWith("SpaceshipNPC")))
            {
                allyShips.Add(t.gameObject);
            }
        }

#if UNITY_EDITOR
        if (!Application.isPlaying && allyShips.Count < 2)
        {
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.name == "GamePlay")
            {
                GameObject prefab29 = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/StarSparrow/Prefabs/ModularExamples/StarSparrow29.prefab");
                if (prefab29 != null)
                {
                    Vector3[] defaultSpawnPositions = new Vector3[]
                    {
                        new Vector3(-42f, 12f, -28f),
                        new Vector3(38f, 16f, 34f)
                    };
                    Quaternion[] defaultSpawnRotations = new Quaternion[]
                    {
                        Quaternion.Euler(0f, 35f, 0f),
                        Quaternion.Euler(0f, -145f, 0f)
                    };

                    while (allyShips.Count < 2)
                    {
                        int idx = allyShips.Count;
                        GameObject instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab29, activeScene);
                        if (instance != null)
                        {
                            instance.name = idx == 0 ? "StarSparrow29" : $"StarSparrow29 ({idx})";
                            instance.transform.position = defaultSpawnPositions[idx % 2];
                            instance.transform.rotation = defaultSpawnRotations[idx % 2];
                            allyShips.Add(instance);
                            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(activeScene);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
        }
#endif

        for (int i = 0; i < allyShips.Count; i++)
        {
            GameObject npcShip = allyShips[i];
            if (npcShip == null) continue;

            SpaceshipController playerCtrl = npcShip.GetComponent<SpaceshipController>();
            if (playerCtrl != null)
            {
                if (Application.isPlaying) Destroy(playerCtrl);
                else DestroyImmediate(playerCtrl);
            }

            SpaceshipNPC npcCtrl = npcShip.GetComponent<SpaceshipNPC>();
            if (npcCtrl == null)
            {
                npcCtrl = npcShip.AddComponent<SpaceshipNPC>();
            }
            npcCtrl.allyUnitIndex = i;
            npcCtrl.EnsureShipLightingRig();
        }
    }
}
