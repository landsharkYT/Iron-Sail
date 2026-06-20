using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Applies day/night visual mood to the world.
//
// Responsibilities in this pass:
// - drive the existing Global Light 2D
// - optionally drive Camera.backgroundColor
// - push separate tint values to registered world tint targets
//
// UI stays out of scope: only world sprites should respond here.
public class DayNightLightingController : MonoBehaviour
{
    [System.Serializable]
    struct LightingPreset
    {
        public Color lightColor;
        [Range(0f, 1f)] public float lightIntensity;
        public Color backgroundColor;
    }

    [System.Serializable]
    struct SpriteTintPreset
    {
        public Color colorMultiplier;
        [Range(0f, 1f)] public float brightnessMultiplier;
    }

    struct LightingState
    {
        public Color lightColor;
        public float lightIntensity;
        public Color backgroundColor;
        public Color spriteTintColor;
        public float spriteTintBrightness;
    }

    public static DayNightLightingController ActiveController { get; private set; }
    public Color CurrentSpriteTintColor => currentState.spriteTintColor;
    public float CurrentSpriteTintBrightness => currentState.spriteTintBrightness;

    [Header("References")]
    [SerializeField] DayNightController dayNightController;
    [SerializeField] Light2D globalLight2D;
    [SerializeField] Camera targetCamera;

    [Header("Behavior")]
    [SerializeField] bool affectCameraBackground = true;
    [SerializeField] float fadeDurationSeconds = 3f;

    [Header("Phase Presets")]
    [SerializeField] LightingPreset sunrisePreset = new LightingPreset
    {
        lightColor = new Color(1f, 0.88f, 0.70f, 1f),
        lightIntensity = 0.94f,
        backgroundColor = new Color(0.36f, 0.46f, 0.58f, 1f)
    };
    [SerializeField] LightingPreset dayPreset = new LightingPreset
    {
        lightColor = Color.white,
        lightIntensity = 1f,
        backgroundColor = new Color(0.19215687f, 0.3019608f, 0.4745098f, 1f)
    };
    [SerializeField] LightingPreset sunsetPreset = new LightingPreset
    {
        lightColor = new Color(1f, 0.80f, 0.60f, 1f),
        lightIntensity = 0.9f,
        backgroundColor = new Color(0.30f, 0.26f, 0.38f, 1f)
    };
    [SerializeField] LightingPreset nightPreset = new LightingPreset
    {
        lightColor = new Color(0.64f, 0.74f, 0.96f, 1f),
        lightIntensity = 0.6f,
        backgroundColor = new Color(0.05f, 0.09f, 0.18f, 1f)
    };

    [Header("Sprite Tint Presets")]
    [SerializeField] SpriteTintPreset sunriseSpriteTintPreset = new SpriteTintPreset
    {
        colorMultiplier = new Color(1f, 0.94f, 0.84f, 1f),
        brightnessMultiplier = 0.97f
    };
    [SerializeField] SpriteTintPreset daySpriteTintPreset = new SpriteTintPreset
    {
        colorMultiplier = Color.white,
        brightnessMultiplier = 1f
    };
    [SerializeField] SpriteTintPreset sunsetSpriteTintPreset = new SpriteTintPreset
    {
        colorMultiplier = new Color(1f, 0.88f, 0.78f, 1f),
        brightnessMultiplier = 0.92f
    };
    [SerializeField] SpriteTintPreset nightSpriteTintPreset = new SpriteTintPreset
    {
        colorMultiplier = new Color(0.68f, 0.75f, 0.90f, 1f),
        brightnessMultiplier = 0.74f
    };

    [Header("Runtime Debug (Play Mode Only)")]
    [SerializeField] DayNightPhase debugTargetPhase;
    [SerializeField] bool debugIsFading;
    [SerializeField] float debugFadeProgress;
    [SerializeField] int debugRegisteredTintTargetCount;

    readonly HashSet<IDayNightTintTarget> registeredTintTargets = new HashSet<IDayNightTintTarget>();

    LightingState currentState;
    LightingState fadeStartState;
    LightingState fadeTargetState;
    DayNightPhase targetPhase;
    bool isFading;
    float fadeElapsedSeconds;
    bool hasAppliedInitialState;
    bool hasWarnedMissingReferences;

    void OnEnable()
    {
        if (ActiveController != null && ActiveController != this)
            Debug.LogWarning("[DayNightLightingController] Multiple active lighting controllers detected. The newest one will become active.", this);

        ActiveController = this;
        SubscribeToController();
        RegisterExistingTintTargets();
    }

    IEnumerator Start()
    {
        // Let DayNightController finish its own Start-time initialization first
        // so the initial visual snap reads the real configured startup phase.
        yield return null;

        if (!ValidateReferences())
            yield break;

        SnapToCurrentControllerPhase();
    }

