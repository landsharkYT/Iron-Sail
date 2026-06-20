using UnityEngine;

// Gameplay-facing stub for future day/night hooks.
//
// This pass intentionally does not implement any real gameplay behavior.
// Keep this script as an explicit wiring point so later systems can attach
// spawn/combat/night-danger logic without bloating DayNightController.
public class DayNightGameplayController : MonoBehaviour
{
    [SerializeField] DayNightController dayNightController;

    bool hasWarnedMissingController;

    void Start()
    {
        if (dayNightController == null && !hasWarnedMissingController)
        {
            hasWarnedMissingController = true;
            Debug.LogWarning("[DayNightGameplayController] Missing DayNightController reference.", this);
        }
    }

    // TODO: Add future gameplay query points here, such as:
    // - spawn modifiers
    // - aggression modifiers
    // - visibility penalties
    // - night-only encounter hooks
}
