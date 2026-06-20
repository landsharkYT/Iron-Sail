using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum WeatherState
{
    Clear,
    Rainfall
}

public class WeatherController : MonoBehaviour
{
    public static WeatherController ActiveInstance { get; private set; }

    [Header("Startup")]
    [SerializeField] WeatherState startWeather = WeatherState.Clear;
    [SerializeField, Range(0f, 1f)] float startStateProgress = 0f;
    [SerializeField, Min(0f)] float rainIntensity = 1f;

    [Header("Dormant Schedule")]
    [SerializeField] bool scheduleEnabled = false;
    [SerializeField, Min(0.1f)] float clearDurationMinSeconds = 180f;
    [SerializeField, Min(0.1f)] float clearDurationMaxSeconds = 420f;
    [SerializeField, Min(0.1f)] float rainDurationMinSeconds = 60f;
    [SerializeField, Min(0.1f)] float rainDurationMaxSeconds = 180f;

    [Header("Debug")]
    [SerializeField] bool enableDebugHotkeys = true;

    [Header("Runtime Debug")]
    [SerializeField] WeatherState debugCurrentWeather;
    [SerializeField] float debugWeatherIntensity;
    [SerializeField] float debugStateProgress;
    [SerializeField] float debugTimeRemainingInState;
    [SerializeField] bool debugScheduleEnabled;

    WeatherState currentWeather;
    float stateDurationSeconds;
    float elapsedSecondsInState;
    bool initialized;

    public event Action<WeatherState, WeatherState> OnWeatherChanged;

    public WeatherState CurrentWeather => currentWeather;
    public float WeatherIntensity => currentWeather == WeatherState.Rainfall ? rainIntensity : 0f;
    public float RainIntensity => rainIntensity;
    public float StateProgress => stateDurationSeconds <= 0f ? 0f : Mathf.Clamp01(elapsedSecondsInState / stateDurationSeconds);
    public float TimeRemainingInState => Mathf.Max(0f, stateDurationSeconds - elapsedSecondsInState);
    public bool IsScheduleEnabled => scheduleEnabled;

    void Awake()
    {
        if (ActiveInstance != null && ActiveInstance != this)
            Debug.LogWarning("[WeatherController] Multiple active weather controllers detected. The newest one will become active.", this);

        ActiveInstance = this;
        Initialize();
    }

    void OnEnable()
    {
        ActiveInstance = this;
        Initialize();
    }

    void OnDisable()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;
    }

    void Update()
    {
        Initialize();
        HandleDebugHotkeys();
        UpdateSchedule();
        UpdateDebugValues();
    }

    public void SetWeather(WeatherState weather)
    {
        SetWeather(weather, true);
    }

    public void ToggleRainfall()
    {
        SetWeather(currentWeather == WeatherState.Rainfall ? WeatherState.Clear : WeatherState.Rainfall);
    }

    void Initialize()
    {
        if (initialized)
            return;

        currentWeather = startWeather;
        stateDurationSeconds = RollDurationFor(currentWeather);
        elapsedSecondsInState = Mathf.Clamp01(startStateProgress) * stateDurationSeconds;
        initialized = true;
        UpdateDebugValues();
    }

    void HandleDebugHotkeys()
    {
        if (!enableDebugHotkeys || Keyboard.current == null)
            return;

        if (Keyboard.current.tKey.wasPressedThisFrame)
            ToggleRainfall();
    }

    void UpdateSchedule()
    {
        if (!scheduleEnabled)
            return;

        elapsedSecondsInState += Time.deltaTime;
        if (elapsedSecondsInState < stateDurationSeconds)
            return;

        WeatherState nextWeather = currentWeather == WeatherState.Rainfall ? WeatherState.Clear : WeatherState.Rainfall;
        SetWeather(nextWeather, false);
    }

    void SetWeather(WeatherState weather, bool resetStateTimer)
    {
        Initialize();

        if (currentWeather == weather && !resetStateTimer)
            return;

        WeatherState previousWeather = currentWeather;
        currentWeather = weather;
        stateDurationSeconds = RollDurationFor(currentWeather);
        elapsedSecondsInState = 0f;
        UpdateDebugValues();

        if (previousWeather != currentWeather)
            OnWeatherChanged?.Invoke(previousWeather, currentWeather);
    }

    float RollDurationFor(WeatherState weather)
    {
        if (weather == WeatherState.Rainfall)
            return UnityEngine.Random.Range(rainDurationMinSeconds, rainDurationMaxSeconds);

        return UnityEngine.Random.Range(clearDurationMinSeconds, clearDurationMaxSeconds);
    }

    void UpdateDebugValues()
    {
        debugCurrentWeather = currentWeather;
        debugWeatherIntensity = WeatherIntensity;
        debugStateProgress = StateProgress;
        debugTimeRemainingInState = TimeRemainingInState;
        debugScheduleEnabled = scheduleEnabled;
    }

    void OnValidate()
    {
        clearDurationMinSeconds = Mathf.Max(0.1f, clearDurationMinSeconds);
        clearDurationMaxSeconds = Mathf.Max(clearDurationMinSeconds, clearDurationMaxSeconds);
        rainDurationMinSeconds = Mathf.Max(0.1f, rainDurationMinSeconds);
        rainDurationMaxSeconds = Mathf.Max(rainDurationMinSeconds, rainDurationMaxSeconds);
        rainIntensity = Mathf.Max(0f, rainIntensity);
        UpdateDebugValues();
    }
}
