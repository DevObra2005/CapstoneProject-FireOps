using UnityEngine;

public class KeyTestScript : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            Debug.Log("TEST: Key 1 works!");
        }
    }
}