// -------------------------------------------------------
// ActionFeedbackManager.cs
// WHAT THIS DOES:
// Runs the "Feedback B" running action log (top-left, below the
// ring timer). Newest entry on top, max 3 visible, each fades out
// after a few seconds. Handles correct actions, wrong actions,
// and positioning hints.
//
// The TITLE text carries the meaning ("CORRECT" / "WRONG ACTION" /
// "REPOSITION") plus a colour, so there is no icon.
//
// WHY THERE ARE THREE KINDS, NOT TWO:
// Green and red teach the player a contract - green means right,
// red means you lost time. A positioning block breaks that: the
// ACTION was correct, only the player's position was wrong, and
// no time was taken. Showing that in red would tell them they were
// penalised when they were not, and they would second-guess a
// decision that was actually right.
//
// Amber says the true thing instead: right idea, wrong conditions,
// try again. That is also how real fire training works - you are
// not marked down for standing badly, you are corrected.
//
// Other scripts call:
//    ActionFeedbackManager.Instance.ShowCorrect("Sound the Alarm");
//    ActionFeedbackManager.Instance.ShowWrong("Aim at the base, not the flames", 20);
//    ActionFeedbackManager.Instance.ShowHint("Move closer to the fire");
// -------------------------------------------------------
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class ActionFeedbackManager : MonoBehaviour
{
    // --- Singleton: one shared instance any script can reach ---
    public static ActionFeedbackManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private GameObject entryPrefab;   // one log row (a prefab)
    [SerializeField] private Transform entryContainer; // the object rows spawn inside

    [Header("Settings")]
    [SerializeField] private int maxVisible = 3;        // how many rows at once
    [SerializeField] private float entryLifetime = 8f;  // seconds before it fades
    [SerializeField] private float fadeDuration = 0.5f; // how long the fade takes

    [Header("Colors")]
    [SerializeField] private Color correctColor = new Color(0.18f, 0.80f, 0.44f); // green
    [SerializeField] private Color wrongColor = new Color(0.91f, 0.30f, 0.24f); // red

    [Tooltip("Used for positioning hints - the action was right, only the " +
             "player's position was wrong, and NO time was lost. Amber " +
             "rather than red so the player does not think they were " +
             "penalised.")]
    [SerializeField] private Color hintColor = new Color(0.95f, 0.68f, 0.18f); // amber

    [Header("Row Titles")]
    [Tooltip("Title shown on positioning hint rows. Keep it short - the " +
             "row trims long text.")]
    [SerializeField] private string hintTitle = "REPOSITION";

    // Keeps track of the rows currently on screen, so we can trim old ones.
    private readonly List<GameObject> activeEntries = new List<GameObject>();

    private void Awake()
    {
        // Standard singleton guard.
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ---------- PUBLIC: call these from other scripts ----------

    public void ShowCorrect(string message)
    {
        AddEntry("CORRECT", message, "", correctColor);
    }

    public void ShowWrong(string tip, int penaltySeconds)
    {
        string penaltyText = penaltySeconds > 0 ? "-" + penaltySeconds + "s" : "";
        AddEntry("WRONG ACTION", tip, penaltyText, wrongColor);
    }

    /// <summary>
    /// A correction that costs nothing - the player picked the right action
    /// but is standing somewhere it does not make sense. No penalty text,
    /// because no time was taken.
    /// </summary>
    public void ShowHint(string hint)
    {
        AddEntry(hintTitle, hint, "", hintColor);
    }

    // ---------- INTERNAL: build and manage a row ----------

    private void AddEntry(string title, string message, string penalty, Color accent)
    {
        if (entryPrefab == null || entryContainer == null) return;

        // 1. Spawn a new row from the prefab, inside the container.
        GameObject row = Instantiate(entryPrefab, entryContainer);

        // 2. Newest on top: move it to the first slot in the layout.
        row.transform.SetSiblingIndex(0);

        // 3. Fill in the child texts (named EXACTLY as below in the prefab).
        SetChildText(row, "Title", title, accent);
        SetChildText(row, "Message", message, Color.white);
        SetChildText(row, "Penalty", penalty, accent);

        // 4. Optional colored accent strip on the left.
        Transform accentStrip = row.transform.Find("Accent");
        if (accentStrip != null)
        {
            var img = accentStrip.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.color = accent;
        }

        // 5. Track it, and trim the oldest if we're over the limit.
        activeEntries.Add(row);
        while (activeEntries.Count > maxVisible)
        {
            GameObject oldest = activeEntries[0];
            activeEntries.RemoveAt(0);
            if (oldest != null) Destroy(oldest);
        }

        // 6. Start this row's own fade-out timer.
        StartCoroutine(FadeAndRemove(row));
    }

    // Waits, then fades this row's CanvasGroup to 0 and destroys it.
    private IEnumerator FadeAndRemove(GameObject row)
    {
        CanvasGroup cg = row.GetComponent<CanvasGroup>();
        if (cg == null) cg = row.AddComponent<CanvasGroup>();

        yield return new WaitForSeconds(entryLifetime);

        float t = 0f;
        while (t < fadeDuration)
        {
            if (row == null) yield break; // trimmed early? stop.
            t += Time.deltaTime;
            cg.alpha = Mathf.Lerp(1f, 0f, t / fadeDuration);
            yield return null;
        }

        activeEntries.Remove(row);
        if (row != null) Destroy(row);
    }

    // Helper: find a child by name, set its text + color.
    // Hides the object if the text is empty (so an empty penalty
    // slot doesn't leave a gap).
    private void SetChildText(GameObject root, string childName, string value, Color color)
    {
        Transform child = root.transform.Find(childName);
        if (child == null) return;

        TextMeshProUGUI tmp = child.GetComponent<TextMeshProUGUI>();
        if (tmp == null) return;

        tmp.text = value;
        tmp.color = color;
        child.gameObject.SetActive(!string.IsNullOrEmpty(value));
    }
}