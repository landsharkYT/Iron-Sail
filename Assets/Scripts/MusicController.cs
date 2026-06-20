using UnityEngine;

[DisallowMultipleComponent]
public class MusicController : MonoBehaviour
{
    enum MusicState
    {
        Day,
        Sunrise,
        Sunset,
        Night,
        HighSpeed,
        Combat
    }

    [Header("References")]
    [SerializeField] DayNightController dayNightController;
    [SerializeField] NightEnemySpawner nightEnemySpawner;
    [SerializeField] BoatController boatController;
    [SerializeField] Transform boatTransform;
    [SerializeField] AudioSource primaryMusicSource;
    [SerializeField] AudioSource secondaryMusicSource;

    [Header("Tracks")]
    [SerializeField] AudioClip dayTrack;
    [SerializeField] AudioClip sunriseTrack;
    [SerializeField] AudioClip sunsetTrack;
    [SerializeField] AudioClip nightTrack;
    [SerializeField] AudioClip combatTrack;
    [SerializeField] AudioClip highSpeedTrack;

    [Header("Track Volumes")]
    [SerializeField] float dayVolume = 1f;
    [SerializeField] float sunriseVolume = 1f;
    [SerializeField] float sunsetVolume = 1f;
    [SerializeField] float nightVolume = 1f;
    [SerializeField] float combatVolume = 1f;
    [SerializeField] float highSpeedVolume = 1f;

    [Header("Combat")]
    [SerializeField] float combatEnterRadius = 9f;
    [SerializeField] float combatExitRadius = 12f;

    [Header("High Speed")]
    [SerializeField] [Range(0f, 1f)] float enterHighSpeedThreshold = 0.78f;
    [SerializeField] [Range(0f, 1f)] float exitHighSpeedThreshold = 0.62f;

    [Header("Transitions")]
    [SerializeField] float crossfadeSeconds = 1.75f;
    [SerializeField] float stateCheckIntervalSeconds = 0.25f;

    [Header("UI Dampening")]
    [SerializeField] [Range(0f, 1f)] float uiDampenVolumeMultiplier = 0.42f;

    AudioSource activeSource;
    AudioSource inactiveSource;
    AudioClip currentResolvedClip;
    float activeTrackVolume = 1f;
    float inactiveTrackVolume = 1f;
    bool isInCombatState;
    bool isInHighSpeedState;
    bool warnedNoTracksAssigned;
    float nextStateCheckTime;
    float crossfadeProgress = 1f;

    void Awake()
    {
        ResolveReferences();
        EnsureSources();
        ConfigureSource(primaryMusicSource);
        ConfigureSource(secondaryMusicSource);
        activeSource = primaryMusicSource;
        inactiveSource = secondaryMusicSource;
        ClampSettings();
    }

    void Start()
    {
        ResolveAndApplyMusicState(forceImmediate: true);
        nextStateCheckTime = Time.time + stateCheckIntervalSeconds;
    }

    void Update()
    {
        if (Time.time >= nextStateCheckTime)
        {
            ResolveAndApplyMusicState(forceImmediate: false);
            nextStateCheckTime = Time.time + stateCheckIntervalSeconds;
        }

        UpdateSourceVolumes(Time.deltaTime);
    }

    void OnValidate()
    {
        ClampSettings();
        ConfigureSource(primaryMusicSource);
        ConfigureSource(secondaryMusicSource);
    }

    void ResolveAndApplyMusicState(bool forceImmediate)
    {
        ResolveReferences();

        MusicState desiredState = ResolveDesiredState();
        AudioClip resolvedClip = ResolveFallbackClip(desiredState);

        if (resolvedClip == null)
        {
            if (!warnedNoTracksAssigned)
            {
                Debug.LogWarning("[MusicController] No music tracks are assigned. Music will remain silent.", this);
                warnedNoTracksAssigned = true;
            }

            currentResolvedClip = null;
            StartFadeToSilence();
            return;
        }

        warnedNoTracksAssigned = false;

        if (currentResolvedClip == resolvedClip && activeSource != null && activeSource.clip == resolvedClip && activeSource.isPlaying)
            return;

        currentResolvedClip = resolvedClip;
        PlayResolvedClip(resolvedClip, GetVolumeForClip(resolvedClip), forceImmediate);
    }

