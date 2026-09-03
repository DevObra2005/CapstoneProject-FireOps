using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using System;
using System.Collections;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance;

    [Header("UI References")]
    public GameObject dialoguePanel;
    public Image characterImage;
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI speakerLabel;
    public Button nextButton;
    public TextMeshProUGUI nextButtonText;
    public Transform progressContainer;
    public GameObject progressDotPrefab;

    [Header("Optional Demo Video")]
    [Tooltip("The RawImage object that displays the looping demo clip")]
    public GameObject demoVideoPanel;
    public VideoPlayer demoVideoPlayer;

    [Tooltip("DialogueText's RectTransform — shrinks when a video shows")]
    public RectTransform dialogueTextRect;

    [Tooltip("Right offset when NO video is showing")]
    public float textRightNormal = 30f;

    [Tooltip("Right offset when a video IS showing")]
    public float textRightWithVideo = 250f;

    // -------------------------------------------------------
    // AUDIO
    //
    // Every clip here is OPTIONAL. Each call is null-guarded inside
    // AudioManager.Play(), so an empty slot is a silent no-op — which is
    // what keeps a scene without an AudioManager working unchanged.
    //
    // The three sounds map onto the three things the player perceives:
    // the panel arriving, their own tap landing, and the panel leaving.
    // -------------------------------------------------------
    [Header("Audio")]
    [Tooltip("Plays once when the dialogue panel slides IN. A short whoosh " +
             "or pop. Leave empty to skip.")]
    public AudioClip popupSound;

    [Tooltip("Plays on every NEXT tap. Leave empty to skip.")]
    public AudioClip tapSound;

    [Tooltip("Plays once when the dialogue panel slides OUT.\n\n" +
             "NOTE: on the final line this fires roughly one frame after the " +
             "tap sound, since the tap is what triggers the close. If the two " +
             "sound cluttered together, use a softer close clip rather than " +
             "delaying it — a delayed close would drift out of sync with the " +
             "slide animation.")]
    public AudioClip closeSound;

    [Header("Animation")]
    [Tooltip("The object that slides — usually SpeechBubble")]
    public RectTransform slidePanel;
    public float slideDistance = 400f;
    public float slideDuration = 0.28f;
    public float typeSpeed = 0.025f;

    private DialogueLine[] currentLines;
    private int currentIndex;
    private Action onDialogueComplete;

    private Coroutine typingRoutine;
    private bool isTyping = false;
    private Vector2 slideHomePos;

    // Tracks whether the current dialogue is a completion/confirmation one,
    // so the final button reads "GOT IT" instead of "DO IT".
    private bool isCompletionDialogue = false;

    // An optional override for the final button's text.
    // When this is empty, the button falls back to the usual
    // "GOT IT" / "DO IT" behaviour. When it's set (e.g. "START"),
    // that word is used instead. Cleared automatically at the end
    // of every dialogue so it can never leak into the next one.
    private string finalButtonOverride = "";

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (slidePanel != null)
            slideHomePos = slidePanel.anchoredPosition;

        if (dialoguePanel != null) dialoguePanel.SetActive(false);
        if (demoVideoPanel != null) demoVideoPanel.SetActive(false);
        if (nextButton != null) nextButton.onClick.AddListener(OnNextPressed);
    }

    // Two-argument version — keeps all existing calls working
    public void StartDialogue(DialogueLine[] lines, Action onComplete)
    {
        StartDialogue(lines, onComplete, false, "");
    }

    // Three-argument version — pass true for completion/confirmation dialogues
    public void StartDialogue(DialogueLine[] lines, Action onComplete, bool isCompletion)
    {
        StartDialogue(lines, onComplete, isCompletion, "");
    }

    // -------------------------------------------------------
    // Four-argument version — lets the caller name the final
    // button themselves, e.g. "START" for the Phase 2 briefing.
    //
    // Pass "" (empty) for finalButtonText and it behaves exactly
    // like the three-argument version above, so nothing that
    // already calls this needs to change.
    // -------------------------------------------------------
    public void StartDialogue(DialogueLine[] lines, Action onComplete, bool isCompletion, string finalButtonText)
    {
        if (lines == null || lines.Length == 0)
        {
            onComplete?.Invoke();
            return;
        }

        currentLines = lines;
        currentIndex = 0;
        onDialogueComplete = onComplete;
        isCompletionDialogue = isCompletion;
        finalButtonOverride = finalButtonText;

        dialoguePanel.SetActive(true);
        BuildProgressDots();
        ShowLine();

        StartCoroutine(SlideIn());
    }

    // ---------- SLIDE ----------

    IEnumerator SlideIn()
    {
        // The pop fires HERE rather than in StartDialogue, so it lands on the
        // same frame the panel starts moving. Called before the yield break
        // below so a scene with no slidePanel still gets the sound.
        AudioManager.Play(popupSound);

        if (slidePanel == null) yield break;

        Vector2 start = slideHomePos + new Vector2(0f, -slideDistance);
        slidePanel.anchoredPosition = start;

        float t = 0f;
        while (t < slideDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / slideDuration);
            // EaseOutCubic — fast then settles
            p = 1f - Mathf.Pow(1f - p, 3f);
            slidePanel.anchoredPosition = Vector2.LerpUnclamped(start, slideHomePos, p);
            yield return null;
        }

        slidePanel.anchoredPosition = slideHomePos;
    }

    IEnumerator SlideOutThenClose()
    {
        // Mirrors the pop in SlideIn — fires as the panel starts moving away,
        // and sits above the null check for the same reason: a scene with no
        // slidePanel still gets the sound.
        AudioManager.Play(closeSound);

        if (slidePanel != null)
        {
            Vector2 start = slidePanel.anchoredPosition;
            Vector2 end = slideHomePos + new Vector2(0f, -slideDistance);

            float t = 0f;
            while (t < slideDuration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / slideDuration);
                // EaseInCubic — accelerates away
                p = p * p * p;
                slidePanel.anchoredPosition = Vector2.LerpUnclamped(start, end, p);
                yield return null;
            }

            slidePanel.anchoredPosition = slideHomePos;
        }

        StopDemoVideo();

        dialoguePanel.SetActive(false);

        // Clear the override so the next dialogue starts clean.
        finalButtonOverride = "";

        Action cb = onDialogueComplete;
        onDialogueComplete = null;
        cb?.Invoke();
    }

    // ---------- DEMO VIDEO ----------

    // Wipes the render texture so no stale frame from the previous clip lingers
    void ClearVideoTexture()
    {
        if (demoVideoPlayer == null || demoVideoPlayer.targetTexture == null) return;

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = demoVideoPlayer.targetTexture;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = prev;
    }

    // Shows or hides the demo clip for this line, and resizes the text
    // so the two never overlap.
    void UpdateDemoVideo(DialogueLine line)
    {
        bool hasVideo = (line.demoVideo != null && demoVideoPlayer != null);

        if (demoVideoPanel != null)
        {
            if (hasVideo)
            {
                demoVideoPanel.SetActive(true);

                // Always stop → swap → play. Don't check isPlaying first:
                // it can still report true on the same frame Stop() is called,
                // which would leave the previous clip's last frame on screen.
                demoVideoPlayer.Stop();
                ClearVideoTexture();

                demoVideoPlayer.clip = line.demoVideo;
                demoVideoPlayer.isLooping = true;
                demoVideoPlayer.Play();
            }
            else
            {
                demoVideoPlayer?.Stop();
                ClearVideoTexture();
                demoVideoPanel.SetActive(false);
            }
        }

        // Reserve space on the right only while a video is visible.
        // offsetMax.x is the RIGHT offset, stored as a negative value.
        if (dialogueTextRect != null)
        {
            Vector2 offsetMax = dialogueTextRect.offsetMax;
            offsetMax.x = hasVideo ? -textRightWithVideo : -textRightNormal;
            dialogueTextRect.offsetMax = offsetMax;
        }
    }

    void StopDemoVideo()
    {
        if (demoVideoPlayer != null)
        {
            demoVideoPlayer.Stop();
            ClearVideoTexture();
        }

        if (demoVideoPanel != null)
            demoVideoPanel.SetActive(false);
    }

    // ---------- TYPEWRITER ----------

    void ShowLine()
    {
        DialogueLine line = currentLines[currentIndex];

        if (line.characterPose != null)
            characterImage.sprite = line.characterPose;

        UpdateDemoVideo(line);

        UpdateProgressDots();

        bool isLast = (currentIndex == currentLines.Length - 1);
        if (isLast)
        {
            // If the caller gave us a custom word for this button,
            // use it. Otherwise fall back to the old behaviour.
            if (!string.IsNullOrEmpty(finalButtonOverride))
                nextButtonText.text = finalButtonOverride;
            else
                nextButtonText.text = isCompletionDialogue ? "GOT IT" : "DO IT";
        }
        else
        {
            nextButtonText.text = "NEXT";
        }

        if (typingRoutine != null) StopCoroutine(typingRoutine);
        typingRoutine = StartCoroutine(TypeLine(line.text));
    }

    IEnumerator TypeLine(string full)
    {
        isTyping = true;
        dialogueText.text = full;
        dialogueText.maxVisibleCharacters = 0;

        int total = full.Length;
        for (int i = 0; i <= total; i++)
        {
            dialogueText.maxVisibleCharacters = i;
            yield return new WaitForSecondsRealtime(typeSpeed);
        }

        isTyping = false;
        typingRoutine = null;
    }

    void SkipTyping()
    {
        if (typingRoutine != null) StopCoroutine(typingRoutine);
        dialogueText.maxVisibleCharacters = dialogueText.text.Length;
        isTyping = false;
        typingRoutine = null;
    }

    // ---------- FLOW ----------

    void OnNextPressed()
    {
        // One sound at the top, before the branch. Every tap gets feedback —
        // including the skip-typing tap, which is still a real press the
        // player made and would otherwise feel unresponsive.
        AudioManager.Play(tapSound);

        // First tap skips the typing instead of advancing
        if (isTyping)
        {
            SkipTyping();
            return;
        }

        if (currentIndex < currentLines.Length - 1)
        {
            currentIndex++;
            ShowLine();
        }
        else
        {
            EndDialogue();
        }
    }

    void EndDialogue()
    {
        StartCoroutine(SlideOutThenClose());
    }

    // ---------- PROGRESS DOTS ----------

    void BuildProgressDots()
    {
        if (progressContainer == null || progressDotPrefab == null) return;

        foreach (Transform child in progressContainer)
            Destroy(child.gameObject);

        for (int i = 0; i < currentLines.Length; i++)
            Instantiate(progressDotPrefab, progressContainer);
    }

    void UpdateProgressDots()
    {
        if (progressContainer == null) return;

        for (int i = 0; i < progressContainer.childCount; i++)
        {
            Image dot = progressContainer.GetChild(i).GetComponent<Image>();
            if (dot != null)
                dot.color = (i <= currentIndex)
                    ? new Color(0.18f, 0.64f, 0.30f)
                    : new Color(0.87f, 0.89f, 0.91f);
        }
    }

    public bool IsDialogueActive()
    {
        return dialoguePanel != null && dialoguePanel.activeSelf;
    }
}