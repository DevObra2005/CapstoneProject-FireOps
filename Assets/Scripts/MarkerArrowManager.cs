using UnityEngine;

public class MarkerArrowManager : MonoBehaviour
{
    public static MarkerArrowManager Instance { get; private set; }

    [Header("Setup")]
    [Tooltip("The MarkerArrow prefab. Spawned once and reused.")]
    public MarkerArrow arrowPrefab;

    [Header("Editor Test (Play Mode only)")]
    [Tooltip("Assign any object, then tick the box below during Play to test.")]
    public Transform testTarget;
    public bool showTestArrow;

    private MarkerArrow arrow;
    private bool lastTestState;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (arrowPrefab == null)
        {
            Debug.LogError("[MarkerArrowManager] No arrow prefab assigned.");
            return;
        }

        arrow = Instantiate(arrowPrefab);
        arrow.name = "MarkerArrow (runtime)";
        arrow.gameObject.SetActive(false);
    }

    /// <summary>
    /// Show the arrow above this object.
    /// Placement tuning (height, ignoring children, exact anchor point)
    /// comes from an optional MarkerArrowAnchor component on the target.
    /// Nothing to pass in here.
    /// </summary>
    public void PointAt(Transform target)
    {
        if (arrow == null || target == null) return;

        arrow.SetTarget(target);
        arrow.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (arrow == null) return;

        arrow.SetTarget(null);
        arrow.gameObject.SetActive(false);
    }

#if UNITY_EDITOR
    void Update()
    {
        if (showTestArrow == lastTestState) return;
        lastTestState = showTestArrow;

        if (showTestArrow) PointAt(testTarget);
        else Hide();
    }
#endif

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}