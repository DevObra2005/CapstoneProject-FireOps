using UnityEngine;

// Attached ONLY to the oil spill hazard.
// When the hazard completes (mop tapped), hides the oil to show it's cleaned.
public class OilCleanupEffect : MonoBehaviour
{
    [Tooltip("The oil object to hide when cleaned. Leave empty to hide this object.")]
    public GameObject oilObject;

    // Called by ClickableHazard.CompleteHazard() when the action is done.
    public void Clean()
    {
        if (oilObject == null)
            oilObject = gameObject;

        oilObject.SetActive(false);
    }
}