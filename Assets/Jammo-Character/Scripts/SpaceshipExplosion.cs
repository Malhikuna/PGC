using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Multi-stage cinematic spaceship explosion effect when any ship or station is destroyed.
/// Includes:
/// - Blinding white-hot plasma core flash transitioning to orange/crimson fire.
/// - Dual expanding 3D shockwave rings.
/// - Multi-layered rolling secondary fireballs & smoke puffs.
/// - 18 high-velocity flaming hull debris & plasma sparks with glowing TrailRenderers.
/// - Dynamic high-intensity blast point light & distance-based camera shake impulse.
/// </summary>
public class SpaceshipExplosion : MonoBehaviour
{
    private struct FireballNode
    {
        public Transform transform;
        public Renderer renderer;
        public Vector3 velocity;
        public float startScale;
        public float peakScale;
        public float delay;
        public float duration;
    }

    private struct DebrisSparkNode
    {
        public Transform transform;
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public float drag;
    }

    private float elapsed;
    private float totalDuration = 1.45f;
    private float scaleMultiplier = 1.0f;

    private Transform coreFlashTransform;
    private Renderer coreFlashRenderer;
    private Transform primaryRingTransform;
    private Renderer primaryRingRenderer;
    private Transform secondaryRingTransform;
    private Renderer secondaryRingRenderer;
    private Light explosionLight;

    private readonly List<FireballNode> fireballs = new List<FireballNode>();
    private readonly List<DebrisSparkNode> debrisSparks = new List<DebrisSparkNode>();

    private static Shader cachedUnlitShader;
    private static Material sharedSparkTrailMat;
    private MaterialPropertyBlock propBlock;

    /// <summary>
    /// Spawns a complete multi-layered spaceship explosion at the given world position.
    /// </summary>
    public static SpaceshipExplosion Spawn(Vector3 position, float scale = 1.0f)
    {
        GameObject root = new GameObject("Spaceship_ExplosionFX");
        root.transform.position = position;

        SpaceshipExplosion fx = root.AddComponent<SpaceshipExplosion>();
        fx.scaleMultiplier = Mathf.Max(0.25f, scale);
        fx.BuildExplosionLayers();

        // Trigger distance-based camera shake if ThirdPersonCamera is present
        if (Camera.main != null)
        {
            ThirdPersonCamera tpCam = Camera.main.GetComponent<ThirdPersonCamera>();
            if (tpCam != null)
            {
                float dist = Vector3.Distance(Camera.main.transform.position, position);
                float atten = Mathf.Clamp01(1f - (dist / 160f));
                if (atten > 0.01f)
                {
                    tpCam.TriggerExplosionShake(atten * 1.45f * scale);
                }
            }
        }

        return fx;
    }

