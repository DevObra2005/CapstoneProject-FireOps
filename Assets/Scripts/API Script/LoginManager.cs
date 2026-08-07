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

    [Header("Error Modal")]
    public GameObject errorModal;        // drag ErrorModal here
    public TextMeshProUGUI modalMessage; // drag ModalMessage here

    private const string NEXT_SCENE = "EventSelectionScene";

    void Start()
    {
        // Hide the modal when the scene first loads
        // Same idea as display:none in CSS — hidden until needed
        HideModal();
    }

    // ── Called by LoginButton's OnClick event ────────────────────────
    // This is the entry point — Unity calls this when the user taps Login
    public void OnLoginButtonClick()
    {
        string email = emailField.text.Trim();
        string password = passwordField.text;

        // Client-side validation — check obvious errors before hitting the API
        // Same idea as form validation in your React Register.jsx
        if (string.IsNullOrEmpty(email))
        {
            ShowModal("Please enter your email address.");
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowModal("Please enter your password.");
            return;
        }

        // Everything looks okay — start the API call
        StartCoroutine(LoginCoroutine(email, password));
    }

    // ── Called by ModalOKButton's OnClick event ──────────────────────
    // When the user taps OK on the error modal, this hides it
    public void OnModalOKClick()
    {
        HideModal();
    }

    // ── The actual API call ──────────────────────────────────────────
    // IEnumerator = this is a Coroutine — it can pause mid-execution
    // while waiting for the server response without freezing the game
    // Think of it like an async function in JavaScript
    private IEnumerator LoginCoroutine(string email, string password)
    {
        // Disable the login button while waiting
        // Prevents the user from tapping Login multiple times
        SetButtonState(false);

        // Build the JSON body — same structure as your Postman test:
        // { "email": "test@test.com", "password": "password123" }
        string jsonBody = JsonUtility.ToJson(
            new LoginRequest { email = email, password = password }
        );

        // Set up the HTTP POST request
        // UnityWebRequest = Unity's version of axios.post() in React
        UnityWebRequest request = new UnityWebRequest(ApiConfig.LoginUrl, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");

        // PAUSE HERE and wait for the server to respond
        // yield return = "pause this function until this operation finishes"
        // The rest of the game keeps running normally during this wait
        yield return request.SendWebRequest();

        // ── Check for network-level failure ──
        // This fires if Laravel isn't running at all, or there's no internet
        // Different from a 401/422 error — those mean the server DID respond
        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            ShowModal("Cannot connect to server.\nMake sure Laravel is running.");
            SetButtonState(true);
            yield break; // stop the coroutine here — like a return statement
        }

#if UNITY_EDITOR
        // Only runs in the Unity Editor — stripped out in the final APK build
        // Lets you inspect the raw server response during development
        Debug.Log("Server response (" + request.responseCode + "): " + request.downloadHandler.text);
#endif

        // ── HTTP 200 = Login successful ──
        if (request.responseCode == 200)
        {
            // JsonUtility.FromJson = converts JSON string → C# object
            // Like JSON.parse() in JavaScript
            LoginResponse response = JsonUtility.FromJson<LoginResponse>(
                request.downloadHandler.text
            );

            // Save the token and participant info to PlayerPrefs
            // PlayerPrefs = Unity's version of localStorage in the browser
            // Key-value storage that persists across scenes
            // Other scenes can read these with PlayerPrefs.GetString("participant_token")
            PlayerPrefs.SetString("participant_token", response.token);
            PlayerPrefs.SetString("participant_name", response.participant.name);
            PlayerPrefs.SetString("participant_email", response.participant.email);
            PlayerPrefs.SetInt("participant_id", response.participant.id);
            PlayerPrefs.Save(); // flush to disk immediately

#if UNITY_EDITOR
            Debug.Log("Login success! Welcome, " + response.participant.name);
            Debug.Log("Token saved: " + response.token);
#endif

            // Load the main menu scene
            // SceneManager.LoadScene = like navigate() in React Router
            UnityEngine.SceneManagement.SceneManager.LoadScene(NEXT_SCENE);
        }
        else
        {
            // ── HTTP 401/422/etc = API returned an error ──
            // The server responded but rejected the credentials
            // Parse Laravel's error message from the JSON response
            ErrorResponse error = JsonUtility.FromJson<ErrorResponse>(
                request.downloadHandler.text
            );

            // Use Laravel's message if available, fallback if not
            string message = !string.IsNullOrEmpty(error.message)
                ? error.message
                : "Invalid email or password.";

            ShowModal(message);
            SetButtonState(true);
        }
    }

    // ── UI Helper Methods ────────────────────────────────────────────

    // Shows the error modal with a specific message
    // errorModal.SetActive(true) = like removing display:none in CSS
    private void ShowModal(string message)
    {
        if (errorModal != null) errorModal.SetActive(true);
        if (modalMessage != null) modalMessage.text = message;
    }

    // Hides the error modal
    // errorModal.SetActive(false) = like adding display:none in CSS
    private void HideModal()
    {
        if (errorModal != null) errorModal.SetActive(false);
    }

    // Enables or disables the login button
    // interactable = false means grayed out and unclickable
    private void SetButtonState(bool isEnabled)
    {
        if (loginButton != null)
            loginButton.interactable = isEnabled;
    }

    // ── JSON Data Classes ────────────────────────────────────────────
    // These are blueprints that mirror your Laravel API's JSON structure
    // [System.Serializable] tells Unity these classes can be
    // converted to/from JSON using JsonUtility

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