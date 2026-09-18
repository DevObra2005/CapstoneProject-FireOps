using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Keeps a UI panel visible while the Android/iOS on-screen keyboard is open.
///
/// Unity does not reflow the Canvas when the native keyboard appears, so this
/// reads the keyboard's pixel height and does two things to the form:
///   1. slides it up into the strip of screen that is still visible
///   2. shrinks it, because in landscape that strip is only about a third tall
///
/// Closing is debounced. Moving focus from one field to another leaves a gap of
/// a frame or two where nothing is focused, and acting on that gap would make
/// the form drop and rise again on every tap. Opening stays instant.
///
/// Objects listed in hideWhileTyping are switched off BEFORE the form is
/// measured, so anything hidden is correctly left out of the height. That
/// means a child of formRoot (the Sign in button, say) can be hidden too.
///
/// Height comes from Android itself, so any keyboard app works: Gboard,
/// SwiftKey, Samsung Keyboard, a number row switched on, the emoji panel.
/// The keyboardFraction value is only a fallback for the Editor and for the
/// rare frame where Android reports nothing.
///
/// Fully Inspector-driven: no scene-specific names are hardcoded, so the same
/// component works on Login, Register, or any other scene with input fields.
/// </summary>
[DisallowMultipleComponent]
public class MobileKeyboardAdapter : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Canvas the login UI lives on. Left empty, it is found in the parents.")]
    public Canvas canvas;

    [Tooltip("The parent holding the input fields and the Sign in button. " +
             "Its anchor must be Middle / Center. Its own Width and Height are ignored.")]
    public RectTransform formRoot;

    [Header("Hidden while typing")]
    [Tooltip("Objects with no room once the keyboard is up. Children of formRoot " +
             "are allowed here: they are switched off before the form is measured, " +
             "so the height stays correct.")]
    public GameObject[] hideWhileTyping = new GameObject[0];

    [Header("Compact mode")]
    [Tooltip("How much the form shrinks while typing. 0.8 = 80% of normal size. " +
             "With fewer objects on screen you can usually raise this.")]
    [Range(0.5f, 1f)]
    public float compactScale = 0.8f;

    [Tooltip("Breathing room kept between the form and the top of the screen.")]
    public float topPadding = 24f;

    [Tooltip("How quickly the form slides. Higher is snappier.")]
    public float moveSpeed = 12f;

    [Header("Close debounce")]
    [Tooltip("Seconds the keyboard must stay closed before the form drops back. " +
             "Bridges the gap when focus moves from one field to the next, so " +
             "tapping between fields does not replay the animation. " +
             "Raise it if you still see a flicker on a slow device.")]
    [Range(0f, 1f)]
    public float closeDelay = 0.25f;

    [Header("Keyboard height")]
    [Tooltip("Fallback only. Used in the Editor, and on device if Android reports " +
             "a nonsense height. On a real phone the true height is read from the OS, " +
             "so this does not need to match any particular keyboard app.")]
    [Range(0.3f, 0.8f)]
    public float keyboardFraction = 0.62f;

    [Header("Editor testing")]
    [Tooltip("Forces the keyboard state on, without clicking a field.")]
    public bool simulateKeyboard = false;

#if UNITY_EDITOR
    [Tooltip("Draws a grey block where the keyboard would sit, so you can see " +
             "whether the form clears it. Editor only, never built.")]
    public bool drawPreviewBlock = true;
