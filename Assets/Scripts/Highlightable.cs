using UnityEngine;

/// <summary>
/// Drop this on any object (hazard or action target) that should visually
/// highlight when the player is meant to look at / interact with it.
/// Swaps every material on every renderer for a single highlight material,
/// then restores the originals when turned off.
/// </summary>
public class Highlightable : MonoBehaviour
{
    public Material highlightMaterial;

    private Renderer[] renderers;
    private Material[][] originalMaterials;
    private bool cached;

    private void CacheIfNeeded()
    {
        if (cached) return;
        renderers = GetComponentsInChildren<Renderer>();
        originalMaterials = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
            originalMaterials[i] = renderers[i].materials;
        cached = true;
    }

    public void SetHighlight(bool state)
    {
        CacheIfNeeded();

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;

            if (state && highlightMaterial != null)
            {
                var mats = new Material[renderers[i].materials.Length];
                for (int j = 0; j < mats.Length; j++) mats[j] = highlightMaterial;
                renderers[i].materials = mats;
            }
            else
            {
                renderers[i].materials = originalMaterials[i];
            }
        }
    }
}
