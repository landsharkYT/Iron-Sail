using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Owns the player's survival hunger state.
//
// Full hunger to starvation is authored as a simple resource pool. By default:
//   - MaxHunger = 36
//   - hungerDrainPerInGameHour = 1
//
// That means 36 in-game hours with no food reduces hunger from full to zero.
// The depletion rate is tied directly to DayNightController.EffectiveTimeScale,
// so pause and debug fast-forward affect hunger exactly with the game clock.
public class HungerController : MonoBehaviour
{
    public static event Action Starved;

    [Header("References")]
    [SerializeField] DayNightController dayNightController;

    [Header("Hunger")]

    // Roughly a day and a half to starvation by default.
    [SerializeField] float maxHunger = 36f;

    // Defaults to full, but can be lowered in the Inspector for UI testing.
    [SerializeField] float startingHunger = 36f;

    // Hunger units lost per in-game hour. With the defaults above, 1 means
    // 36 in-game hours from full to zero.
    [SerializeField] float hungerDrainPerInGameHour = 1f;

    [Header("Debug")]

    // Hold 9 to drain hunger for fast testing.
    [SerializeField] float debugDrainPerSecond = 12f;

    // Hold 0 to restore hunger for fast testing.
    [SerializeField] float debugRefillPerSecond = 12f;

    public float MaxHunger => maxHunger;
    public float CurrentHunger => currentHunger;
    public float HungerFraction => maxHunger > 0f ? currentHunger / maxHunger : 0f;
    public bool IsStarved => currentHunger <= 0f;

    // previousHunger, currentHunger
    public event Action<float, float> OnHungerChanged;

    float currentHunger;
    bool warnedMissingDayNightController;
    bool hasEmittedStarved;

    void Awake()
    {
        currentHunger = Mathf.Clamp(startingHunger, 0f, maxHunger);
        hasEmittedStarved = currentHunger <= 0f;
    }

    void Update()
    {
        HandleNaturalDepletion();
        HandleDebugInput();
    }

    void OnValidate()
    {
        maxHunger = Mathf.Max(1f, maxHunger);
        startingHunger = Mathf.Clamp(startingHunger, 0f, maxHunger);
        hungerDrainPerInGameHour = Mathf.Max(0f, hungerDrainPerInGameHour);
        debugDrainPerSecond = Mathf.Max(0f, debugDrainPerSecond);
        debugRefillPerSecond = Mathf.Max(0f, debugRefillPerSecond);
    }

    public void ConsumeFood(float amount)
    {
        if (amount <= 0f)
            return;

        SetHunger(currentHunger + amount);
    }

    public void ReduceHunger(float amount)
    {
        if (amount <= 0f)
            return;

        SetHunger(currentHunger - amount);
    }

    public void SetHunger(float amount)
    {
        float clampedHunger = Mathf.Clamp(amount, 0f, maxHunger);
        if (Mathf.Approximately(clampedHunger, currentHunger))
            return;

        float previousHunger = currentHunger;
        currentHunger = clampedHunger;
        OnHungerChanged?.Invoke(previousHunger, currentHunger);
        if (!hasEmittedStarved && currentHunger <= 0f)
        {
            hasEmittedStarved = true;
            Starved?.Invoke();
        }
    }

    void HandleNaturalDepletion()
    {
        if (dayNightController == null)
        {
            WarnMissingDayNightControllerOnce();
            return;
        }

        float totalDayLengthSeconds = dayNightController.GetPhaseEndSeconds(DayNightPhase.Night);
        if (totalDayLengthSeconds <= 0f)
            return;

        float effectiveTimeScale = dayNightController.EffectiveTimeScale;
        if (effectiveTimeScale <= 0f || hungerDrainPerInGameHour <= 0f)
            return;

        float inGameHoursPerRealSecond = 24f / totalDayLengthSeconds;
        float hungerLoss = Time.deltaTime * effectiveTimeScale * inGameHoursPerRealSecond * hungerDrainPerInGameHour;
        ReduceHunger(hungerLoss);
    }

    void HandleDebugInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.digit9Key.isPressed)
            ReduceHunger(debugDrainPerSecond * Time.deltaTime);

        if (keyboard.digit0Key.isPressed)
            ConsumeFood(debugRefillPerSecond * Time.deltaTime);
    }

    void WarnMissingDayNightControllerOnce()
    {
        if (warnedMissingDayNightController)
            return;

        Debug.LogWarning("HungerController is missing its DayNightController reference.", this);
        warnedMissingDayNightController = true;
    }
}
