using UnityEngine;

public class GameModeManager : MonoBehaviour
{
    // -------------------------------------------------------
    // WHAT THIS DOES:
    // Checks PlayerPrefs flag on scene load and decides
    // whether to run Phase 1 (identification) or 
    // Phase 2 (simulation)
    // -------------------------------------------------------

    [Header("Phase 1 Objects (Hazard Identification)")]
    public GameObject phase1UI;         // ObjectivePanel + HazardCounterCanvas
    public GameObject hazardObjects;    // Parent object holding all hazards

    [Header("Phase 2 Objects (Simulation)")]
    public GameObject phase2UI;         // Timer UI, simulation instructions
    public GameObject simulationSystem; // Fire simulation logic (build later)

    private void Start()
    {
        int simulationMode = PlayerPrefs.GetInt("SimulationMode", 0);

        if (simulationMode == 0)
        {
            // Phase 1 — Hazard Identification
            Debug.Log("[GameModeManager] Phase 1: Hazard Identification");
            StartPhase1();
        }
        else
        {
            // Phase 2 — Fire Simulation
            Debug.Log("[GameModeManager] Phase 2: Fire Simulation");
            StartPhase2();

            // Reset flag for next time
            PlayerPrefs.SetInt("SimulationMode", 0);
            PlayerPrefs.Save();
        }
    }

    void StartPhase1()
    {
        if (phase1UI != null) phase1UI.SetActive(true);
        if (hazardObjects != null) hazardObjects.SetActive(true);
        if (phase2UI != null) phase2UI.SetActive(false);
        if (simulationSystem != null) simulationSystem.SetActive(false);
    }

    void StartPhase2()
    {
        if (phase1UI != null) phase1UI.SetActive(false);
        if (hazardObjects != null) hazardObjects.SetActive(false);
        if (phase2UI != null) phase2UI.SetActive(true);
        if (simulationSystem != null) simulationSystem.SetActive(true);
    }
}