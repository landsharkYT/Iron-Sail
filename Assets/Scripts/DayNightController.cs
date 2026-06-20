using System;
using UnityEngine;

// Core time-of-day clock for the game.
//
// This controller is intentionally isolated in the first pass:
// it owns time progression, phase math, validation, debug mirrors, and
// controlled mutation APIs, but it does not directly affect lighting, UI,
// wind, combat, or spawning yet.
public class DayNightController : MonoBehaviour
{
    [Serializable]
    struct PhaseDurationsMinutes
    {
        // These values are authored like "4 / 11 / 4 / 11".
        // They are treated as relative weights, not as a required literal sum.
        public float sunrise;
        public float day;
        public float sunset;
        public float night;
    }

    [Header("Cycle")]
    // Total day length in real-world minutes.
    [SerializeField] float totalDayLengthMinutes = 15f;
    [SerializeField] PhaseDurationsMinutes phaseDurationsMinutes = new PhaseDurationsMinutes
    {
        sunrise = 4f,
        day = 11f,
        sunset = 4f,
        night = 11f
    };

    [Header("Startup")]
    [SerializeField] DayNightPhase startPhase = DayNightPhase.Day;
    [SerializeField] [Range(0f, 1f)] float startPhaseProgress = 0.2f;

    [Header("Runtime")]
    [SerializeField] float baseTimeScale = 1f;
    [SerializeField] bool verboseLogging;

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] DayNightPhase debugCurrentPhase;
    [SerializeField] float debugNormalizedTimeOfDay;
    [SerializeField] float debugPhaseProgress;
    [SerializeField] float debugElapsedSecondsInDay;
    [SerializeField] int debugDayCount;
    [SerializeField] bool debugIsPaused;
    [SerializeField] float debugEffectiveTimeScale;
    [SerializeField] float debugResolvedSunriseSeconds;
    [SerializeField] float debugResolvedDaySeconds;
    [SerializeField] float debugResolvedSunsetSeconds;
    [SerializeField] float debugResolvedNightSeconds;

    public event Action<DayNightPhase, DayNightPhase> OnPhaseChanged;
    public event Action<int> OnDayStarted;

    public DayNightPhase CurrentPhase => currentPhase;
    public float PhaseProgress => phaseProgress;
    public float NormalizedTimeOfDay => normalizedTimeOfDay;
    public float ElapsedSecondsInDay => currentDaySeconds;
    public int DayCount => dayCount;
    public bool IsPaused => isTimePaused;
    public float BaseTimeScale => baseTimeScale;
    public float EffectiveTimeScale => GetEffectiveTimeScale();
    public float TimeRemainingInPhase => timeRemainingInPhase;
    public float TimeUntilNextPhase => timeRemainingInPhase;

    float totalDayLengthSeconds;
    float sunriseDurationSeconds;
    float dayDurationSeconds;
    float sunsetDurationSeconds;
    float nightDurationSeconds;

    float sunriseStartSeconds;
    float dayStartSeconds;
    float sunsetStartSeconds;
    float nightStartSeconds;
    float dayEndSeconds;

    float currentDaySeconds;
    int dayCount;
    bool isTimePaused;
    bool hasTemporaryTimeScaleOverride;
    float temporaryTimeScaleOverride;
    bool hasInitialized;

    DayNightPhase currentPhase;
    float phaseProgress;
    float normalizedTimeOfDay;
    float timeRemainingInPhase;

    void Start()
    {
        InitializeFromConfiguredStartTime();
    }

    void Update()
    {
        if (!hasInitialized)
            return;

        float effectiveTimeScale = GetEffectiveTimeScale();
        if (effectiveTimeScale <= 0f)
        {
            RefreshDerivedState();
            UpdateDebugMirrors();
            return;
        }

        AdvanceTime(Time.deltaTime * effectiveTimeScale);
        UpdateDebugMirrors();
    }

    void OnValidate()
    {
        SanitizeSerializedSettings();
        RebuildCachedPhaseData();
    }

    public void SetPaused(bool paused)
    {
        if (isTimePaused == paused)
            return;

        isTimePaused = paused;
        RefreshDerivedState();
        UpdateDebugMirrors();
    }

    public void SetTimeScale(float timeScale)
    {
        float sanitizedTimeScale = SanitizePositiveTimeScale(timeScale, "SetTimeScale");
        baseTimeScale = sanitizedTimeScale;
        RefreshDerivedState();
        UpdateDebugMirrors();
    }

    public void SetTemporaryTimeScaleOverride(float timeScale)
    {
        float sanitizedTimeScale = SanitizePositiveTimeScale(timeScale, "SetTemporaryTimeScaleOverride");
        temporaryTimeScaleOverride = sanitizedTimeScale;
        hasTemporaryTimeScaleOverride = true;
        RefreshDerivedState();
        UpdateDebugMirrors();
    }

    public void ClearTemporaryTimeScaleOverride()
    {
        if (!hasTemporaryTimeScaleOverride)
            return;

        hasTemporaryTimeScaleOverride = false;
        temporaryTimeScaleOverride = 0f;
        RefreshDerivedState();
        UpdateDebugMirrors();
    }

    public void SetTimeNormalized(float normalizedTime)
    {
        float wrappedNormalizedTime = WrapNormalizedTime(normalizedTime, "SetTimeNormalized");
        float targetSeconds = wrappedNormalizedTime * totalDayLengthSeconds;
        SetCurrentDaySecondsInternal(targetSeconds, true, false, "SetTimeNormalized");
    }

    public void JumpToPhase(DayNightPhase phase, float targetPhaseProgress = 0f)
    {
        float targetSeconds = GetTimeSecondsForPhaseProgress(phase, targetPhaseProgress);
        SetCurrentDaySecondsInternal(targetSeconds, true, false, "JumpToPhase");
    }

    public bool AdvanceForwardToPhase(DayNightPhase phase, float targetPhaseProgress = 0f)
    {
        if (!hasInitialized)
            InitializeFromConfiguredStartTime();

        float targetSeconds = GetTimeSecondsForPhaseProgress(phase, targetPhaseProgress);
        float deltaSeconds = targetSeconds - currentDaySeconds;
        if (deltaSeconds <= 0f)
            deltaSeconds += totalDayLengthSeconds;

        if (deltaSeconds <= 0f)
            return false;

        AdvanceTime(deltaSeconds);
        UpdateDebugMirrors();
        return true;
    }

    public void RestoreTimeState(int restoredDayCount, float normalizedTime)
    {
        if (!hasInitialized)
            InitializeFromConfiguredStartTime();

        dayCount = Mathf.Max(0, restoredDayCount);
        float wrappedNormalizedTime = WrapNormalizedTime(normalizedTime, "RestoreTimeState");
        currentDaySeconds = wrappedNormalizedTime * totalDayLengthSeconds;
        RefreshDerivedState();
        UpdateDebugMirrors();
    }

    public float GetPhaseDurationSeconds(DayNightPhase phase)
    {
        if (phase == DayNightPhase.Sunrise)
            return sunriseDurationSeconds;
        if (phase == DayNightPhase.Day)
            return dayDurationSeconds;
        if (phase == DayNightPhase.Sunset)
            return sunsetDurationSeconds;
        if (phase == DayNightPhase.Night)
            return nightDurationSeconds;

        WarnVerbose("Unknown phase requested in GetPhaseDurationSeconds. Returning 0.");
        return 0f;
    }

    public float GetPhaseStartSeconds(DayNightPhase phase)
    {
        if (phase == DayNightPhase.Sunrise)
            return sunriseStartSeconds;
        if (phase == DayNightPhase.Day)
            return dayStartSeconds;
        if (phase == DayNightPhase.Sunset)
            return sunsetStartSeconds;
        if (phase == DayNightPhase.Night)
            return nightStartSeconds;

        WarnVerbose("Unknown phase requested in GetPhaseStartSeconds. Returning 0.");
        return 0f;
    }

    public float GetPhaseEndSeconds(DayNightPhase phase)
    {
        if (phase == DayNightPhase.Sunrise)
            return dayStartSeconds;
        if (phase == DayNightPhase.Day)
            return sunsetStartSeconds;
        if (phase == DayNightPhase.Sunset)
            return nightStartSeconds;
        if (phase == DayNightPhase.Night)
            return dayEndSeconds;

        WarnVerbose("Unknown phase requested in GetPhaseEndSeconds. Returning 0.");
        return 0f;
    }

    public float GetTimeSecondsForPhaseProgress(DayNightPhase phase, float targetPhaseProgress)
    {
        float sanitizedPhaseProgress = Mathf.Clamp01(targetPhaseProgress);
        if (!Mathf.Approximately(sanitizedPhaseProgress, targetPhaseProgress))
            WarnVerbose($"Clamped phase progress from {targetPhaseProgress} to {sanitizedPhaseProgress}.");

        float phaseStartSeconds = GetPhaseStartSeconds(phase);
        float phaseDurationSeconds = GetPhaseDurationSeconds(phase);
        return phaseStartSeconds + phaseDurationSeconds * sanitizedPhaseProgress;
    }

    void InitializeFromConfiguredStartTime()
    {
        SanitizeSerializedSettings();
        RebuildCachedPhaseData();

        currentDaySeconds = GetTimeSecondsForPhaseProgress(startPhase, startPhaseProgress);
        dayCount = 0;
        isTimePaused = false;
        hasTemporaryTimeScaleOverride = false;
        temporaryTimeScaleOverride = 0f;
        hasInitialized = true;

        RefreshDerivedState();
        UpdateDebugMirrors();
    }

    void AdvanceTime(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
            return;

        float nextSeconds = currentDaySeconds + deltaSeconds;
        int wrappedDays = 0;

        while (nextSeconds >= totalDayLengthSeconds)
        {
            nextSeconds -= totalDayLengthSeconds;
            wrappedDays++;
        }

        DayNightPhase previousPhase = currentPhase;

        currentDaySeconds = nextSeconds;
        if (wrappedDays > 0)
        {
            for (int i = 0; i < wrappedDays; i++)
            {
                dayCount++;
                OnDayStarted?.Invoke(dayCount);
            }
        }

        RefreshDerivedState();

        if (previousPhase != currentPhase)
            OnPhaseChanged?.Invoke(previousPhase, currentPhase);
    }

    void SetCurrentDaySecondsInternal(float targetSeconds, bool firePhaseEvent, bool allowDayCountChange, string callerName)
    {
        float wrappedSeconds = WrapDaySeconds(targetSeconds, callerName);
        DayNightPhase previousPhase = currentPhase;

        currentDaySeconds = wrappedSeconds;
        if (!allowDayCountChange)
        {
            // Manual setters are intentionally position-only in this first pass.
            // They must not masquerade as natural progression.
        }

        RefreshDerivedState();
        UpdateDebugMirrors();

        if (firePhaseEvent && previousPhase != currentPhase)
            OnPhaseChanged?.Invoke(previousPhase, currentPhase);
    }

    void RefreshDerivedState()
    {
        if (totalDayLengthSeconds <= 0f)
            totalDayLengthSeconds = 1f;

        currentPhase = GetPhaseForSeconds(currentDaySeconds);
        normalizedTimeOfDay = currentDaySeconds / totalDayLengthSeconds;
        phaseProgress = GetPhaseProgressForSeconds(currentPhase, currentDaySeconds);
        timeRemainingInPhase = GetPhaseEndSeconds(currentPhase) - currentDaySeconds;
        if (timeRemainingInPhase < 0f)
            timeRemainingInPhase = 0f;
    }

    void UpdateDebugMirrors()
    {
        if (!Application.isPlaying)
            return;

        debugCurrentPhase = currentPhase;
        debugNormalizedTimeOfDay = normalizedTimeOfDay;
        debugPhaseProgress = phaseProgress;
        debugElapsedSecondsInDay = currentDaySeconds;
        debugDayCount = dayCount;
        debugIsPaused = isTimePaused;
        debugEffectiveTimeScale = GetEffectiveTimeScale();
        debugResolvedSunriseSeconds = sunriseDurationSeconds;
        debugResolvedDaySeconds = dayDurationSeconds;
        debugResolvedSunsetSeconds = sunsetDurationSeconds;
        debugResolvedNightSeconds = nightDurationSeconds;
    }

    void SanitizeSerializedSettings()
    {
        float originalTotalDayLengthMinutes = totalDayLengthMinutes;
        totalDayLengthMinutes = Mathf.Max(totalDayLengthMinutes, 0.01f);
        if (!Mathf.Approximately(originalTotalDayLengthMinutes, totalDayLengthMinutes))
            WarnVerbose($"Clamped total day length from {originalTotalDayLengthMinutes} to {totalDayLengthMinutes}.");

        float originalBaseTimeScale = baseTimeScale;
        baseTimeScale = Mathf.Max(baseTimeScale, 0.01f);
        if (!Mathf.Approximately(originalBaseTimeScale, baseTimeScale))
            WarnVerbose($"Clamped base time scale from {originalBaseTimeScale} to {baseTimeScale}.");

        float originalStartPhaseProgress = startPhaseProgress;
        startPhaseProgress = Mathf.Clamp01(startPhaseProgress);
        if (!Mathf.Approximately(originalStartPhaseProgress, startPhaseProgress))
            WarnVerbose($"Clamped start phase progress from {originalStartPhaseProgress} to {startPhaseProgress}.");

        bool clampedNegativeWeights = false;
        float sunrise = phaseDurationsMinutes.sunrise;
        float day = phaseDurationsMinutes.day;
        float sunset = phaseDurationsMinutes.sunset;
        float night = phaseDurationsMinutes.night;

        if (sunrise < 0f)
        {
            sunrise = 0f;
            clampedNegativeWeights = true;
        }
        if (day < 0f)
        {
            day = 0f;
            clampedNegativeWeights = true;
        }
        if (sunset < 0f)
        {
            sunset = 0f;
            clampedNegativeWeights = true;
        }
        if (night < 0f)
        {
            night = 0f;
            clampedNegativeWeights = true;
        }

        if (clampedNegativeWeights)
            WarnVerbose("Clamped one or more negative phase duration weights to zero.");

        float totalWeights = sunrise + day + sunset + night;
        if (totalWeights <= 0f)
        {
            WarnVerbose("Phase duration weights were invalid. Restoring safe defaults of 4 / 11 / 4 / 11.");
            phaseDurationsMinutes.sunrise = 4f;
            phaseDurationsMinutes.day = 11f;
            phaseDurationsMinutes.sunset = 4f;
            phaseDurationsMinutes.night = 11f;
            return;
        }

        phaseDurationsMinutes.sunrise = sunrise;
        phaseDurationsMinutes.day = day;
        phaseDurationsMinutes.sunset = sunset;
        phaseDurationsMinutes.night = night;
    }

    void RebuildCachedPhaseData()
    {
        totalDayLengthSeconds = totalDayLengthMinutes * 60f;

        float totalWeights = phaseDurationsMinutes.sunrise
                           + phaseDurationsMinutes.day
                           + phaseDurationsMinutes.sunset
                           + phaseDurationsMinutes.night;

        if (totalWeights <= 0f)
        {
            // SanitizeSerializedSettings should already have prevented this, but
            // keep a final safety net here because boundary math depends on it.
            phaseDurationsMinutes.sunrise = 4f;
            phaseDurationsMinutes.day = 11f;
            phaseDurationsMinutes.sunset = 4f;
            phaseDurationsMinutes.night = 11f;
            totalWeights = 30f;
            WarnVerbose("Rebuilt boundaries from fallback defaults because total weights were invalid.");
        }

        sunriseDurationSeconds = totalDayLengthSeconds * (phaseDurationsMinutes.sunrise / totalWeights);
        dayDurationSeconds = totalDayLengthSeconds * (phaseDurationsMinutes.day / totalWeights);
        sunsetDurationSeconds = totalDayLengthSeconds * (phaseDurationsMinutes.sunset / totalWeights);
        nightDurationSeconds = totalDayLengthSeconds * (phaseDurationsMinutes.night / totalWeights);

        sunriseStartSeconds = 0f;
        dayStartSeconds = sunriseStartSeconds + sunriseDurationSeconds;
        sunsetStartSeconds = dayStartSeconds + dayDurationSeconds;
        nightStartSeconds = sunsetStartSeconds + sunsetDurationSeconds;
        dayEndSeconds = totalDayLengthSeconds;
    }

    DayNightPhase GetPhaseForSeconds(float seconds)
    {
        if (seconds < dayStartSeconds)
            return DayNightPhase.Sunrise;
        if (seconds < sunsetStartSeconds)
            return DayNightPhase.Day;
        if (seconds < nightStartSeconds)
            return DayNightPhase.Sunset;
        return DayNightPhase.Night;
    }

    float GetPhaseProgressForSeconds(DayNightPhase phase, float seconds)
    {
        float phaseStart = GetPhaseStartSeconds(phase);
        float phaseDuration = GetPhaseDurationSeconds(phase);
        if (phaseDuration <= 0f)
            return 0f;

        return Mathf.Clamp01((seconds - phaseStart) / phaseDuration);
    }

    float GetEffectiveTimeScale()
    {
        if (isTimePaused)
            return 0f;
        if (hasTemporaryTimeScaleOverride)
            return temporaryTimeScaleOverride;
        return baseTimeScale;
    }

    float SanitizePositiveTimeScale(float value, string callerName)
    {
        float sanitizedValue = Mathf.Max(value, 0.01f);
        if (!Mathf.Approximately(sanitizedValue, value))
            WarnVerbose($"{callerName} corrected time scale from {value} to {sanitizedValue}.");

        return sanitizedValue;
    }

    float WrapNormalizedTime(float value, string callerName)
    {
        float wrappedValue = Mathf.Repeat(value, 1f);
        if (!Mathf.Approximately(wrappedValue, value))
            WarnVerbose($"{callerName} wrapped normalized time from {value} to {wrappedValue}.");

        return wrappedValue;
    }

    float WrapDaySeconds(float value, string callerName)
    {
        if (totalDayLengthSeconds <= 0f)
            return 0f;

        float wrappedValue = Mathf.Repeat(value, totalDayLengthSeconds);
        if (!Mathf.Approximately(wrappedValue, value))
            WarnVerbose($"{callerName} wrapped day seconds from {value} to {wrappedValue}.");

        return wrappedValue;
    }

    void WarnVerbose(string message)
    {
        if (!verboseLogging)
            return;

        Debug.LogWarning($"[DayNightController] {message}", this);
    }
}