    private void BuildExplosionLayers()
    {
        propBlock = new MaterialPropertyBlock();

        if (cachedUnlitShader == null)
        {
            cachedUnlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (cachedUnlitShader == null) cachedUnlitShader = Shader.Find("Unlit/Color");
        }

        if (sharedSparkTrailMat == null && cachedUnlitShader != null)
        {
            sharedSparkTrailMat = new Material(cachedUnlitShader);
            Color c = new Color(1.0f, 0.55f, 0.12f, 1f);
            sharedSparkTrailMat.color = c;
            if (sharedSparkTrailMat.HasProperty("_BaseColor"))
                sharedSparkTrailMat.SetColor("_BaseColor", c);
        }

        // 1. White-Hot Plasma Core Flash Sphere
        GameObject coreObj = CreateUnlitPrimitive(PrimitiveType.Sphere, "Blast_CoreFlash", new Color(1f, 0.96f, 0.82f, 1f));
        coreFlashTransform = coreObj.transform;
        coreFlashTransform.localScale = Vector3.one * (1.5f * scaleMultiplier);
        coreFlashRenderer = coreObj.GetComponent<Renderer>();

        // 2. Primary Expanding Shockwave Ring
        GameObject ring1 = CreateUnlitPrimitive(PrimitiveType.Cylinder, "Blast_ShockwaveRing_1", new Color(1f, 0.65f, 0.20f, 1f));
        primaryRingTransform = ring1.transform;
        primaryRingTransform.localRotation = Quaternion.Euler(Random.Range(-18f, 18f), Random.Range(0f, 360f), Random.Range(-18f, 18f));
        primaryRingTransform.localScale = new Vector3(1.2f, 0.05f, 1.2f) * scaleMultiplier;
        primaryRingRenderer = ring1.GetComponent<Renderer>();

        // 3. Secondary Tilted Shockwave Ring
        GameObject ring2 = CreateUnlitPrimitive(PrimitiveType.Cylinder, "Blast_ShockwaveRing_2", new Color(1f, 0.25f, 0.15f, 1f));
        secondaryRingTransform = ring2.transform;
        secondaryRingTransform.localRotation = Quaternion.Euler(Random.Range(45f, 135f), Random.Range(0f, 360f), Random.Range(0f, 180f));
        secondaryRingTransform.localScale = new Vector3(0.8f, 0.04f, 0.8f) * scaleMultiplier;
        secondaryRingRenderer = ring2.GetComponent<Renderer>();

        // 4. Rolling Secondary Fireballs (10 spheres expanding & cooling from yellow-orange to crimson-smoke)
        int fireballCount = 10;
        for (int i = 0; i < fireballCount; i++)
        {
            GameObject fbObj = CreateUnlitPrimitive(PrimitiveType.Sphere, "Fireball_" + i, new Color(1f, 0.55f, 0.12f, 1f));
            Vector3 randomDir = Random.onUnitSphere;
            fbObj.transform.localPosition = randomDir * (Random.Range(0.4f, 2.6f) * scaleMultiplier);
            float startS = Random.Range(1.2f, 2.4f) * scaleMultiplier;
            fbObj.transform.localScale = Vector3.one * startS;

            FireballNode node = new FireballNode
            {
                transform = fbObj.transform,
                renderer = fbObj.GetComponent<Renderer>(),
                velocity = randomDir * (Random.Range(4.5f, 14.0f) * scaleMultiplier),
                startScale = startS,
                peakScale = Random.Range(5.5f, 10.5f) * scaleMultiplier,
                delay = Random.Range(0.0f, 0.16f),
                duration = Random.Range(0.55f, 1.15f)
            };
            fireballs.Add(node);
        }

        // 5. High-Velocity Flaming Shrapnel Debris & Plasma Sparks with Glowing Fire Trails
        int sparkCount = 18;
        for (int i = 0; i < sparkCount; i++)
        {
            PrimitiveType prim = (i % 3 == 0) ? PrimitiveType.Cube : PrimitiveType.Sphere;
            GameObject sparkObj = CreateUnlitPrimitive(prim, "Shrapnel_Spark_" + i, new Color(1f, 0.72f, 0.22f, 1f));
            Vector3 dir = Random.onUnitSphere;
            sparkObj.transform.localPosition = dir * (0.6f * scaleMultiplier);
            sparkObj.transform.localRotation = Random.rotation;

            float pieceScale = (prim == PrimitiveType.Cube ? Random.Range(0.28f, 0.58f) : Random.Range(0.18f, 0.34f)) * scaleMultiplier;
            sparkObj.transform.localScale = new Vector3(pieceScale, pieceScale * 0.65f, pieceScale * 1.4f);

            TrailRenderer tr = sparkObj.AddComponent<TrailRenderer>();
            tr.time = Random.Range(0.28f, 0.55f);
            tr.minVertexDistance = 0.15f;
            tr.startWidth = 0.24f * scaleMultiplier;
            tr.endWidth = 0.0f;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            if (sharedSparkTrailMat != null)
            {
                tr.sharedMaterial = sharedSparkTrailMat;
            }
            tr.startColor = new Color(1.0f, 0.68f, 0.18f, 1.0f);
            tr.endColor = new Color(1.0f, 0.12f, 0.05f, 0.0f);

            DebrisSparkNode spark = new DebrisSparkNode
            {
                transform = sparkObj.transform,
                velocity = dir * (Random.Range(24f, 58f) * scaleMultiplier),
                angularVelocity = Random.insideUnitSphere * 540f,
                drag = Random.Range(1.8f, 3.4f)
            };
            debrisSparks.Add(spark);
        }

        // 6. High-Intensity Dynamic Explosion Point Light
        explosionLight = gameObject.AddComponent<Light>();
        explosionLight.type = LightType.Point;
        explosionLight.color = new Color(1.0f, 0.58f, 0.20f);
        explosionLight.intensity = 95f * scaleMultiplier;
        explosionLight.range = 68f * scaleMultiplier;
        explosionLight.shadows = LightShadows.None;
    }

    private GameObject CreateUnlitPrimitive(PrimitiveType type, string objName, Color initialColor)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = objName;
        obj.transform.SetParent(transform, false);

        Collider col = obj.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Renderer rend = obj.GetComponent<Renderer>();
        if (rend != null && cachedUnlitShader != null)
        {
            Material mat = new Material(cachedUnlitShader);
            mat.color = initialColor;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", initialColor);
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }

        return obj;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        elapsed += dt;

        if (elapsed >= totalDuration)
        {
            Destroy(gameObject);
            return;
        }

        float normTime = Mathf.Clamp01(elapsed / totalDuration);

        // 1. Animate Core Flash (expands fast in first 0.25s, then shrinks)
        if (coreFlashTransform != null)
        {
            float coreT = Mathf.Clamp01(elapsed / 0.42f);
            if (coreT < 1f)
            {
                float s = Mathf.Sin(coreT * Mathf.PI) * 12.5f * scaleMultiplier;
                coreFlashTransform.localScale = Vector3.one * Mathf.Max(0.01f, s);
                Color c = Color.Lerp(new Color(1f, 0.98f, 0.88f, 1f), new Color(1f, 0.25f, 0.08f, 1f), coreT);
                SetRendererColor(coreFlashRenderer, c);
            }
            else if (coreFlashTransform.gameObject.activeSelf)
            {
                coreFlashTransform.gameObject.SetActive(false);
            }
        }