    MusicState ResolveDesiredState()
    {
        UpdateCombatState();
        if (isInCombatState)
            return MusicState.Combat;

        UpdateHighSpeedState();
        if (isInHighSpeedState)
            return MusicState.HighSpeed;

        if (dayNightController == null)
            return MusicState.Day;

        return dayNightController.CurrentPhase switch
        {
            DayNightPhase.Sunrise => MusicState.Sunrise,
            DayNightPhase.Sunset => MusicState.Sunset,
            DayNightPhase.Night => MusicState.Night,
            _ => MusicState.Day
        };
    }

    void UpdateCombatState()
    {
        if (nightEnemySpawner == null || boatTransform == null)
        {
            isInCombatState = false;
            return;
        }

        float radius = isInCombatState ? combatExitRadius : combatEnterRadius;
        isInCombatState = nightEnemySpawner.HasActiveEnemyWithinRadius(boatTransform.position, radius);
    }

    void UpdateHighSpeedState()
    {
        if (boatController == null)
        {
            isInHighSpeedState = false;
            return;
        }

        float speedFraction = boatController.SpeedFraction;
        if (isInHighSpeedState)
            isInHighSpeedState = speedFraction >= exitHighSpeedThreshold;
        else
            isInHighSpeedState = speedFraction >= enterHighSpeedThreshold;
    }

    AudioClip ResolveFallbackClip(MusicState desiredState)
    {
        return desiredState switch
        {
            MusicState.Combat => FirstAssigned(combatTrack, highSpeedTrack, nightTrack, sunsetTrack, sunriseTrack, dayTrack),
            MusicState.HighSpeed => FirstAssigned(highSpeedTrack, nightTrack, sunsetTrack, sunriseTrack, dayTrack),
            MusicState.Night => FirstAssigned(nightTrack, sunsetTrack, sunriseTrack, dayTrack),
            MusicState.Sunset => FirstAssigned(sunsetTrack, sunriseTrack, dayTrack),
            MusicState.Sunrise => FirstAssigned(sunriseTrack, dayTrack),
            _ => dayTrack
        };
    }

    static AudioClip FirstAssigned(AudioClip a, AudioClip b)
    {
        if (a != null) return a;
        return b;
    }

    static AudioClip FirstAssigned(AudioClip a, AudioClip b, AudioClip c)
    {
        if (a != null) return a;
        if (b != null) return b;
        return c;
    }

    static AudioClip FirstAssigned(AudioClip a, AudioClip b, AudioClip c, AudioClip d)
    {
        if (a != null) return a;
        if (b != null) return b;
        if (c != null) return c;
        return d;
    }

    static AudioClip FirstAssigned(AudioClip a, AudioClip b, AudioClip c, AudioClip d, AudioClip e)
    {
        if (a != null) return a;
        if (b != null) return b;
        if (c != null) return c;
        if (d != null) return d;
        return e;
    }

    static AudioClip FirstAssigned(AudioClip a, AudioClip b, AudioClip c, AudioClip d, AudioClip e, AudioClip f)
    {
        if (a != null) return a;
        if (b != null) return b;
        if (c != null) return c;
        if (d != null) return d;
        if (e != null) return e;
        return f;
    }

    void PlayResolvedClip(AudioClip resolvedClip, float resolvedVolume, bool forceImmediate)
    {
        if (activeSource == null || inactiveSource == null || resolvedClip == null)
            return;

        AudioSource targetSource = forceImmediate && (!activeSource.isPlaying || activeSource.clip == null)
            ? activeSource
            : inactiveSource;

        bool needsRestart = targetSource.clip != resolvedClip || !targetSource.isPlaying;
        if (needsRestart && targetSource.isPlaying)
            targetSource.Stop();

        targetSource.clip = resolvedClip;
        if (needsRestart)
            targetSource.time = 0f;
        targetSource.volume = 0f;
        targetSource.loop = true;
        if (needsRestart)
            targetSource.Play();

        if (forceImmediate && activeSource == targetSource)
        {
            activeTrackVolume = resolvedVolume;
            inactiveTrackVolume = 0f;
            crossfadeProgress = 1f;
            UpdateSourceVolumes(0f);
            return;
        }

        AudioSource previousActive = activeSource;
        float previousActiveVolume = activeTrackVolume;
        activeSource = targetSource;
        inactiveSource = previousActive;
        activeTrackVolume = resolvedVolume;
        inactiveTrackVolume = previousActiveVolume;
        crossfadeProgress = 0f;
    }

