using System;
using UnityEngine.InputSystem;

public enum BoatControlScheme
{
    WASD = 0,
    IJKL = 1,
    ArrowKeys = 2
}

public static class GameRuntimeSettings
{
    public static event Action SettingsChanged;

    static BoatControlScheme boatControlScheme = BoatControlScheme.WASD;
    static float masterVolume01 = 1f;
    static float musicVolume01 = 1f;
    static float sfxVolume01 = 1f;
    static float ambienceVolume01 = 1f;
    static string worldgenSeedInput = string.Empty;

    // Raw text the player typed in the Worldgen settings seed field. Session-only,
    // like every other setting here. Blank means "random world on New Game".
    public static string WorldgenSeedInput
    {
        get => worldgenSeedInput;
        set
        {
            string sanitized = value ?? string.Empty;
            if (worldgenSeedInput == sanitized)
                return;

            worldgenSeedInput = sanitized;
            SettingsChanged?.Invoke();
        }
    }

    // Maps the typed seed to a concrete world seed. An integer is used verbatim;
    // any other non-blank text is mapped through a stable hash so the same text
    // always produces the same world; blank text requests a random world.
    public static bool TryResolveWorldgenSeed(out int seed)
    {
        seed = 0;
        string trimmed = worldgenSeedInput?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return false;

        if (int.TryParse(trimmed, out int parsed))
        {
            seed = parsed;
            return true;
        }

        seed = StableStringHash(trimmed);
        return true;
    }

    // Deterministic FNV-1a 32-bit hash. Unlike string.GetHashCode it is stable
    // across runs and platforms, so a text seed always reproduces the same world.
    static int StableStringHash(string text)
    {
        unchecked
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            uint hash = offsetBasis;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= prime;
            }

