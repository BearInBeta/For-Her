
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadingSceneController : MonoBehaviour
{
    [Header("Scene Settings")]
    [SerializeField] private string nextSceneName = "Game";

    [Header("Loading Screen")]
    [SerializeField] private SessionLoadingScreen loadingScreen;

    private IEnumerator Start()
    {
        // Display the loading screen.
        if (loadingScreen != null)
        {
            loadingScreen.Show();
        }
        else
        {
            Debug.LogWarning(
                "Loading Screen is not assigned."
            );
        }

        // Allow Unity to render the loading screen.
        yield return null;

        // Check whether the next scene exists.
        if (!Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            Debug.LogError(
                $"Scene '{nextSceneName}' is not in the build."
            );
            yield break;
        }

        // Load the game scene asynchronously.
        AsyncOperation operation =
            SceneManager.LoadSceneAsync(
                nextSceneName,
                LoadSceneMode.Single
            );

        if (operation == null)
        {
            Debug.LogError("Failed to start scene loading.");
            yield break;
        }

        // Keep displaying the loading screen
        // until the new scene takes over.
        while (!operation.isDone)
        {
            yield return null;
        }

        // No need to hide or dismiss the loading screen.
        // Unity unloads this scene automatically.
        // The Game scene has its own loading screen.
    }
}