    void Update()
    {
        if (!Application.isPlaying)
            return;

        if (!ValidateReferences())
            return;

        RefreshRuntimePresetChanges();

        if (!isFading)
        {
            UpdateDebugMirrors();
            return;
        }

        float duration = Mathf.Max(fadeDurationSeconds, 0.01f);
        fadeElapsedSeconds += Time.unscaledDeltaTime;

        float linearProgress = Mathf.Clamp01(fadeElapsedSeconds / duration);
        float easedProgress = Mathf.SmoothStep(0f, 1f, linearProgress);

        currentState = LerpState(fadeStartState, fadeTargetState, easedProgress);
        ApplyState(currentState);

        if (linearProgress >= 1f)
        {
            currentState = fadeTargetState;
            ApplyState(currentState);
            isFading = false;
            fadeElapsedSeconds = duration;
        }

        UpdateDebugMirrors();
    }

    void OnDisable()
    {
        UnsubscribeFromController();

        if (ActiveController == this)
            ActiveController = null;
    }

    public void RegisterTintTarget(IDayNightTintTarget tintTarget)
    {
        if (tintTarget == null)
            return;

        registeredTintTargets.Add(tintTarget);

        if (hasAppliedInitialState)
            tintTarget.ApplyTint(currentState.spriteTintColor, currentState.spriteTintBrightness);

        UpdateDebugMirrors();
    }

    public void UnregisterTintTarget(IDayNightTintTarget tintTarget)
    {
        if (tintTarget == null)
            return;

        registeredTintTargets.Remove(tintTarget);
        UpdateDebugMirrors();
    }

    void HandlePhaseChanged(DayNightPhase previousPhase, DayNightPhase newPhase)
    {
        if (!Application.isPlaying)
            return;

        if (!ValidateReferences())
            return;

        BeginFadeToPhase(newPhase);
    }

    void SubscribeToController()
    {
        if (dayNightController == null)
            return;

        dayNightController.OnPhaseChanged -= HandlePhaseChanged;
        dayNightController.OnPhaseChanged += HandlePhaseChanged;
    }

    void UnsubscribeFromController()
    {
        if (dayNightController == null)
            return;

        dayNightController.OnPhaseChanged -= HandlePhaseChanged;
    }

    void RegisterExistingTintTargets()
    {
        foreach (DayNightTintGroup tintGroup in DayNightTintGroup.ActiveTintGroups)
            RegisterTintTarget(tintGroup);

        DayNightTilemapTint[] tilemapTints = FindObjectsByType<DayNightTilemapTint>(FindObjectsInactive.Include);
        foreach (DayNightTilemapTint tilemapTint in tilemapTints)
            RegisterTintTarget(tilemapTint);
    }

    bool ValidateReferences()
    {
        bool hasCoreReferences = dayNightController != null && globalLight2D != null;
        bool hasCameraReference = !affectCameraBackground || targetCamera != null;

        if (hasCoreReferences && hasCameraReference)
            return true;

        if (!hasWarnedMissingReferences)
        {
            hasWarnedMissingReferences = true;
            Debug.LogWarning(
                "[DayNightLightingController] Missing required reference. Assign DayNightController, Global Light 2D, and Camera when background tinting is enabled.",
                this);
        }

        return false;
    }

    void SnapToCurrentControllerPhase()
    {
        targetPhase = dayNightController.CurrentPhase;
        currentState = GetStateForPhase(targetPhase);
        fadeStartState = currentState;
        fadeTargetState = currentState;
        isFading = false;
        fadeElapsedSeconds = 0f;
        hasAppliedInitialState = true;

        ApplyState(currentState);
        UpdateDebugMirrors();
    }

    void BeginFadeToPhase(DayNightPhase newPhase)
    {
        if (!hasAppliedInitialState)
        {
            SnapToCurrentControllerPhase();
            return;
        }

        targetPhase = newPhase;
        fadeStartState = currentState;
        fadeTargetState = GetStateForPhase(newPhase);
        fadeElapsedSeconds = 0f;
        isFading = true;

        if (fadeDurationSeconds <= 0f)
        {
            currentState = fadeTargetState;
            ApplyState(currentState);
            isFading = false;
        }

        UpdateDebugMirrors();
    }

    void RefreshRuntimePresetChanges()
    {
        DayNightPhase activePhase = targetPhase;
        if (!hasAppliedInitialState && dayNightController != null)
            activePhase = dayNightController.CurrentPhase;

        LightingState desiredState = GetStateForPhase(activePhase);

        if (isFading)
        {
            if (StatesApproximatelyEqual(fadeTargetState, desiredState))
                return;

            // Inspector tuning during play should be immediately visible.
            // Retarget the fade from the currently blended state instead of
            // waiting for a new phase event.
            fadeStartState = currentState;
            fadeTargetState = desiredState;
            fadeElapsedSeconds = 0f;
            targetPhase = activePhase;
            return;
        }

        if (StatesApproximatelyEqual(currentState, desiredState))
            return;

        targetPhase = activePhase;
        currentState = desiredState;
        fadeStartState = desiredState;
        fadeTargetState = desiredState;
        ApplyState(currentState);
    }

