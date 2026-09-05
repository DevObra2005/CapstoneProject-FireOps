using UnityEngine;

public class ClassroomExtinguisherTarget : MonoBehaviour
{
    private PhaseTransitionManagerClassroom owner;

    private Renderer[] rends;
    private Color[] baseColors;
    private bool pulsing = false;

    [Header("Highlight")]
    public Color highlightColor = new Color(1f, 0.66f, 0.17f);
    public float pulseSpeed = 2.5f;

    public void Setup(PhaseTransitionManagerClassroom manager)
    {
        owner = manager;

        rends = GetComponentsInChildren<Renderer>();
        baseColors = new Color[rends.Length];
        for (int i = 0; i < rends.Length; i++)
            baseColors[i] = rends[i].material.color;

        if (GetComponentInChildren<Collider>() == null)
            Debug.LogWarning($"[ClassroomExtinguisherTarget] '{gameObject.name}' has no Collider!");

        pulsing = true;
    }

    void Update()
    {
        if (!pulsing || rends == null) return;

        float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null)
                rends[i].material.color = Color.Lerp(baseColors[i], highlightColor, t);
        }
    }

    public void OnClicked()
    {
        if (owner == null) return;

        pulsing = false;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] != null)
                rends[i].material.color = baseColors[i];
        }

        owner.OnExtinguisherTapped();
    }

    public void OnHoverEnter() { }
    public void OnHoverExit() { }
}