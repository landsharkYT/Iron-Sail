using UnityEngine;

[DisallowMultipleComponent]
public class WorldAmbientAudioController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] DayNightController dayNightController;
    [SerializeField] AudioSource waveAudioSource;
    [SerializeField] AudioSource seagullAudioSource;

    [Header("Clips")]
    [SerializeField] AudioClip seaWaveClip;
    [SerializeField] AudioClip seagullClip;

    [Header("Wave Ambience")]
    [SerializeField] float waveTargetVolume = 0.55f;
    [SerializeField] float waveFadeInSeconds = 2.5f;
    [SerializeField] float waveFadeOutSeconds = 2.5f;

    [Header("Seagulls")]
    [SerializeField] float seagullVolume = 0.8f;
    [SerializeField] float seagullIntervalMin = 10f;
    [SerializeField] float seagullIntervalMax = 24f;

    float nextSeagullStartTime = -1f;
    bool wasSeagullPlayingLastFrame;
    float currentWaveTargetVolume;

    void Awake()
    {
        if (dayNightController == null)
            dayNightController = FindAnyObjectByType<DayNightController>();

        ConfigureWaveSource();
        ConfigureSeagullSource();
        waveTargetVolume = Mathf.Max(0f, waveTargetVolume);
        waveFadeInSeconds = Mathf.Max(0.01f, waveFadeInSeconds);
        waveFadeOutSeconds = Mathf.Max(0.01f, waveFadeOutSeconds);
        seagullVolume = Mathf.Max(0f, seagullVolume);
        seagullIntervalMin = Mathf.Max(0f, seagullIntervalMin);
        seagullIntervalMax = Mathf.Max(seagullIntervalMin, seagullIntervalMax);
    }

    void OnEnable()
    {
        if (dayNightController == null)
            dayNightController = FindAnyObjectByType<DayNightController>();

        if (dayNightController != null)
            dayNightController.OnPhaseChanged += HandlePhaseChanged;
    }

    void Start()
    {
        StartWaveLoop();
        currentWaveTargetVolume = waveTargetVolume;

        if (AreSeagullsAllowed())
            ScheduleNextSeagull();
    }

    void Update()
    {
        UpdateWaveFade();
        UpdateSeagullPlayback();
    }

    void OnDisable()
    {
        if (dayNightController != null)
            dayNightController.OnPhaseChanged -= HandlePhaseChanged;
    }

    void HandlePhaseChanged(DayNightPhase previousPhase, DayNightPhase currentPhase)
    {
        if (AreSeagullsAllowed())
        {
            if (seagullAudioSource != null && !seagullAudioSource.isPlaying && nextSeagullStartTime < 0f)
                ScheduleNextSeagull();
        }
        else
        {
            nextSeagullStartTime = -1f;
        }
    }

    void ConfigureWaveSource()
    {
        if (waveAudioSource == null)
            return;

        waveAudioSource.playOnAwake = false;
        waveAudioSource.loop = true;
        waveAudioSource.spatialBlend = 0f;
        waveAudioSource.dopplerLevel = 0f;
        waveAudioSource.volume = 0f;
    }

    void ConfigureSeagullSource()
    {
        if (seagullAudioSource == null)
            return;

        seagullAudioSource.playOnAwake = false;
        seagullAudioSource.loop = false;
        seagullAudioSource.spatialBlend = 0f;
        seagullAudioSource.dopplerLevel = 0f;
    }

    void StartWaveLoop()
    {
        if (waveAudioSource == null || seaWaveClip == null)
            return;

        waveAudioSource.clip = seaWaveClip;
        if (!waveAudioSource.isPlaying)
            waveAudioSource.Play();
    }

    void UpdateWaveFade()
    {
        if (waveAudioSource == null)
            return;

        float targetVolume = currentWaveTargetVolume * GameRuntimeSettings.GetAmbienceBusVolume();
        float fadeDuration = targetVolume >= waveAudioSource.volume ? waveFadeInSeconds : waveFadeOutSeconds;
        float fadeSpeed = fadeDuration > 0f ? 1f / fadeDuration : float.MaxValue;
        waveAudioSource.volume = Mathf.MoveTowards(waveAudioSource.volume, targetVolume, fadeSpeed * Time.deltaTime);
    }

    void UpdateSeagullPlayback()
    {
        if (seagullAudioSource == null || seagullClip == null)
            return;

        bool isPlaying = seagullAudioSource.isPlaying;
        if (wasSeagullPlayingLastFrame && !isPlaying)
        {
            if (AreSeagullsAllowed())
                ScheduleNextSeagull();
            else
                nextSeagullStartTime = -1f;
        }

        wasSeagullPlayingLastFrame = isPlaying;

        if (!AreSeagullsAllowed())
        {
            nextSeagullStartTime = -1f;
            return;
        }

        if (isPlaying)
            return;

        if (nextSeagullStartTime < 0f)
            ScheduleNextSeagull();

        if (Time.time >= nextSeagullStartTime)
            PlaySeagull();
    }

    void PlaySeagull()
    {
        if (seagullAudioSource == null || seagullClip == null)
            return;

        seagullAudioSource.clip = seagullClip;
        seagullAudioSource.volume = seagullVolume * GameRuntimeSettings.GetAmbienceBusVolume();
        seagullAudioSource.Play();
        nextSeagullStartTime = -1f;
        wasSeagullPlayingLastFrame = true;
    }

    void ScheduleNextSeagull()
    {
        float delay = Random.Range(seagullIntervalMin, seagullIntervalMax);
        nextSeagullStartTime = Time.time + delay;
    }

    bool AreSeagullsAllowed()
    {
        if (dayNightController == null)
            return true;

        return dayNightController.CurrentPhase != DayNightPhase.Night;
    }
}
