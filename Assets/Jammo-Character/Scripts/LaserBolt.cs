using UnityEngine;

/// <summary>
/// Laser bolt projectile fired by Player, Ally NPC, or Enemy spaceships.
/// Supports team-based hit detection, customizable plasma colors, dynamic point lighting, and impact flashes.
/// </summary>
public class LaserBolt : MonoBehaviour
{
    public enum Faction
    {
        Friendly,
        Enemy
    }

    public float speed = 240f;
    public float lifetime = 2.2f;
    public float damage = 18f;
    public Transform ownerRoot;
    public Faction faction = Faction.Friendly;
    public bool damagesStationOnHit = false;

    private float spawnTime;
    private Color boltGlowColor;

    private static Material sharedFriendlyCoreMat;
    private static Material sharedFriendlyGlowMat;
    private static Material sharedEnemyCoreMat;
    private static Material sharedEnemyGlowMat;

    public static LaserBolt SpawnBolt(
        Vector3 position,
        Quaternion rotation,
        float boltSpeed,
        float boltLifetime,
        Transform owner,
        Faction boltFaction = Faction.Friendly,
        float boltDamage = 18f,
        bool canDamageStation = false)
    {
        GameObject boltObj = new GameObject(boltFaction == Faction.Enemy ? "Enemy_LaserBolt" : "Spaceship_LaserBolt");
        boltObj.transform.position = position;
        boltObj.transform.rotation = rotation;

        LaserBolt bolt = boltObj.AddComponent<LaserBolt>();
        bolt.speed = boltSpeed;
        bolt.lifetime = boltLifetime;
        bolt.ownerRoot = owner;
        bolt.faction = boltFaction;
        bolt.damage = boltDamage;
        bolt.damagesStationOnHit = canDamageStation;
        bolt.BuildVisuals();

        return bolt;
    }

    private void Awake()
    {
        spawnTime = Time.time;
    }

    private void BuildVisuals()
    {
        EnsureMaterials();

        bool isEnemy = (faction == Faction.Enemy);
        Material coreMat = isEnemy ? sharedEnemyCoreMat : sharedFriendlyCoreMat;
        Material glowMat = isEnemy ? sharedEnemyGlowMat : sharedFriendlyGlowMat;
        boltGlowColor = isEnemy ? new Color(1.0f, 0.15f, 0.18f) : new Color(0.2f, 0.8f, 1.0f);

        // 1. Plasma core
        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.name = "Bolt_Core";
        core.transform.SetParent(transform, false);
        core.transform.localPosition = Vector3.zero;
        core.transform.localScale = new Vector3(0.18f, 0.18f, 3.2f);
        Collider coreCol = core.GetComponent<Collider>();
        if (coreCol != null) Destroy(coreCol);

        Renderer coreRend = core.GetComponent<Renderer>();
        if (coreRend != null && coreMat != null)
        {
            coreRend.sharedMaterial = coreMat;
            coreRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            coreRend.receiveShadows = false;
        }

        // 2. Plasma glow aura
        GameObject aura = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        aura.name = "Bolt_Aura";
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = Vector3.zero;
        aura.transform.localScale = new Vector3(0.36f, 0.36f, 4.0f);
        Collider auraCol = aura.GetComponent<Collider>();
        if (auraCol != null) Destroy(auraCol);

        Renderer auraRend = aura.GetComponent<Renderer>();
        if (auraRend != null && glowMat != null)
        {
            auraRend.sharedMaterial = glowMat;
            auraRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            auraRend.receiveShadows = false;
        }

        // 3. Point light illuminating nearby surroundings
        Light boltLight = gameObject.AddComponent<Light>();
        boltLight.type = LightType.Point;
        boltLight.color = boltGlowColor;
        boltLight.intensity = 9f;
        boltLight.range = 14f;
        boltLight.shadows = LightShadows.None;
    }

