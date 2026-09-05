// -------------------------------------------------------
// WHAT THIS DOES:
// Holds your Laravel server's address in ONE place, so every
// script (LoginManager, ResultsSubmitter, EventSelectionManager,
// etc.) reads from here instead of each having its own copy.
//
// HOW TO UPDATE:
// Change BASE_DOMAIN below.
//   Production: https://bfpfireops.com
//   Local test: http://YOUR_IP:8000  (run "ipconfig" to find it)
// -------------------------------------------------------
public static class ApiConfig
{
    private const string BASE_DOMAIN = "https://bfpfireops.com";

    public static string BaseUrl => BASE_DOMAIN;
    private static string ApiBase => $"{BaseUrl}/api";

    public static string LoginUrl => $"{ApiBase}/participant/login";
    public static string ResultsUrl => $"{ApiBase}/participant/results";
    public static string EventsUrl => $"{ApiBase}/participant/events";

    // The PARTICIPANT route, not /api/forgot-password. That one searches
    // the users table, which holds superadmin and staff only — a
    // participant's email is never in it, so it would always 404.
    public static string ForgotPasswordUrl => $"{ApiBase}/participant/forgot-password";
}