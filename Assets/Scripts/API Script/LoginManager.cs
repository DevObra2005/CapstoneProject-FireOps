using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

public class LoginManager : MonoBehaviour
{
    [Header("Input Fields")]
    public TMP_InputField emailField;
    public TMP_InputField passwordField;

    [Header("Login Button")]
    public Button loginButton;

    // -------------------------------------------------------
    // THE MODAL IS NOW SHARED.
    //
    // This used to hold three separate references — the modal GameObject,
    // its body text, and its OK button — and drive them directly. That
    // worked while the modal only ever said one thing.
    //
    // It broke once a second screen started using the same modal: this
    // script set the body but never the title, so a message left over
    // from the forgot password flow would still be sitting in the title
    // when a login error appeared underneath it.
    //
    // MessageModal now owns all four pieces — title, body, button label,
    // and colour — and every screen asks it to display something rather
    // than reaching in and setting fields. One place to change wording,
    // and no two scripts fighting over the same OK button.
    //
    // WORTH KNOWING, because it cost hours to find: the OK button once had
    // a SECOND Button component on its TEXT CHILD. The child rendered in
    // front, so it swallowed every tap before the real button saw it — and
    // because that impostor had Color Tint transitions of its own, the
    // button still highlighted on hover. Everything looked correct and
    // nothing worked. If a button ever stops responding while still
    // reacting visually, check its children for a stray Button.
    // -------------------------------------------------------
    [Header("Shared Message Modal")]
    [Tooltip("Drag ErrorModal here — the object with the MessageModal script on it.")]
    public MessageModal messageModal;

    private const string NEXT_SCENE = "EventSelectionScene";

    void Start()
    {
        if (messageModal == null)
        {
            Debug.LogWarning("[LoginManager] Message Modal is not assigned — " +
                             "login errors will be silent. Drag ErrorModal into " +
                             "the slot on this component.");
        }
    }

    // ── Called by LoginButton's OnClick event ────────────────────────
    public void OnLoginButtonClick()
    {
        string email = emailField.text.Trim();
        string password = passwordField.text;

        // Client-side validation — catch obvious errors before hitting the API.
        // Same idea as form validation in your React Register.jsx
        if (string.IsNullOrEmpty(email))
        {
            ShowError("Email Required", "Please enter your email address.");
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowError("Password Required", "Please enter your password.");
            return;
        }

        StartCoroutine(LoginCoroutine(email, password));
    }

    // ── The actual API call ──────────────────────────────────────────
    // IEnumerator = this is a Coroutine — it can pause mid-execution
    // while waiting for the server without freezing the game.
    // Think of it like an async function in JavaScript.
    private IEnumerator LoginCoroutine(string email, string password)
    {
        // Stops the user tapping Login repeatedly while the request is
        // in flight. Matters more now that /participant/login is rate
        // limited — repeated taps burn through the allowance.
        SetButtonState(false);

        string jsonBody = JsonUtility.ToJson(
            new LoginRequest { email = email, password = password }
        );

        // UnityWebRequest = Unity's version of axios.post() in React
        UnityWebRequest request = new UnityWebRequest(ApiConfig.LoginUrl, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");

        // PAUSE HERE until the server responds.
        // The rest of the game keeps running during this wait.
        yield return request.SendWebRequest();

        // ── Network-level failure ──
        // Fires when the server can't be reached at all. Different from a
        // 401 or 422 — those mean the server DID respond, and rejected us.
        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            ShowError("Connection Failed",
                      "FireOps couldn't reach the server. Check your internet " +
                      "connection and try again.");
            SetButtonState(true);
            yield break;
        }

#if UNITY_EDITOR
        Debug.Log("Server response (" + request.responseCode + "): " + request.downloadHandler.text);
#endif

        // ── HTTP 200 = Login successful ──
        if (request.responseCode == 200)
        {
            // JsonUtility.FromJson = JSON string → C# object.
            // Like JSON.parse() in JavaScript.
            LoginResponse response = JsonUtility.FromJson<LoginResponse>(
                request.downloadHandler.text
            );

            // PlayerPrefs = Unity's version of localStorage in the browser.
            // Key-value storage that persists across scenes.
            PlayerPrefs.SetString("participant_token", response.token);
            PlayerPrefs.SetString("participant_name", response.participant.name);
            PlayerPrefs.SetString("participant_email", response.participant.email);
            PlayerPrefs.SetInt("participant_id", response.participant.id);
            PlayerPrefs.Save();

#if UNITY_EDITOR
            Debug.Log("Login success! Welcome, " + response.participant.name);
#endif

            UnityEngine.SceneManagement.SceneManager.LoadScene(NEXT_SCENE);
            yield break;
        }

        // ── Server responded, but rejected the request ──
        ErrorResponse error = JsonUtility.FromJson<ErrorResponse>(
            request.downloadHandler.text
        );

        string serverMessage = (error != null && !string.IsNullOrEmpty(error.message))
            ? error.message
            : "";

        // 429 comes from the rate limiter on /participant/login. It fires
        // before the controller runs, so no password was ever checked —
        // worth saying plainly, or the player assumes their password is wrong.
        if (request.responseCode == 429)
        {
            ShowError("Too Many Attempts",
                      "Too many login attempts from this network. " +
                      "Wait a minute and try again.");
        }
        else
        {
            ShowError("Login Failed!",
                      !string.IsNullOrEmpty(serverMessage)
                          ? serverMessage
                          : "Invalid email or password.");
        }

        SetButtonState(true);
    }

    // ── UI Helper Methods ────────────────────────────────────────────

    // One call, and the modal handles showing itself, setting all four
    // pieces, and wiring its own dismiss button.
    private void ShowError(string title, string body)
    {
        if (messageModal != null) messageModal.ShowError(title, body);
    }

    // interactable = false means greyed out and unclickable
    private void SetButtonState(bool isEnabled)
    {
        if (loginButton != null)
            loginButton.interactable = isEnabled;
    }

    // ── JSON Data Classes ────────────────────────────────────────────
    // Blueprints mirroring your Laravel API's JSON structure.
    // [System.Serializable] lets JsonUtility convert to and from JSON.

    [System.Serializable]
    private class LoginRequest
    {
        public string email;
        public string password;
    }

    [System.Serializable]
    private class LoginResponse
    {
        public string token;
        public ParticipantData participant;
    }

    [System.Serializable]
    private class ParticipantData
    {
        public int id;
        public string name;
        public string email;
    }

    [System.Serializable]
    private class ErrorResponse
    {
        public string message;
    }
}