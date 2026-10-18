using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// High-performance pooled LaserBolt projectile fired by Player, Ally NPC, or Enemy spaceships.
/// Uses object pooling and shared unlit materials/meshes for zero GC allocation and high FPS during combat.
/// </summary>
public class LaserBolt : MonoBehaviour
{
    public enum Faction
    {
        Friendly,
        Enemy
    }

    public float speed = 240f;
    public float lifetime = 2.0f;
    public float damage = 18f;
    public Transform ownerRoot;
    public Faction faction = Faction.Friendly;
    public bool damagesStationOnHit = false;

    private float spawnTime;
    private int frameCounter;
    private Renderer coreRenderer;
    private Renderer auraRenderer;

    private static readonly Stack<LaserBolt> boltPool = new Stack<LaserBolt>(64);
    private static Mesh cachedSphereMesh;
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
        EnsureSharedAssets();

        LaserBolt bolt = null;
        while (boltPool.Count > 0 && bolt == null)
        {
            bolt = boltPool.Pop();
        }

        if (bolt == null)
        {
            GameObject boltObj = new GameObject("Pooled_LaserBolt");
            bolt = boltObj.AddComponent<LaserBolt>();
            bolt.BuildVisualHierarchy();
        }

        bolt.transform.SetPositionAndRotation(position, rotation);
        bolt.speed = boltSpeed;
        bolt.lifetime = boltLifetime;
        bolt.ownerRoot = owner;
        bolt.faction = boltFaction;
        bolt.damage = boltDamage;
        bolt.damagesStationOnHit = canDamageStation;
        bolt.spawnTime = Time.time;
        bolt.frameCounter = Random.Range(0, 2);
        bolt.ApplyFactionMaterials();
        bolt.gameObject.SetActive(true);

        return bolt;
    }

    private void BuildVisualHierarchy()
    {
        EnsureSharedAssets();

        // 1. Plasma core mesh
        GameObject core = new GameObject("Bolt_Core");
        core.transform.SetParent(transform, false);
        core.transform.localPosition = Vector3.zero;
        core.transform.localScale = new Vector3(0.18f, 0.18f, 3.2f);
        MeshFilter coreMf = core.AddComponent<MeshFilter>();
        coreMf.sharedMesh = cachedSphereMesh;
        coreRenderer = core.AddComponent<MeshRenderer>();
        coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        coreRenderer.receiveShadows = false;

        // 2. Plasma glow aura mesh
        GameObject aura = new GameObject("Bolt_Aura");
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = Vector3.zero;
        aura.transform.localScale = new Vector3(0.36f, 0.36f, 4.0f);
        MeshFilter auraMf = aura.AddComponent<MeshFilter>();
        auraMf.sharedMesh = cachedSphereMesh;
        auraRenderer = aura.AddComponent<MeshRenderer>();
        auraRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        auraRenderer.receiveShadows = false;
    }

    private void ApplyFactionMaterials()
    {
        bool isEnemy = (faction == Faction.Enemy);
        if (coreRenderer != null)
        {
            coreRenderer.sharedMaterial = isEnemy ? sharedEnemyCoreMat : sharedFriendlyCoreMat;
        }
        if (auraRenderer != null)
        {
            auraRenderer.sharedMaterial = isEnemy ? sharedEnemyGlowMat : sharedFriendlyGlowMat;
        }
    }

    private static void EnsureSharedAssets()
    {
        if (cachedSphereMesh == null)
        {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            MeshFilter mf = temp.GetComponent<MeshFilter>();
            if (mf != null) cachedSphereMesh = mf.sharedMesh;
            if (Application.isPlaying) Destroy(temp);
            else DestroyImmediate(temp);
        }

        if (sharedFriendlyCoreMat != null && sharedEnemyCoreMat != null) return;

        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");

        if (unlitShader != null)
        {
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

    private void ReturnToPool()
    {
        gameObject.SetActive(false);
        boltPool.Push(this);
    }

    private void Update()
    {
        if (Time.time - spawnTime >= lifetime)
        {
            ReturnToPool();
            return;
        }

        float dt = Time.deltaTime;
        float moveDist = speed * dt;
        Vector3 currentPos = transform.position;
        Vector3 nextPos = currentPos + transform.forward * moveDist;

        // 1. Fast math proximity check against ships (zero physics overhead)
        if (faction == Faction.Friendly)
        {
            EnemySpaceship hitEnemy = EnemySpaceship.FindEnemyHitBySegment(currentPos, nextPos, 3.8f);
            if (hitEnemy != null)
            {
                hitEnemy.TakeDamage(damage, ownerRoot);
                ReturnToPool();
                return;
            }
        }
        else
        {
            SpaceshipController playerShip = SpaceshipController.Instance;
            if (playerShip != null)
            {
                if (playerShip.isPiloting)
                {
                    float distSq = DistancePointToSegmentSq(playerShip.transform.position, currentPos, nextPos);
                    if (distSq <= 4.2f * 4.2f)
                    {
                        playerShip.TakeShipDamage(damage);
                        ReturnToPool();
                        return;
                    }
                }
                else if (playerShip.characterPlayer != null)
                {
                    Vector3 charPos = playerShip.characterPlayer.transform.position + Vector3.up * 1.0f;
                    float distSq = DistancePointToSegmentSq(charPos, currentPos, nextPos);
                    if (distSq <= 2.5f * 2.5f)
                    {
                        playerShip.TakeCharacterDamage(damage);
                        ReturnToPool();
                        return;
                    }
                }
            }

            SpaceshipNPC hitAlly = SpaceshipNPC.FindAllyHitBySegment(currentPos, nextPos, 3.8f);
            if (hitAlly != null)
            {
                hitAlly.TakeDamage(damage);
                ReturnToPool();
                return;
            }

            if (damagesStationOnHit && GameManager.Instance != null)
            {
                Vector3 stationCenter = GameManager.Instance.GetStationTargetPosition();
                float distSq = DistancePointToSegmentSq(stationCenter, currentPos, nextPos);
                if (distSq <= 16f * 16f)
                {
                    GameManager.Instance.DamageStation(damage);
                    ReturnToPool();
                    return;
                }
            }
        }

        // 2. Throttled raycast against station/environment geometry (every 2nd frame to cut physics cost by 50%)
        frameCounter++;
        if ((frameCounter & 1) == 0)
        {
            if (Physics.Raycast(currentPos, transform.forward, out RaycastHit hit, moveDist * 2.0f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (ownerRoot == null || !hit.collider.transform.IsChildOf(ownerRoot))
                {
                    HandleColliderHit(hit);
                    ReturnToPool();
                    return;
                }
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
        else
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

            if (GameManager.Instance != null)
            {
                float distToStationSq = (hit.point - GameManager.Instance.GetStationTargetPosition()).sqrMagnitude;
                if (distToStationSq < 110f * 110f)
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
}
