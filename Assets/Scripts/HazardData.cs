using UnityEngine;

[CreateAssetMenu(fileName = "NewHazard", menuName = "FireGame/Hazard Data")]
public class HazardData : ScriptableObject
{
    [Header("Hazard Info")]
    public string hazardTitle;

    [TextArea(3, 6)]
    public string hazardDescription;

    [TextArea(3, 6)]
    public string safetyAction;
}