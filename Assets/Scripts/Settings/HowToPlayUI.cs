using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Controls the How to Play panel in the Main Menu.
/// Shows one content pane at a time based on which tab button was pressed,
/// and hides the main menu behind it while open.
/// Everything is assigned through the Inspector so this script can be reused
/// in other scenes without any code changes.
/// </summary>
public class HowToPlayUI : MonoBehaviour
{
    [Header("Panel Root")]
    [Tooltip("The object switched on and off when the panel opens/closes. Assign HowToPlayRoot.")]
    [SerializeField] private GameObject panelRoot;

    [Tooltip("The main menu behind this panel. Hidden while How to Play is open.")]
    [SerializeField] private GameObject mainMenuPanel;

    [Header("Tabs — order must match the panes below")]
    [SerializeField] private Button[] tabButtons;
    [SerializeField] private GameObject[] tabPanes;

    [Header("Open / Close Buttons")]
    [Tooltip("The 'How to Play' button in the main menu.")]
    [SerializeField] private Button openButton;
    [Tooltip("The back arrow inside the panel.")]
    [SerializeField] private Button backButton;

    [Header("Tab Colours")]
    [SerializeField] private Color activeTabColor = new Color(0.66f, 0.17f, 0.13f, 1f);
    [SerializeField] private Color inactiveTabColor = new Color(0.04f, 0.05f, 0.06f, 0.72f);
    [SerializeField] private Color activeTextColor = Color.white;
    [SerializeField] private Color inactiveTextColor = new Color(0.94f, 0.87f, 0.77f, 1f);

    [Header("Options")]
    [Tooltip("Scroll each pane back to the top when its tab is opened.")]
    [SerializeField] private bool resetScrollOnTabChange = true;

    private void Awake()
    {
        // Give every tab button its own index, then tell it which pane to open.
        for (int i = 0; i < tabButtons.Length; i++)
        {
            if (tabButtons[i] == null) continue;

            int index = i; // local copy, so each button keeps its own value
            tabButtons[i].onClick.AddListener(() => ShowTab(index));
        }

        if (openButton != null) openButton.onClick.AddListener(Open);
        if (backButton != null) backButton.onClick.AddListener(Close);

        // Start hidden.
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    /// <summary>Opens the panel on the first tab and hides the main menu.</summary>
    public void Open()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (panelRoot != null) panelRoot.SetActive(true);
        ShowTab(0);
    }

    /// <summary>Closes the panel and brings the main menu back.</summary>
    public void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
    }

    /// <summary>Shows one pane and highlights its tab. Everything else is hidden.</summary>
    public void ShowTab(int index)
    {
        if (tabPanes == null || index < 0 || index >= tabPanes.Length) return;

        // Show the chosen pane, hide the others.
        for (int i = 0; i < tabPanes.Length; i++)
        {
            if (tabPanes[i] != null) tabPanes[i].SetActive(i == index);
        }

        // Recolour the tab buttons so the active one stands out.
        for (int i = 0; i < tabButtons.Length; i++)
        {
            if (tabButtons[i] == null) continue;

            bool isActive = (i == index);

            Image background = tabButtons[i].GetComponent<Image>();
            if (background != null)
                background.color = isActive ? activeTabColor : inactiveTabColor;

            TMP_Text label = tabButtons[i].GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.color = isActive ? activeTextColor : inactiveTextColor;
        }

        if (resetScrollOnTabChange) ScrollToTop(index);
    }

    /// <summary>Returns a newly opened pane to the top of its scroll area.</summary>
    private void ScrollToTop(int index)
    {
        if (tabPanes[index] == null) return;

        ScrollRect scroll = tabPanes[index].GetComponentInChildren<ScrollRect>(true);
        if (scroll == null) return;

        // The layout has to finish rebuilding before the scroll position will stick.
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 1f;
    }
}