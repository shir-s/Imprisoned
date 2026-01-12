using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI; 

namespace JellyGame.UI
{
    [RequireComponent(typeof(Button))]
    public class UIButtonTrigger : MonoBehaviour
    {
        [Header("Controller Input")]
        [Tooltip("List of keys that will trigger this button (Support for PC/Mac/Keyboard).")]
        [SerializeField] private List<KeyCode> triggerKeys = new List<KeyCode>
        {
            KeyCode.JoystickButton0, // A (Xbox) / X (PS) on Windows
            KeyCode.JoystickButton1, // A (Xbox) on Mac
            KeyCode.Return,          // Enter key
            KeyCode.Space            // Space key
        };

        private Button _myButton;

        private void Awake()
        {
            // automatically gets the Button component on this GameObject
            _myButton = GetComponent<Button>();
        }

        private void Update()
        {
            // checks if the button is interactable
            if (_myButton == null || !_myButton.interactable) return;

            for (int i = 0; i < triggerKeys.Count; i++)
            {
                if (Input.GetKeyDown(triggerKeys[i]))
                {
                    //invokes the button's onClick event
                    _myButton.onClick.Invoke();
                    
                    // optional visual effect for button press
                    _myButton.OnSubmit(null); 
                    
                    return; 
                }
            }
        }
    }
}