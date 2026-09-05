using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;

// -------------------------------------------------------
// WHAT THIS DOES:
// Handles the Forgot Password screen. It collects an email address
// and asks Laravel to send a reset link to it.
//
// IT DOES NOT RESET THE PASSWORD ITSELF. The link in that email
// opens the React web app, and the participant sets their new
// password there. Unity only starts the process.
//
// That keeps the reset token in one place instead of two, and means
// a wording change is a git push rather than a new APK.
// -------------------------------------------------------
public class ForgotPasswordManager : MonoBehaviour
{
    [Header("Panels")]
    [Tooltip("Drag LoginPanel here.")]
    [SerializeField] private GameObject loginPanel;
    [Tooltip("Drag ForgotPanel here.")]
    [SerializeField] private GameObject forgotPanel;

    [Header("Login Screen")]
    [Tooltip("Drag the ForgotPassword link from LoginPanel here.")]
    [SerializeField] private Button forgotPasswordLink;

    [Header("Forgot Panel")]
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private Button sendButton;
    [Tooltip("The text label INSIDE SendButton — not the button itself.")]
    [SerializeField] private TextMeshProUGUI sendButtonText;
    [SerializeField] private Button backButton;

    [Header("Shared")]
    [Tooltip("Drag ErrorModal here — the object with MessageModal on it.")]
    [SerializeField] private MessageModal messageModal;

    // Remembered at startup so the busy state can put it back afterwards.
    private string idleLabel = "SEND RESET LINK";

    [System.Serializable] private class EmailPayload { public string email; }
    [System.Serializable] private class ApiResponse { public string message; }

    void Start()
    {
        if (sendButtonText != null) idleLabel = sendButtonText.text;

        // Listeners are attached here rather than through Inspector OnClick
        // entries. A serialized entry stores a reference to one specific
        // object, and that reference can be lost silently — by a rename, a
        // merge, or an object being recreated — while the Inspector still
        // shows the method name as if nothing is wrong.
        if (forgotPasswordLink != null) forgotPasswordLink.onClick.AddListener(OpenForgotPanel);
        if (sendButton != null) sendButton.onClick.AddListener(OnSendPressed);
        if (backButton != null) backButton.onClick.AddListener(BackToLogin);
    }

    // ── Screen switching ─────────────────────────────────────────────
    // No scene load. Both panels live in LoginScene and take turns being
    // active, so the office backdrop never reloads and nothing is lost
    // when the player changes their mind.

    public void OpenForgotPanel()
    {
        if (emailInput != null) emailInput.text = "";
        SetBusy(false);
        if (loginPanel != null) loginPanel.SetActive(false);
        if (forgotPanel != null) forgotPanel.SetActive(true);
    }

    public void BackToLogin()
    {
        if (forgotPanel != null) forgotPanel.SetActive(false);
        if (loginPanel != null) loginPanel.SetActive(true);
    }

    // ── Entry point — called when the player taps Send ───────────────
    private void OnSendPressed()
    {
        string email = emailInput != null ? emailInput.text.Trim() : "";

        // Client-side validation. Deliberately loose: it only rejects text
        // that could not possibly be an email. Strict patterns reject valid
        // addresses, so the server stays the real judge.
        if (string.IsNullOrWhiteSpace(email))
        {
            ShowError("Email Required",
                      "Enter the email address you use for FireOps.");
            return;
        }

        if (!email.Contains("@") || !email.Contains("."))
        {
            ShowError("Invalid Email",
                      "That doesn't look like an email address. Check it and try again.");
            return;
        }

        SetBusy(true);
        StartCoroutine(SendResetLink(email));
    }

    // ── The API call ─────────────────────────────────────────────────
    // Same coroutine pattern as LoginManager: pause while the server
    // answers, without freezing the game.
    private IEnumerator SendResetLink(string email)
    {
        string json = JsonUtility.ToJson(new EmailPayload { email = email });

        using (UnityWebRequest request = new UnityWebRequest(ApiConfig.ForgotPasswordUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            SetBusy(false);

            // Server unreachable — no internet, or the domain is down.
            // Different from a 404 or 422, where the server DID answer.
            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                ShowError("Connection Failed",
                          "FireOps couldn't reach the server. Check your internet " +
                          "connection and try again.");
                yield break;
            }

#if UNITY_EDITOR
            Debug.Log("Forgot password (" + request.responseCode + "): " + request.downloadHandler.text);
#endif

            string serverMessage = ReadMessage(request.downloadHandler.text);

            switch (request.responseCode)
            {
                // 200 — link sent. The success message comes from Laravel,
                // so BFP can reword it without rebuilding the APK.
                case 200:
                    messageModal.ShowSuccess(
                        "Check Your Email",
                        !string.IsNullOrEmpty(serverMessage)
                            ? serverMessage
                            : "A password reset link has been sent to your email.",
                        "BACK TO LOGIN",
                        BackToLogin);   // runs when the player dismisses the modal
                    break;

                // 404 — no account with that address.
                case 404:
                    ShowError("Email Not Found",
                              !string.IsNullOrEmpty(serverMessage)
                                  ? serverMessage
                                  : "We couldn't find an account with that email address.");
                    break;

                // 422 — Laravel's validate() rejected the format. Its errors
                // are nested under "errors", not "message", so the flat read
                // usually comes back empty. Hence the fallback.
                case 422:
                    ShowError("Invalid Email",
                              !string.IsNullOrEmpty(serverMessage)
                                  ? serverMessage
                                  : "Enter a valid email address.");
                    break;

                // 429 — the rate limiter on /forgot-password. Fires before
                // the controller runs, so no email was sent.
                case 429:
                    ShowError("Too Many Attempts",
                              "You've requested several reset links. " +
                              "Wait a minute before trying again.");
                    break;

                default:
                    ShowError("Something Went Wrong",
                              "The server couldn't process that request. " +
                              "Try again in a moment.");
                    break;
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    // Pulls the "message" field out of Laravel's JSON.
    // Returns empty rather than throwing if the shape is unexpected.
    private string ReadMessage(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        try
        {
            ApiResponse parsed = JsonUtility.FromJson<ApiResponse>(json);
            return (parsed != null && !string.IsNullOrEmpty(parsed.message))
                ? parsed.message
                : "";
        }
        catch
        {
            return "";
        }
    }


    private void ShowError(string title, string body)
    {
        if (messageModal != null) messageModal.ShowError(title, body);
    }

    // Dims the button and swaps its label while the request is in flight.
    // Without this a player taps Send repeatedly waiting on the server —
    // and with a limit of 5 per minute, locks themselves out of their own reset.
    private void SetBusy(bool busy)
    {
        if (sendButton != null) sendButton.interactable = !busy;
        if (sendButtonText != null) sendButtonText.text = busy ? "SENDING..." : idleLabel;
    }
}