    private static void EnsureMaterials()
    {
        if (sharedFriendlyCoreMat != null && sharedEnemyCoreMat != null) return;

        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");

        if (unlitShader != null)
        {
            // Friendly (Cyan/Blue)
            sharedFriendlyCoreMat = new Material(unlitShader);
            Color fCoreCol = new Color(0.85f, 0.98f, 1.0f, 1f);
            sharedFriendlyCoreMat.color = fCoreCol;
            if (sharedFriendlyCoreMat.HasProperty("_BaseColor"))
                sharedFriendlyCoreMat.SetColor("_BaseColor", fCoreCol);

            sharedFriendlyGlowMat = new Material(unlitShader);
            Color fGlowCol = new Color(0.12f, 0.68f, 1.0f, 1f);
            sharedFriendlyGlowMat.color = fGlowCol;
            if (sharedFriendlyGlowMat.HasProperty("_BaseColor"))
                sharedFriendlyGlowMat.SetColor("_BaseColor", fGlowCol);

            // Enemy (Crimson Red)
            sharedEnemyCoreMat = new Material(unlitShader);
            Color eCoreCol = new Color(1.0f, 0.82f, 0.82f, 1f);
            sharedEnemyCoreMat.color = eCoreCol;
            if (sharedEnemyCoreMat.HasProperty("_BaseColor"))
                sharedEnemyCoreMat.SetColor("_BaseColor", eCoreCol);

            sharedEnemyGlowMat = new Material(unlitShader);
            Color eGlowCol = new Color(1.0f, 0.12f, 0.15f, 1f);
            sharedEnemyGlowMat.color = eGlowCol;
            if (sharedEnemyGlowMat.HasProperty("_BaseColor"))
                sharedEnemyGlowMat.SetColor("_BaseColor", eGlowCol);
        }
    }

