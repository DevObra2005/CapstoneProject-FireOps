using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// The "brain" of the How to Play panel. It owns the page state
/// (currentPage, highestReached) and tells everything else what to show:
/// the chapter stepper, the dots indicator, the pages, and the buttons.
///
/// React analogy: this is the parent component with useState.
/// ChapterStepper and StepIndicator are child components that only
/// display the "props" this script passes to them.
/// </summary>
public class HowToPlayNavigator : MonoBehaviour
{
    [Serializable]
    public class Page
    {
        [Tooltip("The page object to show (only one is visible at a time).")]
        public GameObject root;

        [Tooltip("Which chapter this page belongs to: 0 = Controls, 1 = Hazard, 2 = Response, 3 = Scoring.")]
        public int chapter;
    }

    [Header("Pages (in reading order, chapters from low to high)")]
    [SerializeField] private Page[] pages;

    [Header("Indicators")]
    [SerializeField] private ChapterStepper chapterStepper;
    [SerializeField] private StepIndicator stepIndicator;

    [Header("Buttons")]
    [SerializeField] private Button prevButton;
    [SerializeField] private Button nextButton;
    [Tooltip("The text inside the Next button. It changes to the finish text on the last page.")]
    [SerializeField] private TMP_Text nextButtonLabel;
    [SerializeField] private string nextText = "Next";
    [SerializeField] private string finishText = "Got it";

    [Header("Closing")]
    [Tooltip("The close arrow (←). 'Got it' presses it for you, so both close the panel the same way.")]
    [SerializeField] private Button closeButton;

    [Header("Behavior")]
    [Tooltip("Start from page 1 and reset progress every time the panel opens.")]
    [SerializeField] private bool resetOnOpen = true;

    [Header("Extra actions when 'Got it' is pressed (optional)")]
    [Tooltip("Optional. Anything else that should happen on 'Got it'. Closing is already handled by Close Button.")]
    [SerializeField] private UnityEvent onFinished;

    // ---------- State (the "useState" of this panel) ----------
    private int currentPage;
    private int highestReached;
    private bool started;

    private void Awake()
    {
        // Added once here, so re-opening the panel never adds duplicate listeners.
        if (prevButton != null) prevButton.onClick.AddListener(GoPrevious);
        if (nextButton != null) nextButton.onClick.AddListener(GoNextOrFinish);
    }

    private void OnEnable()
    {
        if (chapterStepper != null)
            chapterStepper.ChapterClicked += GoToChapter;

        // First opening is handled by Start(), when every child is awake.
        // Later openings come through here.
        if (started) Open();
    }

    private void Start()
    {
        started = true;
        Open();
    }

    private void OnDisable()
    {
        // Always unsubscribe what you subscribed, like removeEventListener.
        if (chapterStepper != null)
            chapterStepper.ChapterClicked -= GoToChapter;
    }

    private void Open()
    {
        if (resetOnOpen)
        {
            currentPage = 0;
            highestReached = 0;
        }
        Refresh();
    }

    // ---------- Actions ----------

    /// <summary>Next button: go forward, or finish on the last page.</summary>
    public void GoNextOrFinish()
    {
        if (!HasPages()) return;

        if (currentPage >= pages.Length - 1)
        {
            Finish();
            return;
        }

        currentPage++;
        highestReached = Mathf.Max(highestReached, currentPage);
        Refresh();
    }

    /// <summary>Back button: go back one page. Progress is kept.</summary>
    public void GoPrevious()
    {
        if (!HasPages() || currentPage == 0) return;

        currentPage--;
        Refresh();
    }

    /// <summary>Chapter clicked in the stepper: jump to its first page, if reached.</summary>
    private void GoToChapter(int chapter)
    {
        if (!HasPages()) return;

        // Only search pages the participant has already reached.
        for (int i = 0; i <= highestReached; i++)
        {
            if (pages[i].chapter == chapter)
            {
                currentPage = i;
                Refresh();
                return;
            }
        }
        // Not reached yet: do nothing.
    }

    /// <summary>"Got it": run any extra actions, then press the close arrow.</summary>
    private void Finish()
    {
        onFinished?.Invoke();

        if (closeButton != null)
            closeButton.onClick.Invoke(); // like closeButton.click() in JavaScript
        else
            Debug.LogWarning("[HowToPlayNavigator] Close Button is not assigned, so 'Got it' can't close the panel.", this);
    }

    // ---------- Render (repaint everything from the state) ----------

    private void Refresh()
    {
        if (!HasPages()) return;

        // 1. Show only the current page.
        for (int i = 0; i < pages.Length; i++)
        {
            if (pages[i].root != null)
                pages[i].root.SetActive(i == currentPage);
        }

        // 2. Update the chapter stepper. Pages are in chapter order,
        //    so the furthest page reached also gives the furthest chapter.
        int currentChapter = pages[currentPage].chapter;
        int reachedChapter = pages[highestReached].chapter;
        if (chapterStepper != null)
            chapterStepper.SetChapter(currentChapter, reachedChapter);

        // 3. Update the dots and "Page X of Y".
        if (stepIndicator != null)
            stepIndicator.SetStep(currentPage, highestReached, pages.Length);

        // 4. Buttons: no Back on the first page, "Got it" on the last.
        if (prevButton != null)
            prevButton.gameObject.SetActive(currentPage > 0);

        if (nextButtonLabel != null)
            nextButtonLabel.text = currentPage == pages.Length - 1 ? finishText : nextText;
    }

    private bool HasPages()
    {
        if (pages == null || pages.Length == 0)
        {
            Debug.LogError("[HowToPlayNavigator] No pages assigned. Add them in the Inspector.", this);
            return false;
        }
        return true;
    }
}