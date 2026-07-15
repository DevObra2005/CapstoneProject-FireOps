using UnityEngine;

public class GameModeManager : MonoBehaviour
{
    // -------------------------------------------------------
    // WHAT THIS DOES:
    // Checks PlayerPrefs flag on scene load and decides
    // whether to run Phase 1 (identification) or
    // Phase 2 (simulation).
    // SimulationMode = 0 -> Phase 1
    // SimulationMode = 1 -> Phase 2
    // The flag is SET by CompletionManager when all hazards
    // are found, and RESET by SimulationManager when Phase 2 ends.
    // -------------------------------------------------------

    [Header("Phase 1 Objects (Hazard Identification)")]
    public GameObject phase1UI;
    public GameObject hazardObjects;

    [Header("Phase 2 Objects (Simulation)")]
    public GameObject phase2UI;
    public GameObject simulationSystem;

    // The FPS hands. They live under PlayerCamera (so they can't
    // sit inside simulationSystem), so GameModeManager controls them
    // directly here. Hidden in Phase 1, shown in Phase 2.
    public GameObject handRig;

    // NEW: the Phase2Objects container (holds the Phase 2 extinguisher
    // and the office fire). These must NOT appear during Phase 1, so
    // we hide the whole container in Phase 1 and show it in Phase 2.
    public GameObject phase2Objects;

    // -------------------------------------------------------
    // This is a static bool - it lives in memory only.
    // It is NOT saved to disk like PlayerPrefs.
    // When the app starts fresh, it is always false.
    // CompletionManager sets it to true right before the
    // scene reloads into Phase 2.
    // This lets us tell the difference between:
    //   - a legitimate Phase 2 transition (intentionalTransition = true)
    //   - a leftover PlayerPrefs value from a crash or Editor stop
    // -------------------------------------------------------
    public static bool intentionalTransition = false;

    private void Awake()
    {
        // If the scene loaded WITHOUT an intentional transition,
        // it means the game launched fresh (or crashed last time).
        // Reset SimulationMode to 0 so we always start in Phase 1.
        if (!intentionalTransition)
        {
            PlayerPrefs.SetInt("SimulationMode", 0);
            PlayerPrefs.Save();
        }

        // Always reset the flag after reading it.
        // Next scene load will be treated as fresh unless
        // CompletionManager sets it to true again.
        intentionalTransition = false;
    }

    private void Start()
    {
        int simulationMode = PlayerPrefs.GetInt("SimulationMode", 0);

        if (simulationMode == 0)
        {
            Debug.Log("[GameModeManager] Phase 1: Hazard Identification");
            StartPhase1();
        }
        else
        {
            Debug.Log("[GameModeManager] Phase 2: Fire Simulation");
            StartPhase2();
        }
    }

    void StartPhase1()
    {
        if (phase1UI != null) phase1UI.SetActive(true);
        if (hazardObjects != null) hazardObjects.SetActive(true);

        if (phase2UI != null) phase2UI.SetActive(false);
        if (simulationSystem != null) simulationSystem.SetActive(false);

        // Hide the hands during hazard identification.
        if (handRig != null) handRig.SetActive(false);

        // NEW: hide the Phase 2 objects (fire + Phase 2 extinguisher).
        if (phase2Objects != null) phase2Objects.SetActive(false);
    }

    void StartPhase2()
    {
        if (phase1UI != null) phase1UI.SetActive(false);
        if (hazardObjects != null) hazardObjects.SetActive(false);

        if (phase2UI != null) phase2UI.SetActive(true);
        if (simulationSystem != null) simulationSystem.SetActive(true);

        // Show the hands during the simulation.
        if (handRig != null) handRig.SetActive(true);

        // NEW: show the Phase 2 objects (fire + Phase 2 extinguisher).
        if (phase2Objects != null) phase2Objects.SetActive(true);
    }

    // Called by SimulationManager.EndSimulation() when Phase 2 ends.
    public void ResetToPhase1()
    {
        PlayerPrefs.SetInt("SimulationMode", 0);
        PlayerPrefs.Save();
        Debug.Log("[GameModeManager] SimulationMode reset to 0.");
    }
}