    private void Update()
    {
        if (Time.time - spawnTime >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        float dt = Time.deltaTime;
        float moveDist = speed * dt;
        Vector3 nextPos = transform.position + transform.forward * moveDist;

        // 1. Proximity check against ships (generous hitbox for high-speed space dogfighting)
        if (faction == Faction.Friendly)
        {
            EnemySpaceship hitEnemy = EnemySpaceship.FindEnemyHitBySegment(transform.position, nextPos, 3.6f);
            if (hitEnemy != null)
            {
                hitEnemy.TakeDamage(damage, ownerRoot);
                SpawnImpactFlash(hitEnemy.transform.position, -transform.forward);
                Destroy(gameObject);
                return;
            }
        }
        else if (faction == Faction.Enemy)
        {
            // Check hit against Player Spaceship or Character
            SpaceshipController playerShip = SpaceshipController.Instance;
            if (playerShip != null)
            {
                if (playerShip.isPiloting)
                {
                    float distSq = DistancePointToSegmentSq(playerShip.transform.position, transform.position, nextPos);
                    if (distSq <= 4.2f * 4.2f)
                    {
                        playerShip.TakeShipDamage(damage);
                        SpawnImpactFlash(nextPos, -transform.forward);
                        Destroy(gameObject);
                        return;
                    }
                }
                else if (playerShip.characterPlayer != null)
                {
                    Vector3 charPos = playerShip.characterPlayer.transform.position + Vector3.up * 1.0f;
                    float distSq = DistancePointToSegmentSq(charPos, transform.position, nextPos);
                    if (distSq <= 2.5f * 2.5f)
                    {
                        playerShip.TakeCharacterDamage(damage);
                        SpawnImpactFlash(nextPos, -transform.forward);
                        Destroy(gameObject);
                        return;
                    }
                }
            }

            // Check hit against Ally NPC ships
            SpaceshipNPC hitAlly = SpaceshipNPC.FindAllyHitBySegment(transform.position, nextPos, 3.8f);
            if (hitAlly != null)
            {
                hitAlly.TakeDamage(damage);
                SpawnImpactFlash(hitAlly.transform.position, -transform.forward);
                Destroy(gameObject);
                return;
            }
        }

        // 2. Physics SphereCast against colliders (station, asteroids, ship colliders)
        float castRadius = faction == Faction.Friendly ? 0.55f : 0.45f;
        if (Physics.SphereCast(transform.position, castRadius, transform.forward, out RaycastHit hit, moveDist + 0.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (ownerRoot == null || !hit.collider.transform.IsChildOf(ownerRoot))
            {
                HandleColliderHit(hit);
                SpawnImpactFlash(hit.point, hit.normal);
                Destroy(gameObject);
                return;
            }
        }

        // 3. Check if an enemy bolt aimed at the station reached the station core area
        if (faction == Faction.Enemy && damagesStationOnHit && GameManager.Instance != null)
        {
            Vector3 stationCenter = GameManager.Instance.GetStationTargetPosition();
            float distSq = DistancePointToSegmentSq(stationCenter, transform.position, nextPos);
            if (distSq <= 16f * 16f)
            {
                GameManager.Instance.DamageStation(damage);
                SpawnImpactFlash(nextPos, -transform.forward);
                Destroy(gameObject);
                return;
            }
        }

        transform.position = nextPos;
    }

    private void HandleColliderHit(RaycastHit hit)
    {
        Transform hitTrans = hit.collider.transform;

        if (faction == Faction.Friendly)
        {
            EnemySpaceship enemy = hitTrans.GetComponentInParent<EnemySpaceship>();
            if (enemy != null)
            {
                enemy.TakeDamage(damage, ownerRoot);
            }
        }
        else if (faction == Faction.Enemy)
        {
            SpaceshipController playerShip = hitTrans.GetComponentInParent<SpaceshipController>();
            if (playerShip != null)
            {
                playerShip.TakeShipDamage(damage);
                return;
            }

            MovementInput playerChar = hitTrans.GetComponentInParent<MovementInput>();
            if (playerChar != null && SpaceshipController.Instance != null)
            {
                SpaceshipController.Instance.TakeCharacterDamage(damage);
                return;
            }

            SpaceshipNPC allyNpc = hitTrans.GetComponentInParent<SpaceshipNPC>();
            if (allyNpc != null)
            {
                allyNpc.TakeDamage(damage);
                return;
            }

            // Otherwise, if enemy hit station structure, apply station damage
            if (GameManager.Instance != null)
            {
                float distToStation = Vector3.Distance(hit.point, GameManager.Instance.GetStationTargetPosition());
                if (distToStation < 110f)
                {
                    GameManager.Instance.DamageStation(damage);
                }
            }
        }
    }

    public static float DistancePointToSegmentSq(Vector3 point, Vector3 segA, Vector3 segB)
    {
        Vector3 ab = segB - segA;
        float abLenSq = ab.sqrMagnitude;
        if (abLenSq < 0.0001f) return (point - segA).sqrMagnitude;
        float t = Mathf.Clamp01(Vector3.Dot(point - segA, ab) / abLenSq);
        Vector3 projection = segA + ab * t;
        return (point - projection).sqrMagnitude;
    }

    private void SpawnImpactFlash(Vector3 hitPoint, Vector3 hitNormal)
    {
        EnsureMaterials();

        Material glowMat = (faction == Faction.Enemy) ? sharedEnemyGlowMat : sharedFriendlyGlowMat;

        GameObject flashObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flashObj.name = "Laser_ImpactFlash";
        flashObj.transform.position = hitPoint + hitNormal * 0.15f;
        flashObj.transform.localScale = Vector3.one * 1.55f;

        Collider col = flashObj.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Renderer rend = flashObj.GetComponent<Renderer>();
        if (rend != null && glowMat != null)
        {
            rend.sharedMaterial = glowMat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }

        Light flashLight = flashObj.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = boltGlowColor;
        flashLight.intensity = 22f;
        flashLight.range = 16f;
        flashLight.shadows = LightShadows.None;

        Destroy(flashObj, 0.18f);
    }
}
