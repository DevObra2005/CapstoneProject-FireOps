using UnityEngine;
using UnityEngine.Video;

[System.Serializable]
public class DialogueLine
{
    [TextArea(2, 4)]
    public string text;          // What the officer says
    public Sprite characterPose; // Which BFP sprite to show

    [Tooltip("Optional — a looping demo clip shown beside the text")]
    public VideoClip demoVideo;  // Leave empty for text-only lines
}