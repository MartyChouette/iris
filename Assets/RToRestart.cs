using UnityEngine;
using UnityEngine.SceneManagement; // Required for SceneManager

public class RestartSceneController : MonoBehaviour
{
    void Update()
    {
        // Check if the 'R' key is pressed down
        if (Input.GetKeyDown(KeyCode.R))
        {
            // Reload the current scene
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
}