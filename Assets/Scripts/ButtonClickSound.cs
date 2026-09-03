using UnityEngine;
using UnityEngine.UI;

// -------------------------------------------------------
// Drop this on any Button and it plays the UI click. No Inspector wiring,
// no OnClick entry.
//
// WHY THIS EXISTS INSTEAD OF DRAGGING AudioManager INTO OnClick:
//
// An OnClick entry stores a reference to ONE specific object. AudioManager
// is DontDestroyOnLoad, so every scene after the first has its copy
// destroyed by the singleton guard — and the buttons in that scene are left
// pointing at a destroyed object. Unity logs nothing; the click is just
// silent from the second scene onward.
//
// This listens at runtime and asks the singleton who is currently alive, so
// it cannot go stale.
// -------------------------------------------------------

[RequireComponent(typeof(Button))]
public class ButtonClickSound : MonoBehaviour
{
    private void Start()
    {
        // Start, not Awake — AudioManager sets Instance in Awake, and this
        // guarantees it has already run.
        Button button = GetComponent<Button>();

        if (button != null)
            button.onClick.AddListener(AudioManager.Click);
    }
}