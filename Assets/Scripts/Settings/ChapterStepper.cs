using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the chapter stepper (1 Controls - 2 Hazard - 3 Response - 4 Scoring)
/// on the How to Play panel.
///
/// Like StepIndicator, this script only DISPLAYS. The navigator tells it which
/// chapter is showing by calling SetChapter(). When a reached chapter is
/// clicked, it raises the ChapterClicked event so the navigator can jump there.
///
/// Expected structure (names matter, order = left to right):
///   Steps
///   ├─ Step_1  (optional Button component)
///   │  ├─ Node       (Image: the circle)
///   │  │  └─ NodeText (TMP: the number)
///   │  └─ Label      (TMP: the chapter name)
///   ├─ Step_2 ...
/// </summary>
public class ChapterStepper : MonoBehaviour
{
    // One step's parts, found automatically by name.
    private class StepView
    {
        public Button button;
        public Image node;
        public TMP_Text nodeText;
        public TMP_Text label;
    }

    [Header("References")]
    [Tooltip("The 'Steps' object. Its children are read in Hierarchy order.")]
    [SerializeField] private RectTransform stepsContainer;

    [Tooltip("The amber 'LineFill' inside Track. Its right edge moves with progress.")]
    [SerializeField] private RectTransform lineFill;

    [Header("Circle colors")]
    [SerializeField] private Color currentColor = new Color32(0xF4, 0xA3, 0x00, 0xFF); // bright amber
    [SerializeField] private Color doneColor = new Color32(0xB8, 0x7A, 0x00, 0xFF); // darker amber
    [SerializeField] private Color upcomingColor = new Color32(0x3A, 0x45, 0x52, 0xFF); // gray

    [Header("Number colors (inside the circle)")]
    [SerializeField] private Color currentNumberColor = new Color32(0x1A, 0x12, 0x06, 0xFF); // dark
    [SerializeField] private Color doneNumberColor = Color.white;
    [SerializeField] private Color upcomingNumberColor = new Color32(0x9A, 0xA4, 0xAE, 0xFF); // light gray

    [Header("Label colors (below the circle)")]
    [SerializeField] private Color currentLabelColor = new Color32(0xF4, 0xA3, 0x00, 0xFF);
    [SerializeField] private Color doneLabelColor = Color.white;
    [SerializeField] private Color upcomingLabelColor = new Color32(0x9A, 0xA4, 0xAE, 0xFF);

    [Header("Current step")]
    [Tooltip("How much bigger the current circle is (1 = same size).")]
    [SerializeField] private float currentScale = 1.2f;

    [Header("Done steps")]
    [Tooltip("Leave empty to keep the number. Type ✓ only if your font has that character, otherwise it shows a box.")]
    [SerializeField] private string doneText = "";

    [Header("Editor test (turn off once the navigator is built)")]
    [SerializeField] private bool showTestOnStart = true;

    /// <summary>Raised with the chapter index (0 = first) when a reached chapter is clicked.</summary>
    public event Action<int> ChapterClicked;

    /// <summary>How many chapters were found under Steps.</summary>
    public int ChapterCount => steps.Count;

    private readonly List<StepView> steps = new List<StepView>();

    // Only used by the test buttons.
    private int testCurrent;
    private int testReached;

    private void Awake()
    {
        CollectSteps();
    }

    private void Start()
    {
        if (!showTestOnStart) return;

        // In test mode, clicking a chapter just jumps the stepper there.
        ChapterClicked += index =>
        {
            Debug.Log($"[ChapterStepper] Chapter {index + 1} clicked.");
            testCurrent = index;
            SetChapter(testCurrent, testReached);
        };

        testCurrent = 0;
        testReached = 0;
        SetChapter(testCurrent, testReached);
    }

