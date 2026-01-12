using UnityEngine;

public class ButtonTester : MonoBehaviour
{
    void Update()
    {
        // Check for joystick button presses
        for (int i = 0; i < 20; i++)
        {
            KeyCode code = KeyCode.JoystickButton0 + i;
            if (Input.GetKeyDown(code))
            {
                Debug.Log("LHUCT AL KAPTOR: " + code.ToString());
            }
        }
    }
}