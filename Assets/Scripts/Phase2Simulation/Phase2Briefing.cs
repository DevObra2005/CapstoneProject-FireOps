using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Plays the BFP officer's intro dialogue the moment Phase 2
// loads, then starts the simulation timer when the player
// taps "GOT IT" on the last line.
//
// WHY IT EXISTS:
// SimulationManager used to start its 90-second countdown the
// instant the scene loaded. That meant the clock was already
// draining while the player was still reading what to do.
// This script holds the clock until the briefing is dismissed.
//
// HOW IT CONNECTS:
// DialogueManager.StartDialogue takes a callback that fires
// after the panel slides away. We hand it StartTimer, so the
// countdown begins at exactly the right moment.
//
// SETUP:
// 1. Create an empty GameObject in the Phase 2 scene
// 2. Attach this script to it
// 3. Fill in Briefing Lines in the Inspector (5 lines)
// -------------------------------------------------------

public class Phase2Briefing : MonoBehaviour
{
    [Header("Briefing Lines")]
    [Tooltip("The BFP officer's intro — plays once when Phase 2 starts")]
    public DialogueLine[] briefingLines;

    [Header("Timing")]
    [Tooltip("Short pause before the dialogue appears, so the scene has settled")]
    public float startDelay = 0.5f;

    // Guard so the briefing can never play twice.
    private bool hasPlayed = false;

    private void Start()
    {
        // Only run in Phase 2. During Phase 1 this object does
        // nothing at all — we switch the script off entirely so
        // it isn't wasting an Update call.
        if (PlayerPrefs.GetInt("SimulationMode", 0) != 1)
        {
            enabled = false;
            return;
        }

        // Invoke runs a method after a delay, by name. Same idea
        // as a short setTimeout — gives the scene a moment to
        // finish loading before the panel slides in.
        Invoke(nameof(PlayBriefing), startDelay);
    }

    private void PlayBriefing()
    {
        if (hasPlayed) return;
        hasPlayed = true;

        // FALLBACK: no DialogueManager in the scene. Rather than
        // freezing the game with a timer that never starts, we
        // skip the briefing and begin immediately.
        if (DialogueManager.Instance == null)
        {
            Debug.LogWarning("[Phase2Briefing] No DialogueManager found — starting simulation immediately.");
            StartTimer();
            return;
        }

        // FALLBACK: lines were never filled in on the Inspector.
        // Same reasoning — don't strand the player.
        if (briefingLines == null || briefingLines.Length == 0)
        {
            Debug.LogWarning("[Phase2Briefing] No briefing lines assigned — starting simulation immediately.");
            StartTimer();
            return;
        }

        // The third argument (true) marks this as a completion
        // dialogue, which makes the final button read "GOT IT"
        // instead of "DO IT" — correct here, since the player
        // isn't being sent to tap a specific object.
        DialogueManager.Instance.StartDialogue(
            briefingLines,
            StartTimer,
            true,
            "START");
    }

    // Called by DialogueManager once the panel has slid away.
    // Called by DialogueManager once the panel has slid away.
    private void StartTimer()
    {
        // Music starts here rather than on scene load, so the officer's
        // briefing is heard against silence. Fired before the timer so
        // the fade-in has already begun when the countdown starts.
        //
        // Null-conditional: if a scene has no SceneAudioProfile (Kitchen,
        // Classroom before they are set up), this is a silent no-op
        // rather than a crash.
        SceneAudioProfile.Instance?.BeginPhaseAudio();

        if (SimulationManager.Instance == null)
        {
            Debug.LogError("[Phase2Briefing] No SimulationManager in scene — timer cannot start!");
            return;
        }
        SimulationManager.Instance.BeginSimulation();
    }
}