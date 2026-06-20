using UnityEngine;
using UnityEngine.InputSystem;

// Debug-only helper for the day/night clock.
//
// Remove or gate this later for production builds.
// For this pass it only supports the agreed hold-to-fast-forward input.
public class DayNightDebugUIController : MonoBehaviour
{
    [SerializeField] DayNightController dayNightController;
    [SerializeField] float fastForwardTimeScale = 30f;

    bool hasWarnedMissingController;

    void Update()
    {
        if (dayNightController == null)
        {
            WarnMissingControllerOnce();
            return;
        }

        if (dayNightController.IsPaused)
        {
            dayNightController.ClearTemporaryTimeScaleOverride();
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            dayNightController.ClearTemporaryTimeScaleOverride();
            return;
        }

        bool isFastForwardHeld = keyboard.equalsKey.isPressed || keyboard.numpadPlusKey.isPressed;
        if (isFastForwardHeld)
            dayNightController.SetTemporaryTimeScaleOverride(fastForwardTimeScale);
        else
            dayNightController.ClearTemporaryTimeScaleOverride();
    }

    void WarnMissingControllerOnce()
    {
        if (hasWarnedMissingController)
            return;

        hasWarnedMissingController = true;
        Debug.LogWarning("[DayNightDebugUIController] Missing DayNightController reference.", this);
    }
}
