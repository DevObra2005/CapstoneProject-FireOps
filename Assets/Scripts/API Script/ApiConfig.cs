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
    private const string BASE_DOMAIN = "https://bfpfireops.com";
    public static string BaseUrl => BASE_DOMAIN;
    private static string ApiBase => $"{BaseUrl}/api";

    public static string LoginUrl => $"{ApiBase}/participant/login";
    public static string ResultsUrl => $"{ApiBase}/participant/results";
    public static string EventsUrl => $"{ApiBase}/participant/events";
}