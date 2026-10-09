using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the "● ● ○ ○  Page 2 of 7" indicator on the How to Play panel.
/// This script only DISPLAYS. Another script (the page navigator) tells it
/// which page is showing by calling SetStep(). Think of it like a React
/// component that receives props: it doesn't own the page state.
/// </summary>
public class StepIndicator : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The 'Dots' object that has the Horizontal Layout Group.")]
    [SerializeField] private RectTransform dotsContainer;

    [Tooltip("The inactive 'DotTemplate' image inside Dots. It is copied once per page.")]
    [SerializeField] private Image dotTemplate;

    [Tooltip("The 'CountText' that shows 'Page X of Y'.")]
    [SerializeField] private TMP_Text countText;

    [Header("Dot colors")]
    [SerializeField] private Color currentColor = new Color32(0xF4, 0xA3, 0x00, 0xFF); // bright amber
    [SerializeField] private Color seenColor = new Color32(0xF4, 0xA3, 0x00, 0x8C); // dim amber
    [SerializeField] private Color unseenColor = new Color32(0x3A, 0x45, 0x52, 0xFF); // gray

    [Header("Dot sizes")]
    [SerializeField] private float dotSize = 14f;
    [SerializeField] private float currentDotSize = 22f;

    [Header("Editor test (turn off once the navigator is built)")]
    [Tooltip("Shows a fake indicator on Start so you can test it before Next/Back exist.")]
    [SerializeField] private bool showTestOnStart = true;
    [SerializeField] private int testTotalPages = 7;

    // The dots this script created (copies of DotTemplate).
    private readonly List<Image> dots = new List<Image>();

    // Only used by the test buttons in the component's ⋮ menu.
    private int testCurrent;
    private int testReached;

    private void Start()
    {
        if (showTestOnStart)
        {
            testCurrent = 0;
            testReached = 0;
            SetStep(testCurrent, testReached, testTotalPages);
        }
    }

    /// <summary>
    /// Call this whenever the page changes.
    /// currentIndex   = the page showing now (0 = first page)
    /// highestReached = the furthest page the participant has reached
    /// totalPages     = how many pages there are in total
    /// </summary>
    public void SetStep(int currentIndex, int highestReached, int totalPages)
    {
        if (!HasReferences()) return;
        if (totalPages <= 0) return;

        // Keep the numbers inside a safe range.
        currentIndex = Mathf.Clamp(currentIndex, 0, totalPages - 1);
        highestReached = Mathf.Clamp(highestReached, currentIndex, totalPages - 1);

        BuildDots(totalPages);

        // Paint every dot based on where it is compared to the current page.
        for (int i = 0; i < dots.Count; i++)
        {
            bool isCurrent = i == currentIndex;
            bool isSeen = i <= highestReached;

            dots[i].color = isCurrent ? currentColor : (isSeen ? seenColor : unseenColor);

            float size = isCurrent ? currentDotSize : dotSize;
            dots[i].rectTransform.sizeDelta = new Vector2(size, size);
        }

        countText.text = $"Page {currentIndex + 1} of {totalPages}";
    }

    /// <summary>
    /// Makes sure there is exactly one dot per page.
    /// Like .map() in React: one template, copied once per item.
    /// </summary>
    private void BuildDots(int totalPages)
    {
        if (dots.Count == totalPages) return; // already correct, nothing to rebuild

        foreach (Image oldDot in dots)
        {
            oldDot.transform.SetParent(null); // remove from layout right away
            Destroy(oldDot.gameObject);
        }
        dots.Clear();

        for (int i = 0; i < totalPages; i++)
        {
            Image dot = Instantiate(dotTemplate, dotsContainer);
            dot.name = $"Dot_{i + 1}";
            dot.gameObject.SetActive(true); // the template is inactive, the copies must be active
            dots.Add(dot);
        }
    }

    private bool HasReferences()
    {
        if (dotsContainer == null || dotTemplate == null || countText == null)
        {
            Debug.LogError("[StepIndicator] Missing reference. Assign Dots Container, Dot Template, and Count Text in the Inspector.", this);
            return false;
        }
        return true;
    }

    // ---------- Test buttons (Play mode only) ----------
    // In Play mode, click the ⋮ on this component in the Inspector to use these.

    [ContextMenu("Test: Next Page")]
    private void TestNext()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[StepIndicator] Enter Play mode first."); return; }
        testCurrent = Mathf.Min(testCurrent + 1, testTotalPages - 1);
        testReached = Mathf.Max(testReached, testCurrent);
        SetStep(testCurrent, testReached, testTotalPages);
    }

    [ContextMenu("Test: Previous Page")]
    private void TestPrevious()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[StepIndicator] Enter Play mode first."); return; }
        testCurrent = Mathf.Max(testCurrent - 1, 0);
        SetStep(testCurrent, testReached, testTotalPages);
    }
}