// -------------------------------------------------------
// StepNames.cs
// Two lookups, one place:
//   Friendly(raw)            -> label for CORRECT rows + results screen
//   Hint(stepNum)            -> "do this now" line for WRONG rows (TPASS)
//   Hint(stepNum, isKitchen) -> same, but Kitchen's WCTL sequence
//
// Hint is keyed by the STEP NUMBER, because the wrong-action message should
// point at the step the player is CURRENTLY on (SimulationManager.CurrentStep),
// not the button they mis-tapped.
//
// WHY THE OVERLOAD:
// Office step 3 is "Twist the pin". Kitchen step 3 is "Wet the towel". Same
// integer, completely different instruction - so one lookup cannot serve both.
//
// The single-argument Hint(int) is UNCHANGED and still returns the TPASS
// lines. Office and Classroom call sites need no edits at all.
//
// Keep Hint lines short - the log row trims long text with "...".
// -------------------------------------------------------

public static class StepNames
{
    // Used by correct rows + results screen (keyed by enum name).
    // Both SimStep and KitchenStep names live here - they cannot collide,
    // because no name appears in both enums.
    public static string Friendly(string raw)
    {
        switch (raw)
        {
            // --- Office / Classroom (TPASS) ---
            case "SoundAlarm": return "Sound the Alarm";
            case "GrabExtinguisher": return "Grab Extinguisher";
            case "TPASS_Twist": return "Twist the Pin";
            case "TPASS_Pull": return "Pull the Pin";
            case "TPASS_Aim": return "Aim at the Base";
            case "TPASS_Squeeze": return "Squeeze the Handle";
            case "TPASS_Sweep": return "Sweep Side to Side";
            case "Evacuate": return "Evacuate";

            // --- Kitchen (WCTL) ---
            case "GrabTowel": return "Grab the Towel";
            case "WCTL_Wet": return "Wet the Towel";
            case "WCTL_Cover": return "Cover the Fire";
            case "WCTL_TurnOff": return "Turn Off the LPG Valve";

            // --- Legacy Kitchen names, kept so older scene data still reads ---
            case "WetBlanket": return "Cover with Wet Blanket";
            case "TurnOffLPG": return "Turn Off the LPG";

            default: return raw;
        }
    }

    // -------------------------------------------------------
    // TPASS hints (Office / Classroom). Unchanged.
    // -------------------------------------------------------
    public static string Hint(int step)
    {
        switch (step)
        {
            case 1: return "Sound the alarm first";
            case 2: return "You must grab the fire extinguisher";
            case 3: return "Twist the pin first";
            case 4: return "Pull the pin out";
            case 5: return "Aim at the base of the fire";
            case 6: return "Squeeze the handle";
            case 7: return "Sweep side to side";
            case 8: return "Get out through the exit";
            default: return "Keep going";
        }
    }

    // -------------------------------------------------------
    // Environment-aware hint.
    //
    // SimulationManager passes its own useKitchenHints flag, so the decision
    // lives on the scene's manager rather than being sniffed from the scene
    // name at runtime - one place to look when a hint comes out wrong.
    // -------------------------------------------------------
    public static string Hint(int step, bool isKitchen)
    {
        if (!isKitchen) return Hint(step);

        switch (step)
        {
            case 1: return "Sound the alarm first";
            case 2: return "Pick up the kitchen towel";
            case 3: return "Wet the towel at the sink";
            case 4: return "Cover the flame with the wet towel";
            case 5: return "Shut the valve on the LPG tank";
            case 6: return "Get out through the exit";
            default: return "Keep going";
        }
    }
}