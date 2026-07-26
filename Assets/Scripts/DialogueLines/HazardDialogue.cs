using UnityEngine;

public class HazardDialogue : MonoBehaviour
{
    [Header("Hazard Info")]
    public string hazardName = "Octopus Wiring";

    [Header("Dialogue Lines")]
    public DialogueLine[] lines;

    [Header("Completion Lines")]
    [Tooltip("Shown after the player performs the correct action")]
    public DialogueLine[] completionLines;

    [Header("Action Target")]
    [Tooltip("The object the player must tap after the dialogue ends")]
    public GameObject actionTarget;

    [HideInInspector]
    public bool isComplete = false;
}