    void StartFadeToSilence()
    {
        if (activeSource == null || inactiveSource == null)
            return;

        if (inactiveSource.isPlaying)
            inactiveSource.Stop();

        crossfadeProgress = 0f;
        currentResolvedClip = null;
        inactiveTrackVolume = 0f;
    }

    void UpdateSourceVolumes(float deltaTime)
    {
        if (activeSource == null || inactiveSource == null)
            return;

        if (crossfadeProgress < 1f)
        {
            float duration = Mathf.Max(0.01f, crossfadeSeconds);
            crossfadeProgress = Mathf.MoveTowards(crossfadeProgress, 1f, deltaTime / duration);
        }

        float dampenMultiplier = IsUiDampeningActive() ? uiDampenVolumeMultiplier : 1f;
        float musicBusVolume = GameRuntimeSettings.GetMusicBusVolume();
        float activeBase = currentResolvedClip != null ? crossfadeProgress : 1f - crossfadeProgress;
        float inactiveBase = currentResolvedClip != null ? 1f - crossfadeProgress : 0f;

        activeSource.volume = activeBase * activeTrackVolume * dampenMultiplier * musicBusVolume;
        inactiveSource.volume = inactiveBase * inactiveTrackVolume * dampenMultiplier * musicBusVolume;

        if (crossfadeProgress >= 1f && inactiveSource.isPlaying && inactiveSource.volume <= 0.0001f)
            inactiveSource.Stop();
    }

    bool IsUiDampeningActive()
    {
        return PauseMenuController.IsPauseOpen
            || InventoryUIController.IsInventoryOpen
            || WorldMapUIController.IsMapOpen
            || ShopController.IsShopOpen
            || FishingMinigameController.IsFishingOpen;
    }

    float GetVolumeForClip(AudioClip clip)
    {
        if (clip == null)
            return 1f;
        if (clip == combatTrack)
            return combatVolume;
        if (clip == highSpeedTrack)
            return highSpeedVolume;
        if (clip == nightTrack)
            return nightVolume;
        if (clip == sunsetTrack)
            return sunsetVolume;
        if (clip == sunriseTrack)
            return sunriseVolume;
        if (clip == dayTrack)
            return dayVolume;

        return 1f;
    }

    void ResolveReferences()
    {
        if (dayNightController == null)
            dayNightController = FindAnyObjectByType<DayNightController>();
        if (nightEnemySpawner == null)
            nightEnemySpawner = FindAnyObjectByType<NightEnemySpawner>();
        if (boatController == null)
            boatController = FindAnyObjectByType<BoatController>();
        if (boatTransform == null && boatController != null)
            boatTransform = boatController.transform;
    }

    void EnsureSources()
    {
        if (primaryMusicSource == null)
            primaryMusicSource = gameObject.AddComponent<AudioSource>();
        if (secondaryMusicSource == null)
            secondaryMusicSource = gameObject.AddComponent<AudioSource>();
    }

    void ClampSettings()
    {
        combatEnterRadius = Mathf.Max(0f, combatEnterRadius);
        combatExitRadius = Mathf.Max(combatEnterRadius, combatExitRadius);
        enterHighSpeedThreshold = Mathf.Clamp01(enterHighSpeedThreshold);
        exitHighSpeedThreshold = Mathf.Clamp(exitHighSpeedThreshold, 0f, enterHighSpeedThreshold);
        crossfadeSeconds = Mathf.Max(0.01f, crossfadeSeconds);
        stateCheckIntervalSeconds = Mathf.Max(0.05f, stateCheckIntervalSeconds);
        uiDampenVolumeMultiplier = Mathf.Clamp01(uiDampenVolumeMultiplier);
        dayVolume = Mathf.Max(0f, dayVolume);
        sunriseVolume = Mathf.Max(0f, sunriseVolume);
        sunsetVolume = Mathf.Max(0f, sunsetVolume);
        nightVolume = Mathf.Max(0f, nightVolume);
        combatVolume = Mathf.Max(0f, combatVolume);
        highSpeedVolume = Mathf.Max(0f, highSpeedVolume);
    }

    static void ConfigureSource(AudioSource source)
    {
        if (source == null)
            return;

        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
    }
}
