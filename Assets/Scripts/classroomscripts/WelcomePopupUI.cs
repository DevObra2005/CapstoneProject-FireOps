using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the "BFP Fire Safety Officer" welcome dialogue popup.
/// Attach this to the PopupPanel GameObject and wire up the
/// references in the Inspector.
/// </summary>
public class WelcomePopupUI : MonoBehaviour
{
    [Header("Officer Dialogue")]
    [TextArea(2, 4)]
    public string[] dialoguePages =
    {
        "Good day. I am an officer of the Bureau of Fire Protection, " +
        "Natividad Station. Today we will cover how to identify fire hazards before they cause harm.",
        "Fire hazards can hide in plain sight — overloaded outlets, blocked exits, " +
        "flammable materials near heat sources.",
        "Look around the office. Click or tap each hazard you find. Good luck, Officer."
    };

    [Header("UI References")]
    public TMP_Text dialogueText;
    public Button nextButton;
    public TMP_Text nextButtonLabel;      // shows "NEXT" or "START"
    public Image[] progressDots;          // one per dialoguePages entry
    public Color activeDotColor = new Color(0.25f, 0.9f, 0.4f);   // green
    public Color inactiveDotColor = new Color(1f, 1f, 1f, 0.4f);  // dim white

    [Header("Panels")]
    public GameObject welcomePanel;       // this whole popup
    public GameObject hazardChecklistBox; // top-left "0 / 2" box, stays visible during play

    [Header("Events")]
    public HazardManager hazardManager;   // optional: enables hazard clicking once popup closes

    private int currentPage = 0;

    private void OnEnable()
    {
        currentPage = 0;
        RefreshPage();

        if (hazardManager != null)
            hazardManager.SetHazardsClickable(false); // block clicks until intro finishes
    }

    public void OnNextPressed()
    {
        currentPage++;

        if (currentPage >= dialoguePages.Length)
        {
            ClosePopup();
            return;
        }

        RefreshPage();
    }

    private void RefreshPage()
    {
        dialogueText.text = dialoguePages[currentPage];

        for (int i = 0; i < progressDots.Length; i++)
        {
            progressDots[i].color = (i == currentPage) ? activeDotColor : inactiveDotColor;
        }

        bool isLastPage = currentPage == dialoguePages.Length - 1;
        if (nextButtonLabel != null)
            nextButtonLabel.text = isLastPage ? "START" : "NEXT";
    }

    private void ClosePopup()
    {
        welcomePanel.SetActive(false);

        if (hazardChecklistBox != null)
            hazardChecklistBox.SetActive(true);

        if (hazardManager != null)
            hazardManager.SetHazardsClickable(true);
    }
}
