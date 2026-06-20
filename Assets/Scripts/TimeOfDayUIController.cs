using UnityEngine;
using UnityEngine.UIElements;

// Drives the time-of-day HUD icon.
//
// This controller reads the current phase from DayNightController and swaps the
// UI Toolkit background sprite on the dedicated icon element. The layout lives
// in UXML/USS; this script only resolves references and assigns the correct
// sprite for Sunrise / Day / Sunset / Night.
public class TimeOfDayUIController : MonoBehaviour
{
    // The scene UIDocument hosting GameUI. Auto-found if left unassigned.
    [SerializeField] UIDocument uiDocument;

    // Source of truth for the current time-of-day phase. Auto-found if left unassigned.
    [SerializeField] DayNightController dayNightController;

    // Optional weather source. When rain is active, the icon uses the rain variant
    // for the current day/night phase.
    [SerializeField] WeatherController weatherController;

    // Name of the UI Toolkit element whose background image should be swapped.
    [SerializeField] string timeOfDayIconElementName = "time-of-day-icon";

    // Phase sprites assigned in the Inspector so sliced sprite assets resolve
    // to the exact intended sprite instead of relying on USS texture URLs.
    [SerializeField] Sprite sunriseSprite;
    [SerializeField] Sprite daySprite;
    [SerializeField] Sprite sunsetSprite;
    [SerializeField] Sprite nightSprite;

    [Header("Rain Variants")]
    [SerializeField] Sprite rainSunriseSprite;
    [SerializeField] Sprite rainDaySprite;
    [SerializeField] Sprite rainSunsetSprite;
    [SerializeField] Sprite rainNightSprite;

    VisualElement timeOfDayIcon;
    bool isDayNightSubscribed;
    bool isWeatherSubscribed;
    bool warnedMissingIcon;
    bool warnedMissingController;
    bool warnedMissingSprite;

    // Cached so Update can refresh safely without reassigning the same sprite every frame.
    DayNightPhase? lastAppliedPhase;
    WeatherState? lastAppliedWeather;

    void OnEnable()
    {
        TryInitialize();
    }

    void Start()
    {
        TryInitialize();
        RefreshIcon();
    }

    void Update()
    {
        TryInitialize();
        RefreshIcon();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void TryInitialize()
    {
        if (uiDocument == null)
            uiDocument = FindAnyObjectByType<UIDocument>();

        if (dayNightController == null)
            dayNightController = FindAnyObjectByType<DayNightController>();

        if (weatherController == null)
            weatherController = WeatherController.ActiveInstance != null ? WeatherController.ActiveInstance : FindAnyObjectByType<WeatherController>();

        if (uiDocument != null && timeOfDayIcon == null)
            timeOfDayIcon = uiDocument.rootVisualElement.Q(timeOfDayIconElementName);

        if (timeOfDayIcon == null && !warnedMissingIcon)
        {
            Debug.LogWarning("TimeOfDayUIController could not find the time-of-day icon element.");
            warnedMissingIcon = true;
        }

        if (dayNightController == null && !warnedMissingController)
        {
            Debug.LogWarning("TimeOfDayUIController could not find a DayNightController.");
            warnedMissingController = true;
        }

        if (dayNightController != null && !isDayNightSubscribed)
        {
            dayNightController.OnPhaseChanged += HandlePhaseChanged;
            isDayNightSubscribed = true;
        }

        if (weatherController != null && !isWeatherSubscribed)
        {
            weatherController.OnWeatherChanged += HandleWeatherChanged;
            isWeatherSubscribed = true;
        }
    }

    void Unsubscribe()
    {
        if (dayNightController != null && isDayNightSubscribed)
            dayNightController.OnPhaseChanged -= HandlePhaseChanged;

        if (weatherController != null && isWeatherSubscribed)
            weatherController.OnWeatherChanged -= HandleWeatherChanged;

        isDayNightSubscribed = false;
        isWeatherSubscribed = false;
    }

    void HandlePhaseChanged(DayNightPhase previousPhase, DayNightPhase currentPhase)
    {
        RefreshIcon();
    }

    void HandleWeatherChanged(WeatherState previousWeather, WeatherState currentWeather)
    {
        RefreshIcon();
    }

    void RefreshIcon()
    {
        if (timeOfDayIcon == null || dayNightController == null)
            return;

        WeatherState currentWeather = weatherController != null ? weatherController.CurrentWeather : WeatherState.Clear;

        // Startup order can vary, so keep checking until DayNightController has
        // published its real configured phase instead of the enum default.
        if (lastAppliedPhase.HasValue
            && lastAppliedPhase.Value == dayNightController.CurrentPhase
            && lastAppliedWeather.HasValue
            && lastAppliedWeather.Value == currentWeather)
            return;

        Sprite sprite = GetSpriteForPhase(dayNightController.CurrentPhase, currentWeather);
        if (sprite == null)
        {
            if (!warnedMissingSprite)
            {
                Debug.LogWarning("TimeOfDayUIController is missing one or more phase sprites.");
                warnedMissingSprite = true;
            }
            return;
        }

        timeOfDayIcon.style.backgroundImage = new StyleBackground(sprite);
        lastAppliedPhase = dayNightController.CurrentPhase;
        lastAppliedWeather = currentWeather;
    }

    Sprite GetSpriteForPhase(DayNightPhase phase, WeatherState weather)
    {
        if (weather == WeatherState.Rainfall)
        {
            Sprite rainSprite = GetRainSpriteForPhase(phase);
            if (rainSprite != null)
                return rainSprite;
        }

        if (phase == DayNightPhase.Sunrise)
            return sunriseSprite;
        if (phase == DayNightPhase.Day)
            return daySprite;
        if (phase == DayNightPhase.Sunset)
            return sunsetSprite;
        return nightSprite;
    }

    Sprite GetRainSpriteForPhase(DayNightPhase phase)
    {
        if (phase == DayNightPhase.Sunrise)
            return rainSunriseSprite;
        if (phase == DayNightPhase.Day)
            return rainDaySprite;
        if (phase == DayNightPhase.Sunset)
            return rainSunsetSprite;
        return rainNightSprite;
    }
}