        // 2. Animate Expanding Shockwave Rings
        if (primaryRingTransform != null)
        {
            float ringT = Mathf.Clamp01(elapsed / 0.55f);
            if (ringT < 1f)
            {
                float radius = Mathf.Lerp(1.5f, 36.0f, 1f - Mathf.Pow(1f - ringT, 3f)) * scaleMultiplier;
                float thickness = Mathf.Lerp(0.08f, 0.002f, ringT) * scaleMultiplier;
                primaryRingTransform.localScale = new Vector3(radius, thickness, radius);
                Color ringCol = Color.Lerp(new Color(1f, 0.85f, 0.4f, 1f), new Color(1f, 0.15f, 0.05f, 0f), ringT);
                SetRendererColor(primaryRingRenderer, ringCol);
            }
            else if (primaryRingTransform.gameObject.activeSelf)
            {
                primaryRingTransform.gameObject.SetActive(false);
            }
        }

        if (secondaryRingTransform != null)
        {
            float ring2T = Mathf.Clamp01(elapsed / 0.45f);
            if (ring2T < 1f)
            {
                float radius = Mathf.Lerp(1.0f, 28.0f, 1f - Mathf.Pow(1f - ring2T, 3f)) * scaleMultiplier;
                float thickness = Mathf.Lerp(0.06f, 0.002f, ring2T) * scaleMultiplier;
                secondaryRingTransform.localScale = new Vector3(radius, thickness, radius);
            }
            else if (secondaryRingTransform.gameObject.activeSelf)
            {
                secondaryRingTransform.gameObject.SetActive(false);
            }
        }

        // 3. Animate Rolling Fireball Clouds
        for (int i = 0; i < fireballs.Count; i++)
        {
            FireballNode fb = fireballs[i];
            if (fb.transform == null) continue;

            if (elapsed < fb.delay)
            {
                fb.transform.localScale = Vector3.zero;
                continue;
            }

            float localT = (elapsed - fb.delay) / fb.duration;
            if (localT >= 1f)
            {
                if (fb.transform.gameObject.activeSelf)
                    fb.transform.gameObject.SetActive(false);
                continue;
            }

            fb.transform.localPosition += fb.velocity * dt;
            fb.velocity *= (1f - 2.2f * dt);

            // Expand fast to peakScale then shrink slightly at the end as smoke dissipates
            float scaleCurve = localT < 0.45f
                ? Mathf.Lerp(fb.startScale, fb.peakScale, localT / 0.45f)
                : Mathf.Lerp(fb.peakScale, 0.05f, (localT - 0.45f) / 0.55f);

            fb.transform.localScale = Vector3.one * Mathf.Max(0.01f, scaleCurve);

            // Color gradient: Bright Yellow-Orange -> Deep Crimson -> Dark Smoke
            Color fbColor;
            if (localT < 0.35f)
            {
                fbColor = Color.Lerp(new Color(1f, 0.85f, 0.25f, 1f), new Color(1f, 0.32f, 0.08f, 1f), localT / 0.35f);
            }
            else
            {
                fbColor = Color.Lerp(new Color(1f, 0.32f, 0.08f, 1f), new Color(0.14f, 0.06f, 0.06f, 1f), (localT - 0.35f) / 0.65f);
            }
            SetRendererColor(fb.renderer, fbColor);
        }

        // 4. Animate Flying Shrapnel Debris & Sparks
        for (int i = 0; i < debrisSparks.Count; i++)
        {
            DebrisSparkNode spark = debrisSparks[i];
            if (spark.transform == null) continue;

            spark.transform.localPosition += spark.velocity * dt;
            spark.velocity = Vector3.Lerp(spark.velocity, Vector3.zero, spark.drag * dt);
            spark.transform.Rotate(spark.angularVelocity * dt, Space.Self);
            spark.transform.localScale *= (1f - 0.85f * dt);

            debrisSparks[i] = spark;
        }

        // 5. Animate Explosion Point Light
        if (explosionLight != null)
        {
            float lightDecay = Mathf.Pow(1f - normTime, 2.2f);
            float flicker = 1f + Mathf.Sin(Time.time * 65f) * 0.15f;
            explosionLight.intensity = 95f * scaleMultiplier * lightDecay * flicker;
            explosionLight.color = Color.Lerp(new Color(1.0f, 0.72f, 0.28f), new Color(1.0f, 0.18f, 0.08f), normTime);
        }
    }

    private void SetRendererColor(Renderer rend, Color c)
    {
        if (rend == null) return;
        rend.GetPropertyBlock(propBlock);
        propBlock.SetColor("_Color", c);
        propBlock.SetColor("_BaseColor", c);
        rend.SetPropertyBlock(propBlock);
    }
}
