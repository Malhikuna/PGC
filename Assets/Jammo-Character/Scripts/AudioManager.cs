using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Central AudioManager for handling SFX (blaster-shot) and switching between
/// Space Station interior audio and Outer Space ambience (space.mp3) when entering/exiting Spaceship Mode.
/// </summary>
[ExecuteAlways]
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Clips (Assets/Audio)")]
    [Tooltip("Laser blaster firing sound effect (Assets/Audio/blaster-shot.mp3).")]
    public AudioClip blasterShotClip;
    [Tooltip("Outer space background ambience when in spaceship mode (Assets/Audio/space.mp3).")]
    public AudioClip spaceAmbienceClip;

    [Header("Volume Settings")]
    [Range(0f, 1f)]
    public float playerBlasterVolume = 0.72f;
    [Range(0f, 1f)]
    public float npcBlasterVolume = 0.28f;
    [Range(0f, 1f)]
    public float spaceAmbienceVolume = 0.80f;

    private AudioSource sfxSource;
    private AudioSource spaceAmbienceSource;

    private struct StationAudioState
    {
        public AudioSource source;
        public bool wasMuted;
        public bool wasPlaying;
        public float originalVolume;
    }

    private readonly List<StationAudioState> cachedStationSources = new List<StationAudioState>();
    private bool isSpaceshipAudioActive = false;
    private float lastPlayerShotSfxTime = -10f;
    private float lastNpcShotSfxTime = -10f;

    private void OnEnable()
    {
        Instance = this;
        EnsureAudioClipsLoaded();
        EnsureAudioSources();
    }

    private void Awake()
    {
        Instance = this;
        EnsureAudioClipsLoaded();
        EnsureAudioSources();
    }

    private void Start()
    {
        EnsureAudioClipsLoaded();
        EnsureAudioSources();

        if (!Application.isPlaying) return;

        bool piloting = (SpaceshipController.Instance != null && SpaceshipController.Instance.isPiloting);
        SetSpaceshipAudioMode(piloting, forceRefresh: true);
    }

    public void EnsureAudioClipsLoaded()
    {
        if (blasterShotClip == null)
        {
            blasterShotClip = Resources.Load<AudioClip>("blaster-shot");
        }
        if (spaceAmbienceClip == null)
        {
            spaceAmbienceClip = Resources.Load<AudioClip>("space");
        }

#if UNITY_EDITOR
        if (blasterShotClip == null)
        {
            blasterShotClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/blaster-shot.mp3");
        }
        if (spaceAmbienceClip == null)
        {
            spaceAmbienceClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/space.mp3");
        }
#endif
    }

    private void EnsureAudioSources()
    {
        Transform sfxTrans = transform.Find("SFX_BlasterSource");
        if (sfxTrans != null)
        {
            sfxSource = sfxTrans.GetComponent<AudioSource>();
        }
        if (sfxSource == null)
        {
            GameObject sfxObj = sfxTrans != null ? sfxTrans.gameObject : new GameObject("SFX_BlasterSource");
            sfxObj.transform.SetParent(transform, false);
            sfxSource = sfxObj.GetComponent<AudioSource>();
            if (sfxSource == null) sfxSource = sfxObj.AddComponent<AudioSource>();
        }
        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f;

        Transform spaceTrans = transform.Find("Ambience_SpaceSource");
        if (spaceTrans != null)
        {
            spaceAmbienceSource = spaceTrans.GetComponent<AudioSource>();
        }
        if (spaceAmbienceSource == null)
        {
            GameObject spaceObj = spaceTrans != null ? spaceTrans.gameObject : new GameObject("Ambience_SpaceSource");
            spaceObj.transform.SetParent(transform, false);
            spaceAmbienceSource = spaceObj.GetComponent<AudioSource>();
            if (spaceAmbienceSource == null) spaceAmbienceSource = spaceObj.AddComponent<AudioSource>();
        }
        spaceAmbienceSource.playOnAwake = false;
        spaceAmbienceSource.loop = true;
        spaceAmbienceSource.spatialBlend = 0f;
        spaceAmbienceSource.volume = spaceAmbienceVolume;
        if (spaceAmbienceClip != null && spaceAmbienceSource.clip != spaceAmbienceClip)
        {
            spaceAmbienceSource.clip = spaceAmbienceClip;
        }
    }

    /// <summary>
    /// Plays the blaster-shot.mp3 sound effect when firing weapons.
    /// </summary>
    public void PlayBlasterShot(bool isPlayer = true, Vector3 worldPos = default)
    {
        if (!Application.isPlaying) return;

        EnsureAudioClipsLoaded();
        if (blasterShotClip == null) return;
        EnsureAudioSources();

        float now = Time.unscaledTime;

        if (isPlayer)
        {
            if (now - lastPlayerShotSfxTime < 0.045f) return;
            lastPlayerShotSfxTime = now;

            sfxSource.pitch = Random.Range(0.95f, 1.05f);
            sfxSource.PlayOneShot(blasterShotClip, playerBlasterVolume);
        }
        else
        {
            // Throttle NPC/Enemy blaster shots to prevent audio channel spam & lag
            if (now - lastNpcShotSfxTime < 0.14f) return;

            Camera cam = Camera.main;
            if (cam != null)
            {
                float dist = Vector3.Distance(cam.transform.position, worldPos);
                if (dist > 110f) return;

                float attenuation = Mathf.Clamp01(1f - (dist / 110f));
                lastNpcShotSfxTime = now;
                sfxSource.pitch = Random.Range(0.88f, 1.12f);
                sfxSource.PlayOneShot(blasterShotClip, npcBlasterVolume * attenuation);
            }
        }
    }

    /// <summary>
    /// Switches between Space Station interior audio (On-Foot Mode) and Outer Space background audio (Spaceship Mode).
    /// When inSpaceshipMode is true: mutes all station AudioSources and plays Assets/Audio/space.mp3.
    /// When inSpaceshipMode is false: stops space.mp3 and restores station AudioSources.
    /// </summary>
    public void SetSpaceshipAudioMode(bool inSpaceshipMode, bool forceRefresh = false)
    {
        if (!Application.isPlaying) return;
        if (!forceRefresh && isSpaceshipAudioActive == inSpaceshipMode) return;

        isSpaceshipAudioActive = inSpaceshipMode;
        EnsureAudioClipsLoaded();
        EnsureAudioSources();

        if (inSpaceshipMode)
        {
            // Cache and mute all station/environment AudioSources in the scene
            cachedStationSources.Clear();
            AudioSource[] allSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);

            for (int i = 0; i < allSources.Length; i++)
            {
                AudioSource src = allSources[i];
                if (src == null) continue;

                // Skip AudioManager's own sources and GameManager's UI AudioSource
                if (src == sfxSource || src == spaceAmbienceSource || src.transform.IsChildOf(transform))
                    continue;
                if (GameManager.Instance != null && src.gameObject == GameManager.Instance.gameObject)
                    continue;

                StationAudioState state = new StationAudioState
                {
                    source = src,
                    wasMuted = src.mute,
                    wasPlaying = src.isPlaying,
                    originalVolume = src.volume
                };
                cachedStationSources.Add(state);

                src.mute = true;
                if (src.isPlaying && src.loop)
                {
                    src.Pause();
                }
            }

            // Play outer space background ambience (Assets/Audio/space.mp3)
            if (spaceAmbienceSource != null && spaceAmbienceClip != null)
            {
                spaceAmbienceSource.clip = spaceAmbienceClip;
                spaceAmbienceSource.volume = spaceAmbienceVolume;
                spaceAmbienceSource.mute = false;
                if (!spaceAmbienceSource.isPlaying)
                {
                    spaceAmbienceSource.Play();
                }
            }
        }
        else
        {
            // Stop outer space ambience
            if (spaceAmbienceSource != null && spaceAmbienceSource.isPlaying)
            {
                spaceAmbienceSource.Stop();
            }

            // Restore all station AudioSources
            for (int i = 0; i < cachedStationSources.Count; i++)
            {
                StationAudioState state = cachedStationSources[i];
                if (state.source != null)
                {
                    state.source.mute = state.wasMuted;
                    state.source.volume = state.originalVolume;
                    if (state.wasPlaying && !state.source.isPlaying)
                    {
                        state.source.UnPause();
                        if (!state.source.isPlaying)
                        {
                            state.source.Play();
                        }
                    }
                }
            }
            cachedStationSources.Clear();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void EnsureAudioManagerInScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.name != "GamePlay" && !string.IsNullOrEmpty(activeScene.name))
        {
            return;
        }

        AudioManager existing = FindFirstObjectByType<AudioManager>();
        if (existing == null)
        {
            GameObject amObj = GameObject.Find("AudioManager");
            if (amObj == null)
            {
                amObj = new GameObject("AudioManager");
            }
            existing = amObj.GetComponent<AudioManager>();
            if (existing == null)
            {
                existing = amObj.AddComponent<AudioManager>();
            }
#if UNITY_EDITOR
            if (!Application.isPlaying && activeScene.IsValid())
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(activeScene);
            }
#endif
        }

        existing.EnsureAudioClipsLoaded();
        existing.EnsureAudioSources();
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void EditorAutoSetupAudioManager()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating) return;
            EnsureAudioManagerInScene();
        };
    }
#endif
}