    /// <summary>
    /// Reads each child of Steps (in Hierarchy order) and finds its parts by name.
    /// Like children.map() in React.
    /// </summary>
    private void CollectSteps()
    {
        steps.Clear();

        if (stepsContainer == null)
        {
            Debug.LogError("[ChapterStepper] Steps Container is not assigned in the Inspector.", this);
            return;
        }

        for (int i = 0; i < stepsContainer.childCount; i++)
        {
            Transform step = stepsContainer.GetChild(i);
            if (!step.gameObject.activeSelf) continue; // a hidden step is skipped

            Transform nodeT = step.Find("Node");
            Transform nodeTextT = step.Find("Node/NodeText");
            Transform labelT = step.Find("Label");

            StepView view = new StepView
            {
                button = step.GetComponent<Button>(),
                node = nodeT != null ? nodeT.GetComponent<Image>() : null,
                nodeText = nodeTextT != null ? nodeTextT.GetComponent<TMP_Text>() : null,
                label = labelT != null ? labelT.GetComponent<TMP_Text>() : null
            };

            if (view.node == null || view.nodeText == null || view.label == null)
            {
                Debug.LogError($"[ChapterStepper] '{step.name}' is missing Node, Node/NodeText, or Label (check the names and structure).", step);
                continue;
            }

            int index = steps.Count; // copy for the click listener below
            if (view.button != null)
                view.button.onClick.AddListener(() => ChapterClicked?.Invoke(index));

            steps.Add(view);
        }

        if (steps.Count == 0)
            Debug.LogError("[ChapterStepper] No valid steps found under Steps Container.", this);
    }

    /// <summary>
    /// Call this whenever the chapter changes.
    /// currentChapter        = the chapter showing now (0 = Controls)
    /// highestReachedChapter = the furthest chapter the participant has reached
    /// </summary>
    public void SetChapter(int currentChapter, int highestReachedChapter)
    {
        if (steps.Count == 0) return;

        int last = steps.Count - 1;
        currentChapter = Mathf.Clamp(currentChapter, 0, last);
        highestReachedChapter = Mathf.Clamp(highestReachedChapter, currentChapter, last);

        for (int i = 0; i < steps.Count; i++)
        {
            StepView s = steps[i];
            bool isCurrent = i == currentChapter;
            bool isDone = !isCurrent && i <= highestReachedChapter;

            if (isCurrent)
            {
                s.node.color = currentColor;
                s.nodeText.color = currentNumberColor;
                s.label.color = currentLabelColor;
            }
            else if (isDone)
            {
                s.node.color = doneColor;
                s.nodeText.color = doneNumberColor;
                s.label.color = doneLabelColor;
            }
            else
            {
                s.node.color = upcomingColor;
                s.nodeText.color = upcomingNumberColor;
                s.label.color = upcomingLabelColor;
            }

            bool useDoneText = isDone && !string.IsNullOrEmpty(doneText);
            s.nodeText.text = useDoneText ? doneText : (i + 1).ToString();

            s.node.rectTransform.localScale = isCurrent ? Vector3.one * currentScale : Vector3.one;

            // Only reached chapters can be clicked. Locked ones do nothing.
            if (s.button != null)
                s.button.interactable = i <= highestReachedChapter;
        }

        // Move the fill line's right edge, like setting width: 66% in CSS.
        if (lineFill != null)
        {
            float progress = last == 0 ? 1f : (float)highestReachedChapter / last;
            Vector2 max = lineFill.anchorMax;
            max.x = progress;
            lineFill.anchorMax = max;
        }
    }

    // ---------- Test buttons (Play mode only) ----------
    // In Play mode, click the ⋮ on this component in the Inspector to use these.

    [ContextMenu("Test: Next Chapter")]
    private void TestNext()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[ChapterStepper] Enter Play mode first."); return; }
        testCurrent = Mathf.Min(testCurrent + 1, steps.Count - 1);
        testReached = Mathf.Max(testReached, testCurrent);
        SetChapter(testCurrent, testReached);
    }

    [ContextMenu("Test: Previous Chapter")]
    private void TestPrevious()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[ChapterStepper] Enter Play mode first."); return; }
        testCurrent = Mathf.Max(testCurrent - 1, 0);
        SetChapter(testCurrent, testReached);
    }
}