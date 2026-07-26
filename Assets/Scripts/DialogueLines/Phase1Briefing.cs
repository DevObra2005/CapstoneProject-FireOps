using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Plays the BFP officer's introduction the moment Phase 1
// loads — before the player starts hunting for hazards.
//
// WHY IT EXISTS:
// Phase 1 used to drop the player into the office with no
// context at all. This gives them the same officer briefing
// Phase 2 has, so both phases open the same way.
//
// HOW IT DIFFERS FROM Phase2Briefing:
// Phase 2's briefing starts a 90-second timer when dismissed.
// Phase 1 has no timer, so the callback does nothing — the
// player is simply free to explore once the panel slides away.
//
// SETUP:
// 1. Create an empty GameObject in the Phase 1 scene
// 2. Attach this script to it
// 3. Fill in Briefing Lines in the Inspector (3 lines)
// -------------------------------------------------------

public class Phase1Briefing : MonoBehaviour
{
    [Header("Briefing Lines")]
    [Tooltip("The BFP officer's introduction — plays once when Phase 1 starts")]
    public DialogueLine[] briefingLines;

    [Header("Timing")]
    [Tooltip("Short pause before the dialogue appears, so the scene has settled")]
    public float startDelay = 0.5f;

    // Guard so the briefing can never play twice.
    private bool hasPlayed = false;

    private void Start()
    {
        // Only run in Phase 1. If SimulationMode is 1 we're in
        // Phase 2, so this object switches itself off entirely.
        if (PlayerPrefs.GetInt("SimulationMode", 0) == 1)
        {
            enabled = false;
            return;
        }

        // Invoke runs a method after a delay, by name. Gives the
        // scene a moment to finish loading before the panel
        // slides in — same as Phase2Briefing.
        Invoke(nameof(PlayBriefing), startDelay);
    }

    private void PlayBriefing()
    {
        if (hasPlayed) return;
        hasPlayed = true;

        // FALLBACK: no DialogueManager in the scene. Nothing to
        // play, so we just let the player start exploring.
        if (DialogueManager.Instance == null)
        {
            Debug.LogWarning("[Phase1Briefing] No DialogueManager found — skipping briefing.");
            return;
        }

        // FALLBACK: lines were never filled in on the Inspector.
        if (briefingLines == null || briefingLines.Length == 0)
        {
            Debug.LogWarning("[Phase1Briefing] No briefing lines assigned — skipping briefing.");
            return;
        }

        // false = not a completion dialogue, so the final button
        // reads "DO IT" — correct here, since the player is being
        // sent off to go and find the hazards.
        DialogueManager.Instance.StartDialogue(
            briefingLines,
            OnBriefingComplete,
            false);
    }

    // Called by DialogueManager once the panel has slid away.
    // Phase 1 has no timer to start, so there is nothing to do
    // here — the player is already free to move, since
    // HazardInteractionManager and FPSMobileController both
    // unblock automatically once IsDialogueActive() goes false.
    private void OnBriefingComplete()
    {
        Debug.Log("[Phase1Briefing] Briefing finished — player can now explore.");
    }
}