    LightingPreset GetLightingPresetForPhase(DayNightPhase phase)
    {
        if (phase == DayNightPhase.Sunrise)
            return sunrisePreset;
        if (phase == DayNightPhase.Day)
            return dayPreset;
        if (phase == DayNightPhase.Sunset)
            return sunsetPreset;
        return nightPreset;
    }

    SpriteTintPreset GetSpriteTintPresetForPhase(DayNightPhase phase)
    {
        if (phase == DayNightPhase.Sunrise)
            return sunriseSpriteTintPreset;
        if (phase == DayNightPhase.Day)
            return daySpriteTintPreset;
        if (phase == DayNightPhase.Sunset)
            return sunsetSpriteTintPreset;
        return nightSpriteTintPreset;
    }

    LightingState GetStateForPhase(DayNightPhase phase)
    {
        LightingPreset lightPreset = GetLightingPresetForPhase(phase);
        SpriteTintPreset spriteTintPreset = GetSpriteTintPresetForPhase(phase);

        LightingState state = new LightingState();
        state.lightColor = OpaqueColor(lightPreset.lightColor);
        state.lightIntensity = Mathf.Clamp01(lightPreset.lightIntensity);
        state.backgroundColor = OpaqueColor(lightPreset.backgroundColor);
        state.spriteTintColor = OpaqueColor(spriteTintPreset.colorMultiplier);
        state.spriteTintBrightness = Mathf.Clamp01(spriteTintPreset.brightnessMultiplier);
        return state;
    }

    LightingState LerpState(LightingState from, LightingState to, float t)
    {
        LightingState state = new LightingState();
        state.lightColor = Color.Lerp(from.lightColor, to.lightColor, t);
        state.lightIntensity = Mathf.Lerp(from.lightIntensity, to.lightIntensity, t);
        state.backgroundColor = Color.Lerp(from.backgroundColor, to.backgroundColor, t);
        state.spriteTintColor = Color.Lerp(from.spriteTintColor, to.spriteTintColor, t);
        state.spriteTintBrightness = Mathf.Lerp(from.spriteTintBrightness, to.spriteTintBrightness, t);
        return state;
    }

    bool StatesApproximatelyEqual(LightingState a, LightingState b)
    {
        if (!ColorsApproximatelyEqual(a.lightColor, b.lightColor))
            return false;
        if (!Mathf.Approximately(a.lightIntensity, b.lightIntensity))
            return false;
        if (!ColorsApproximatelyEqual(a.backgroundColor, b.backgroundColor))
            return false;
        if (!ColorsApproximatelyEqual(a.spriteTintColor, b.spriteTintColor))
            return false;
        if (!Mathf.Approximately(a.spriteTintBrightness, b.spriteTintBrightness))
            return false;

        return true;
    }

    bool ColorsApproximatelyEqual(Color a, Color b)
    {
        if (!Mathf.Approximately(a.r, b.r))
            return false;
        if (!Mathf.Approximately(a.g, b.g))
            return false;
        if (!Mathf.Approximately(a.b, b.b))
            return false;
        if (!Mathf.Approximately(a.a, b.a))
            return false;

        return true;
    }

    void ApplyState(LightingState state)
    {
        globalLight2D.color = OpaqueColor(state.lightColor);
        globalLight2D.intensity = Mathf.Clamp01(state.lightIntensity);

        if (affectCameraBackground)
            targetCamera.backgroundColor = OpaqueColor(state.backgroundColor);

        ApplyTintToTargets(state.spriteTintColor, state.spriteTintBrightness);
    }

    void ApplyTintToTargets(Color spriteTintColor, float spriteTintBrightness)
    {
        foreach (IDayNightTintTarget tintTarget in registeredTintTargets)
        {
            if (tintTarget == null)
                continue;

            tintTarget.ApplyTint(spriteTintColor, spriteTintBrightness);
        }
    }

    Color OpaqueColor(Color color)
    {
        color.a = 1f;
        return color;
    }

    void UpdateDebugMirrors()
    {
        if (!Application.isPlaying)
            return;

        debugTargetPhase = targetPhase;
        debugIsFading = isFading;
        debugRegisteredTintTargetCount = registeredTintTargets.Count;

        if (!isFading)
        {
            debugFadeProgress = 1f;
            return;
        }

        float duration = Mathf.Max(fadeDurationSeconds, 0.01f);
        debugFadeProgress = Mathf.Clamp01(fadeElapsedSeconds / duration);
    }

    // TODO: If a dedicated water backdrop sprite replaces camera clear color,
    // move the background-color target from Camera to that world object here.
    // TODO: Future passes can add optional per-phase fog/post-processing hooks
    // without expanding the time controller itself.
}
