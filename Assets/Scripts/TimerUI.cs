using UnityEngine;
using TMPro;

public class TimerUI : MonoBehaviour
{
    // -------------------------------------------------------
    // WHAT THIS DOES:
    // Reads TimeRemaining from SimulationManager every frame
    // and updates the timer display.
    // No step instructions — player figures out what to do
    // by exploring the environment.
    //
    // Displays a raw SECONDS countdown (90, 89, 88 ... 1, 0)
    // instead of MM:SS, so a 90-second timer reads as "90 sec"
    // rather than "01:30".
    // -------------------------------------------------------

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI timerText;

    [Header("Display")]
    [Tooltip("Text after the number. Examples: \" sec\", \"s\", \" seconds\", " +
             "or leave empty for just the number.")]
    [SerializeField] private string suffix = " sec";

    [Tooltip("Seconds remaining at/under which the timer turns red for urgency.")]
    [SerializeField] private float urgentThreshold = 30f;

    private void Update()
    {
        if (SimulationManager.Instance == null) return;
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        float t = SimulationManager.Instance.TimeRemaining;

        // Round UP so the last second still shows "1" before hitting "0",
        // and the clock never displays a value below 0.
        int secondsLeft = Mathf.Max(0, Mathf.CeilToInt(t));

        timerText.text = secondsLeft + suffix;

        // Turn red when under the urgent threshold — visual urgency.
        if (t <= urgentThreshold)
            timerText.color = Color.red;
        else
            timerText.color = Color.white;
    }
}