using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Tracks how many hazards the player has found and updates
/// the "HAZARDS IDENTIFIED: X / Y" counter in the top-left box.
/// </summary>
public class HazardManager : MonoBehaviour
{
    public static HazardManager Instance { get; private set; }

    [Header("UI")]
    public TMP_Text counterText; // shows "0 / 2"

    [Header("State")]
    public int totalHazards = 2;
    private int foundHazards = 0;
    private bool hazardsClickable = false;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        UpdateCounterText();
    }

    public void SetHazardsClickable(bool value)
    {
        hazardsClickable = value;
    }

    public bool AreHazardsClickable()
    {
        return hazardsClickable;
    }

    public void RegisterHazardFound()
    {
        foundHazards++;
        UpdateCounterText();

        if (foundHazards >= totalHazards)
        {
            OnAllHazardsFound();
        }
    }

    private void UpdateCounterText()
    {
        if (counterText != null)
            counterText.text = $"{foundHazards} / {totalHazards}";
    }

    private void OnAllHazardsFound()
    {
        // Hook your "level complete" popup or next scene transition here.
        Debug.Log("All hazards identified!");
    }
}

/// <summary>
/// Attach to each clickable hazard object in the scene
/// (needs a Collider2D/Collider or be on a UI element with a Graphic + Raycast Target).
/// </summary>
public class HazardHotspot : MonoBehaviour, IPointerClickHandler
{
    public bool alreadyFound = false;
    public GameObject foundMarker; // optional: checkmark/highlight shown once clicked

    public void OnPointerClick(PointerEventData eventData)
    {
        TryIdentify();
    }

    // Use this instead if the hazard is a 3D/2D scene object rather than a UI element
    private void OnMouseDown()
    {
        TryIdentify();
    }

    private void TryIdentify()
    {
        if (alreadyFound) return;
        if (HazardManager.Instance == null || !HazardManager.Instance.AreHazardsClickable()) return;

        alreadyFound = true;
        if (foundMarker != null) foundMarker.SetActive(true);

        HazardManager.Instance.RegisterHazardFound();
    }
}