            return (int)hash;
        }
    }

    public static BoatControlScheme CurrentBoatControlScheme
    {
        get => boatControlScheme;
        set
        {
            if (boatControlScheme == value)
                return;

            boatControlScheme = value;
            SettingsChanged?.Invoke();
        }
    }

    public static float MasterVolume01
    {
        get => masterVolume01;
        set
        {
            float clamped = UnityEngine.Mathf.Clamp01(value);
            if (UnityEngine.Mathf.Approximately(masterVolume01, clamped))
                return;

            masterVolume01 = clamped;
            SettingsChanged?.Invoke();
        }
    }

    public static float MusicVolume01
    {
        get => musicVolume01;
        set
        {
            float clamped = UnityEngine.Mathf.Clamp01(value);
            if (UnityEngine.Mathf.Approximately(musicVolume01, clamped))
                return;

            musicVolume01 = clamped;
            SettingsChanged?.Invoke();
        }
    }

    public static float SfxVolume01
    {
        get => sfxVolume01;
        set
        {
            float clamped = UnityEngine.Mathf.Clamp01(value);
            if (UnityEngine.Mathf.Approximately(sfxVolume01, clamped))
                return;

            sfxVolume01 = clamped;
            SettingsChanged?.Invoke();
        }
    }

    public static float AmbienceVolume01
    {
        get => ambienceVolume01;
        set
        {
            float clamped = UnityEngine.Mathf.Clamp01(value);
            if (UnityEngine.Mathf.Approximately(ambienceVolume01, clamped))
                return;

            ambienceVolume01 = clamped;
            SettingsChanged?.Invoke();
        }
    }

    public static float GetMusicBusVolume()
    {
        return masterVolume01 * musicVolume01;
    }

    public static float GetSfxBusVolume()
    {
        return masterVolume01 * sfxVolume01;
    }

    public static float GetAmbienceBusVolume()
    {
        return masterVolume01 * ambienceVolume01;
    }

    public static bool IsTurnLeftPressed(Keyboard keyboard)
    {
        if (keyboard == null)
            return false;

        return CurrentBoatControlScheme switch
        {
            BoatControlScheme.WASD => keyboard.aKey.isPressed,
            BoatControlScheme.IJKL => keyboard.jKey.isPressed,
            BoatControlScheme.ArrowKeys => keyboard.leftArrowKey.isPressed,
            _ => keyboard.aKey.isPressed
        };
    }

    public static bool IsTurnRightPressed(Keyboard keyboard)
    {
        if (keyboard == null)
            return false;

        return CurrentBoatControlScheme switch
        {
            BoatControlScheme.WASD => keyboard.dKey.isPressed,
            BoatControlScheme.IJKL => keyboard.lKey.isPressed,
            BoatControlScheme.ArrowKeys => keyboard.rightArrowKey.isPressed,
            _ => keyboard.dKey.isPressed
        };
    }

    public static bool WasLowerSailPressedThisFrame(Keyboard keyboard)
    {
        if (keyboard == null)
            return false;

        return CurrentBoatControlScheme switch
        {
            BoatControlScheme.WASD => keyboard.wKey.wasPressedThisFrame,
            BoatControlScheme.IJKL => keyboard.iKey.wasPressedThisFrame,
            BoatControlScheme.ArrowKeys => keyboard.upArrowKey.wasPressedThisFrame,
            _ => keyboard.wKey.wasPressedThisFrame
        };
    }

    public static bool WasRaiseSailPressedThisFrame(Keyboard keyboard)
    {
        if (keyboard == null)
            return false;

        return CurrentBoatControlScheme switch
        {
            BoatControlScheme.WASD => keyboard.sKey.wasPressedThisFrame,
            BoatControlScheme.IJKL => keyboard.kKey.wasPressedThisFrame,
            BoatControlScheme.ArrowKeys => keyboard.downArrowKey.wasPressedThisFrame,
            _ => keyboard.sKey.wasPressedThisFrame
        };
    }

    public static bool IsRaiseSailHeld(Keyboard keyboard)
    {
        if (keyboard == null)
            return false;

        return CurrentBoatControlScheme switch
        {
            BoatControlScheme.WASD => keyboard.sKey.isPressed,
            BoatControlScheme.IJKL => keyboard.kKey.isPressed,
            BoatControlScheme.ArrowKeys => keyboard.downArrowKey.isPressed,
            _ => keyboard.sKey.isPressed
        };
    }

    public static string GetLowerSailKeyLabel()
    {
        return CurrentBoatControlScheme switch
        {
            BoatControlScheme.WASD => "W",
            BoatControlScheme.IJKL => "I",
            BoatControlScheme.ArrowKeys => "Up",
            _ => "W"
        };
    }

    public static string GetRaiseSailKeyLabel()
    {
        return CurrentBoatControlScheme switch
        {
            BoatControlScheme.WASD => "S",
            BoatControlScheme.IJKL => "K",
            BoatControlScheme.ArrowKeys => "Down",
            _ => "S"
        };
    }

    public static string GetSteerKeyLabel()
    {
        return CurrentBoatControlScheme switch
        {
            BoatControlScheme.WASD => "A / D",
            BoatControlScheme.IJKL => "J / L",
            BoatControlScheme.ArrowKeys => "Left / Right",
            _ => "A / D"
        };
    }

    public static string BuildSailingControlsText()
    {
        return $"{GetLowerSailKeyLabel()}: Lower sail\n" +
               $"{GetRaiseSailKeyLabel()}: Raise sail / paddle when fully furled\n" +
               $"{GetSteerKeyLabel()}: Steer";
    }

    public static string BuildCombatControlsText()
    {
        return "Space: Fire cannons\n" +
               "Shift + Space: Fire both sides\n" +
               "Left Click: Fire equipped gun";
    }

    public static string BuildMenuControlsText()
    {
        return "E: Inventory\n" +
               "M: Map\n" +
               "Q: Dockside shop\n" +
               "F: Start fishing\n" +
               "C: Fishing catch timing\n" +
               "Esc: Back / pause\n" +
               "Mouse Wheel / Drag: Zoom / pan map";
    }

    public static string GetBoatControlSchemeLabel(BoatControlScheme scheme)
    {
        return scheme switch
        {
            BoatControlScheme.WASD => "WASD",
            BoatControlScheme.IJKL => "IJKL",
            BoatControlScheme.ArrowKeys => "Arrow Keys",
            _ => "WASD"
        };
    }
}
