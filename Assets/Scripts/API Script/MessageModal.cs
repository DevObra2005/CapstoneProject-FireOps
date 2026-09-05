using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MessageModal : MonoBehaviour
{
    [Header("Card Contents")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private Button okButton;
    [SerializeField] private TextMeshProUGUI okButtonText;

    [Header("Title Colours")]
    [SerializeField] private Color errorColour = new Color(0.90f, 0.23f, 0.18f);
    [SerializeField] private Color successColour = new Color(0.95f, 0.64f, 0.24f);

    private Action dismissCallback;



    public void ShowError(string title, string body,
                          string buttonLabel = "OKAY", Action onDismissed = null)
    {
        Display(title, body, errorColour, buttonLabel, onDismissed);
    }

    public void ShowSuccess(string title, string body,
                            string buttonLabel = "OKAY", Action onDismissed = null)
    {
        Display(title, body, successColour, buttonLabel, onDismissed);
    }

    private void Display(string title, string body, Color accent,
                         string buttonLabel, Action onDismissed)
    {
        if (titleText != null)
        {
            titleText.text = title;
            titleText.color = accent;
        }
        if (bodyText != null) bodyText.text = body;
        if (okButtonText != null) okButtonText.text = buttonLabel;

        dismissCallback = onDismissed;

        if (okButton != null)
        {
            okButton.onClick.RemoveAllListeners();
            okButton.onClick.AddListener(Hide);
        }

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);

        Action callback = dismissCallback;
        dismissCallback = null;
        callback?.Invoke();
    }

#if UNITY_EDITOR
    [ContextMenu("Test / Show Error")]
    private void TestError()
    {
        ShowError("Login Failed!",
                  "Invalid email or password. Check them and try again.",
                  "TRY AGAIN");
    }

    [ContextMenu("Test / Show Success")]
    private void TestSuccess()
    {
        ShowSuccess("Check Your Email",
                    "If that address is registered, a reset link is on its way.",
                    "BACK TO LOGIN");
    }
#endif
}