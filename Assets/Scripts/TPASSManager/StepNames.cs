// -------------------------------------------------------
// StepNames.cs
// Two lookups, one place:
//   Friendly(raw)  -> label for CORRECT rows + results screen
//   Hint(stepNum)  -> instructional "do this now" line for WRONG rows
//
// Hint is keyed by the STEP NUMBER (1-8), because the wrong-action
// message should point at the step the player is CURRENTLY on
// (SimulationManager.CurrentStep), not the button they mis-tapped.
//
// Keep Hint lines short — the log row trims long text with "…".
// -------------------------------------------------------
public static class StepNames
{
    // Used by correct rows + results screen (keyed by enum name).
    public static string Friendly(string raw)
    {
        switch (raw)
        {
            case "SoundAlarm": return "Sound the Alarm";
            case "GrabExtinguisher": return "Grab Extinguisher";
            case "TPASS_Twist": return "Twist the Pin";
            case "TPASS_Pull": return "Pull the Pin";
            case "TPASS_Aim": return "Aim at the Base";
            case "TPASS_Squeeze": return "Squeeze the Handle";
            case "TPASS_Sweep": return "Sweep Side to Side";
            case "Evacuate": return "Evacuate";

            case "WetBlanket": return "Cover with Wet Blanket";
            case "TurnOffLPG": return "Turn Off the LPG";

            default: return raw;
        }
    }

    // Used by wrong rows (keyed by current step number 1-8).
    // Instructional, direct — tells the player what to actually do.
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
}