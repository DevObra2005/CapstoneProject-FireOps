using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Show / hide toggle for a password field.
///
/// Deliberately NOT a Button. A Button is a Selectable, so tapping one moves
/// EventSystem focus onto the Button and off the password field. That would
/// close the keyboard and make MobileKeyboardAdapter think typing had finished,
/// dropping the form back down mid-edit.
///
/// A plain Image with a click handler is not Selectable, so Unity's selection
/// walk passes straight through it and lands on the parent input field, which
/// stays focused. The click still reaches this script, because this script
/// handles clicks and the field does not.
///
/// Requirements: this object must be a CHILD of the input field, and its Image
/// must have Raycast Target ticked.
/// </summary>
[RequireComponent(typeof(Image))]
public class PasswordVisibilityToggle : MonoBehaviour, IPointerClickHandler
{
    [Header("References")]
    [Tooltip("The password field. Left empty, the nearest one in the parents is used.")]
    public TMP_InputField passwordField;

    [Tooltip("The Image showing the icon. Left empty, the one on this object is used.")]
    public Image icon;

    [Header("Sprites")]
    [Tooltip("Shown while the password is hidden. The open eye: tap to reveal.")]
    public Sprite maskedSprite;

    [Tooltip("Shown while the password is visible. The slashed eye: tap to hide.")]
    public Sprite revealedSprite;

    [Header("Colours")]
    [Tooltip("Icon tint while the password is hidden. Quiet, so it does not compete.")]
    public Color maskedColour = new Color32(93, 114, 134, 255);

    [Tooltip("Icon tint while the password is visible. Amber, so the state is obvious.")]
    public Color revealedColour = new Color32(244, 163, 0, 255);

    [Header("Startup")]
    [Tooltip("Leave off. Passwords should start hidden.")]
    public bool startRevealed = false;

    private bool isRevealed;

    private void Awake()
    {
        if (icon == null)
        {
            icon = GetComponent<Image>();
        }

        if (passwordField == null)
        {
            passwordField = GetComponentInParent<TMP_InputField>();
        }
    }

    private void Start()
    {
        // Start, not Awake: TMP finishes setting itself up first.
        isRevealed = startRevealed;
        Apply(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        isRevealed = !isRevealed;
        Apply(true);
    }

    /// <summary>
    /// Re-masks the password. Hook this to a failed-login response if you want
    /// the field hidden again after an error.
    /// </summary>
    public void Hide()
    {
        isRevealed = false;
        Apply(false);
    }

    private void Apply(bool keepCaret)
    {
        if (passwordField != null)
        {
            // Changing Content Type rebuilds the field, which sends the caret
            // back to the start. Remember where it was so typing can continue.
            int caret = keepCaret ? passwordField.caretPosition : 0;

            passwordField.contentType = isRevealed
                ? TMP_InputField.ContentType.Standard
                : TMP_InputField.ContentType.Password;

            // Without this, TMP keeps drawing the old masked or unmasked text.
            passwordField.ForceLabelUpdate();

            if (keepCaret)
            {
                passwordField.caretPosition = caret;

                // Both ends of the selection, or the field highlights a range.
                passwordField.selectionAnchorPosition = caret;
                passwordField.selectionFocusPosition = caret;
            }
        }

        if (icon != null)
        {
            Sprite wanted = isRevealed ? revealedSprite : maskedSprite;

            if (wanted != null)
            {
                icon.sprite = wanted;
            }

            icon.color = isRevealed ? revealedColour : maskedColour;
        }
    }
}