#endif

    [Header("Read-outs")]
    [Tooltip("Filled in automatically. Measured height of the visible form, in Canvas units.")]
    [SerializeField] private float measuredFormHeight;

    [Tooltip("Filled in automatically. Height of the whole Canvas, in Canvas units.")]
    [SerializeField] private float measuredCanvasHeight;

    [Tooltip("Filled in automatically. Where the script is trying to put the form.")]
    [SerializeField] private float currentTargetY;

    [Tooltip("Filled in automatically. Which signal is reporting a keyboard right now.")]
    [SerializeField] private string detectedBy = "closed";

    [Tooltip("Filled in automatically. True while a close is waiting out the debounce.")]
    [SerializeField] private bool closePending;

    private RectTransform canvasRect;
    private Vector2 restPosition;
    private float restScale;
    private float targetY;
    private float targetScale;
    private bool stableOpen;
    private float closeRequestedAt = -1f;
    private Bounds contentBounds;
    private readonly Vector3[] corners = new Vector3[4];

    private void Awake()
    {
        if (canvas == null)
        {
            canvas = GetComponentInParent<Canvas>();
        }

        if (canvas != null)
        {
            canvasRect = canvas.transform as RectTransform;
        }

        if (formRoot != null)
        {
            restPosition = formRoot.anchoredPosition;
            restScale = formRoot.localScale.x;
            targetY = restPosition.y;
            targetScale = restScale;
        }
    }

    private void Start()
    {
        MeasureContent();
    }

    /// <summary>
    /// Measures the ACTIVE direct children of formRoot. Grandchildren are
    /// deliberately ignored, because art and text objects inside an input field
    /// are often larger than the field itself. Anything switched off is skipped,
    /// which is why hiding has to happen before this runs.
    /// Result is in formRoot's own local space, so its scale does not matter.
    /// </summary>
    public void MeasureContent()
    {
        if (formRoot == null)
        {
            return;
        }

        bool found = false;
        Bounds measured = new Bounds();

        for (int i = 0; i < formRoot.childCount; i++)
        {
            RectTransform child = formRoot.GetChild(i) as RectTransform;

            if (child == null || !child.gameObject.activeInHierarchy)
            {
                continue;
            }

            child.GetWorldCorners(corners);

            for (int c = 0; c < 4; c++)
            {
                Vector3 local = formRoot.InverseTransformPoint(corners[c]);

                if (!found)
                {
                    measured = new Bounds(local, Vector3.zero);
                    found = true;
                }
                else
                {
                    measured.Encapsulate(local);
                }
            }
        }

        contentBounds = measured;
        measuredFormHeight = found ? measured.size.y : 0f;
    }

    private void Update()
    {
        if (formRoot == null || canvasRect == null)
        {
            return;
        }

        bool wasOpen = stableOpen;
        UpdateStableState();

        if (stableOpen != wasOpen)
        {
            // Hide or show FIRST. The measurement below only counts what is
            // actually on screen, so this order matters.
            for (int i = 0; i < hideWhileTyping.Length; i++)
            {
                if (hideWhileTyping[i] != null)
                {
                    hideWhileTyping[i].SetActive(!stableOpen);
                }
            }

            MeasureContent();
        }

        float canvasHeight = canvasRect.rect.height;
        measuredCanvasHeight = canvasHeight;

        if (stableOpen)
        {
            float keyboardHeight = KeyboardHeightInCanvasUnits();

            // Where the content sits relative to formRoot's own origin, and how
            // tall it is. Both shrink along with the form.
            float centreOffset = contentBounds.center.y * compactScale;
            float halfHeight = contentBounds.extents.y * compactScale;

            // Middle of the strip of screen the keyboard is NOT covering.
            float stripCentreY = keyboardHeight * 0.5f;

            // Put the content's own centre in the middle of that strip.
            float wantedY = stripCentreY - centreOffset;

            // But never push the top of the content off the top of the screen.
            float highestY = (canvasHeight * 0.5f) - topPadding - halfHeight - centreOffset;

            // If the measurement came back nonsense, trust the simple answer
            // rather than a clamp built on a bad number.
            bool measurementLooksSane = measuredFormHeight > 1f
                                        && measuredFormHeight < canvasHeight;

            targetY = measurementLooksSane ? Mathf.Min(wantedY, highestY) : wantedY;
            targetScale = compactScale;
        }
        else
        {
            targetY = restPosition.y;
            targetScale = restScale;
        }

        currentTargetY = targetY;

        // unscaledDeltaTime so this still animates if the game is paused.
        float step = Time.unscaledDeltaTime * moveSpeed;

        Vector2 position = formRoot.anchoredPosition;
        position.y = Mathf.Lerp(position.y, targetY, step);
        formRoot.anchoredPosition = position;

        float scale = Mathf.Lerp(formRoot.localScale.x, targetScale, step);
        formRoot.localScale = new Vector3(scale, scale, 1f);
    }

    /// <summary>
    /// Turns the raw, twitchy signal into a stable one. Opening is immediate.
    /// Closing has to hold for closeDelay seconds, which bridges the frame or
    /// two where focus has left one field and not yet reached the next.
    /// </summary>
    private void UpdateStableState()
    {
        bool rawOpen = IsKeyboardOpen();

        if (rawOpen)
        {
            closeRequestedAt = -1f;
            closePending = false;
            stableOpen = true;
            return;
        }

        if (!stableOpen)
        {
            closePending = false;
            return;
        }

        if (closeRequestedAt < 0f)
        {
            closeRequestedAt = Time.unscaledTime;
        }

        closePending = true;

        if (Time.unscaledTime - closeRequestedAt >= closeDelay)
        {
            stableOpen = false;
            closePending = false;
            closeRequestedAt = -1f;
        }
    }

    /// <summary>
    /// Two independent signals, either of which means "a keyboard is up".
    /// Focus is Unity's own state and works in the Editor too. The OS flag is
    /// the backup for the case where focus sits somewhere unexpected.
    /// </summary>
    private bool IsKeyboardOpen()
    {
        if (simulateKeyboard)
        {
            detectedBy = "simulated";
            return true;
        }

        if (IsTextFieldFocused())
        {
            detectedBy = "field focus";
            return true;
        }

        if (TouchScreenKeyboard.visible)
        {
            detectedBy = "OS flag";
            return true;
        }

        detectedBy = "closed";
        return false;
    }

    /// <summary>Is a text input currently selected? Unity's answer, not the OS's.</summary>
    private bool IsTextFieldFocused()
    {
        EventSystem events = EventSystem.current;

        if (events == null)
        {
            return false;
        }

        GameObject selected = events.currentSelectedGameObject;

        if (selected == null)
        {
            return false;
        }

        TMP_InputField modernField = selected.GetComponent<TMP_InputField>();

        if (modernField != null && modernField.isFocused)
        {
            return true;
        }

        InputField legacyField = selected.GetComponent<InputField>();

        if (legacyField != null && legacyField.isFocused)
        {
            return true;
        }

        return false;
    }

    /// <summary>Keyboard height in raw device pixels, as reported by the OS.</summary>
    private float KeyboardHeightInPixels()
    {
        float pixels = TouchScreenKeyboard.area.height;

        // Zero in the Editor, and some Android builds report 0 or the full
        // screen height for a frame or two. Anything outside a believable
        // range falls back to the estimate.
        float lowestBelievable = Screen.height * 0.10f;
        float highestBelievable = Screen.height * 0.85f;

        if (pixels < lowestBelievable || pixels > highestBelievable)
        {
            pixels = Screen.height * keyboardFraction;
        }

        return pixels;
    }

    private float KeyboardHeightInCanvasUnits()
    {
        // Screen pixels are not Canvas units. Canvas Scaler stretches the
        // reference resolution onto the real screen, and scaleFactor is that ratio.
        float scaleFactor = (canvas != null && canvas.scaleFactor > 0f)
            ? canvas.scaleFactor
            : 1f;

        return KeyboardHeightInPixels() / scaleFactor;
    }

#if UNITY_EDITOR
    private GUIStyle previewLabel;

    private void OnGUI()
    {
        if (!drawPreviewBlock || !stableOpen)
        {
            return;
        }

        float height = KeyboardHeightInPixels();
        Rect block = new Rect(0f, Screen.height - height, Screen.width, height);

        Color previous = GUI.color;

        GUI.color = new Color(0.17f, 0.19f, 0.21f, 0.97f);
        GUI.DrawTexture(block, Texture2D.whiteTexture);

        if (previewLabel == null)
        {
            previewLabel = new GUIStyle(GUI.skin.label);
            previewLabel.alignment = TextAnchor.MiddleCenter;
        }

        previewLabel.fontSize = Mathf.RoundToInt(Screen.height * 0.035f);

        GUI.color = new Color(1f, 1f, 1f, 0.55f);
        GUI.Label(block,
                  "preview keyboard, " + Mathf.RoundToInt(keyboardFraction * 100f) +
                  "% of screen  (" + detectedBy + ")",
                  previewLabel);

        GUI.color = previous;
    }
#endif
}