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
// Office step 3 is "Twist the pin". Kitchen step 3 is "Cover the flame". Same
// integer, completely different instruction - so one lookup cannot serve both.
//
// The single-argument Hint(int) is UNCHANGED and still returns the TPASS
// lines. Office and Classroom call sites need no edits at all.
//
// Keep Hint lines short - the log row trims long text with "...".
//
// -------------------------------------------------------
// THE KITCHEN TABLE WAS OFF BY ONE. FIXED.
//
// It was written while Kitchen still had a Sound Alarm step, so it started at
// "Sound the alarm first" and ran to six entries. The alarm step was later
// dropped - a home LPG fire starts with smothering, not with raising an alarm -
// and KitchenStep was renumbered to five:
//
//     1 GrabTowel   2 WCTL_Wet   3 WCTL_Cover   4 WCTL_TurnOff   5 Evacuate
//
// The table was not renumbered with it. Every hint pointed at the step BEFORE
// the one the player was actually on: tap the door on step 1 and Kitchen told
// you to sound an alarm that does not exist in that scene. Case 6 was
// unreachable entirely.
//
// Nothing errored. The lookup is by integer and every integer 1-6 had an
// answer, so it returned confidently wrong instructions instead.
//
// IF THE STEP SEQUENCE EVER CHANGES AGAIN, change this table in the same
// commit. There is no compile-time link between KitchenStep and these cases,
// and the failure mode is a training tool teaching the wrong order.
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
    //
    // THE NUMBERS BELOW MUST MATCH KitchenStep. There is no compile-time link
    // between them, so a renumbering that misses this table produces hints that
    // are confidently, silently wrong. Read KitchenStep.cs alongside any edit
    // here.
    // -------------------------------------------------------
    public static string Hint(int step, bool isKitchen)
    {
        if (!isKitchen) return Hint(step);

        switch (step)
        {
            case 1: return "Grab the towel from the sink";
            case 2: return "Wet the towel in the sink";
            case 3: return "Cover the flame with the wet towel";
            case 4: return "Shut the valve on the LPG tank";
            case 5: return "Get out through the exit";
            default: return "Keep going";
        }
    }
}