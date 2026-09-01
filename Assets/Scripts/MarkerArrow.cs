using UnityEngine;

[DisallowMultipleComponent]
public class MarkerArrow : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Object the arrow floats above. Set at runtime by MarkerArrowManager.")]
    public Transform target;

    [Header("Default Placement")]
    [Tooltip("Sit above the object's actual top edge instead of its pivot point")]
    public bool useRendererBounds = true;

    [Tooltip("Default gap above the object, in metres. Any target carrying a MarkerArrowAnchor can override this.")]
    public float heightOffset = 0.35f;

    [Header("Bob")]
    [Tooltip("How far it floats up and down, in metres")]
    public float bobHeight = 0.12f;

    [Tooltip("Breathing speed. 2 is calm, 4 is urgent.")]
    public float bobSpeed = 2.2f;

    [Header("Spin")]
    [Tooltip("Slow rotation so it reads as a 3D object from any angle")]
    public bool spin = true;

    [Tooltip("Degrees per second")]
    public float spinSpeed = 55f;

    [Header("Size")]
    [Tooltip("Keeps the arrow the same size on screen no matter how far away you stand")]
    public bool constantScreenSize = true;

    [Tooltip("Arrow height in metres at the reference distance")]
    public float baseScale = 0.5f;

    [Tooltip("Distance where the arrow is exactly baseScale tall")]
    public float referenceDistance = 3f;

    public float minScale = 0.2f;
    public float maxScale = 2f;

    // --- runtime state ---
    private Camera cam;
    private Renderer[] targetRenderers;
    private Transform anchorPoint;      // exact placement, if the target supplies one
    private float activeHeightOffset;   // resolved per target
    private Vector3 anchor;
    private float bobTimer;
    private float spinAngle;

    /// <summary>
    /// Point the arrow at an object. If that object carries a
    /// MarkerArrowAnchor, its settings win; otherwise the arrow's
    /// own defaults are used.
    /// </summary>
    public void SetTarget(Transform t)
    {
        target = t;
        bobTimer = 0f;
        spinAngle = 0f;
        targetRenderers = null;
        anchorPoint = null;
        activeHeightOffset = heightOffset;

        if (target == null) return;

        MarkerArrowAnchor cfg = target.GetComponent<MarkerArrowAnchor>();

        if (cfg != null)
        {
            if (cfg.overrideHeight)
                activeHeightOffset = cfg.heightOffset;

            if (cfg.anchorPoint != null)
            {
                // Exact placement wins outright — no bounds needed.
                anchorPoint = cfg.anchorPoint;
                return;
            }
        }

        if (!useRendererBounds) return;

        bool ignoreChildren = cfg != null && cfg.ignoreChildRenderers;

        if (ignoreChildren)
        {
            Renderer own = target.GetComponent<Renderer>();
            targetRenderers = own != null ? new Renderer[] { own } : null;
        }
        else
        {
            targetRenderers = target.GetComponentsInChildren<Renderer>();
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        UpdateAnchor();

        bobTimer += Time.deltaTime * bobSpeed;
        float bob = Mathf.Sin(bobTimer) * bobHeight;

        transform.position = anchor + Vector3.up * (activeHeightOffset + bob);

        if (spin)
        {
            spinAngle += Time.deltaTime * spinSpeed;
            transform.rotation = Quaternion.Euler(0f, spinAngle, 0f);
        }

        if (constantScreenSize)
        {
            float dist = Vector3.Distance(cam.transform.position, transform.position);
            float s = Mathf.Clamp(baseScale * (dist / referenceDistance), minScale, maxScale);
            transform.localScale = Vector3.one * s;
        }
        else
        {
            transform.localScale = Vector3.one * baseScale;
        }
    }

    private void UpdateAnchor()
    {
        // 1. Exact placement — an empty GameObject positioned by hand
        if (anchorPoint != null)
        {
            anchor = anchorPoint.position;
            return;
        }

        // 2. Top of the renderer bounds
        if (targetRenderers != null && targetRenderers.Length > 0)
        {
            Bounds b = new Bounds();
            bool started = false;

            foreach (var r in targetRenderers)
            {
                if (r == null) continue;
                if (!started) { b = r.bounds; started = true; }
                else b.Encapsulate(r.bounds);
            }

            if (started)
            {
                anchor = new Vector3(b.center.x, b.max.y, b.center.z);
                return;
            }
        }

        // 3. Fallback — the object's pivot
        anchor = target.position;
    }
}