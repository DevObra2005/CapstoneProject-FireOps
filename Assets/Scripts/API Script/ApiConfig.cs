 // -------------------------------------------------------
// WHAT THIS DOES:
// Holds your Laravel server's address in ONE place, so every
// script (LoginManager, ResultsSubmitter, EventSelectionManager,
// etc.) reads from here instead of each having its own copy.
//
// HOW TO UPDATE:
// 1. Run "ipconfig" (Windows) on the PC running php artisan serve
// 2. Find "IPv4 Address" under your active WiFi adapter
// 3. Replace the IP below (keep "http://" and ":8000" as-is)
// -------------------------------------------------------

public static class ApiConfig
{
    // CHANGE THIS LINE when your network changes:
    private const string SERVER_IP = "https://bfpfireops.com/api"; // <-- your PC's IP
    private const string PORT = "8000";

    public static string BaseUrl => $"http://{SERVER_IP}:{PORT}";
    private static string ApiBase => $"{BaseUrl}/api";

    // One dedicated property per endpoint � no more manually
    // concatenating "/participant/whatever" in each script, which
    // is exactly what caused the missing "/api" bug just now.
    public static string LoginUrl => $"{ApiBase}/participant/login";
    public static string ResultsUrl => $"{ApiBase}/participant/results";
    public static string EventsUrl => $"{ApiBase}/participant/events";
}