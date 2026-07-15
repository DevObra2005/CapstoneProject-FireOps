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
    // -------------------------------------------------------

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI timerText;

    private void Update()
    {
        if (SimulationManager.Instance == null) return;
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        float t = SimulationManager.Instance.TimeRemaining;

        // Convert seconds into MM:SS format
        int minutes = Mathf.FloorToInt(t / 60f);
        int seconds = Mathf.FloorToInt(t % 60f);

        // {0:00} adds leading zero — so 5 seconds shows 00:05 not 0:5
        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);

        // Turn red when under 30 seconds — visual urgency for the player
        if (t <= 30f)
            timerText.color = Color.red;
        else
            timerText.color = Color.white;
    }
}