using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the loading overlay used while GameController restores a saved session.
/// Place this on the loading screen root (or another always-active GameObject).
/// Assign the visual references in the Inspector.
/// </summary>
public class SessionLoadingScreen : MonoBehaviour
{
    [Header("Session Loading Screen")]
    [SerializeField] private GameObject loadingScreenRoot;
    [SerializeField] private CanvasGroup loadingScreenCanvasGroup;
    [SerializeField] private Image loadingBackground;
    [SerializeField] private RectTransform loadingSpinner;
    [SerializeField] private TMP_Text loadingScreenLabel;
    [SerializeField] private GameObject mainCanvas;

    [Header("Loading Screen Appearance")]
    [SerializeField] private Color loadingBackgroundColor =
        new Color(0.025f, 0.03f, 0.045f, 1f);
    [SerializeField] private Color loadingSpinnerColor = Color.white;
    [SerializeField, Min(0f)] private float loadingSpinnerSpeed = 180f;
    [SerializeField] private string loadingScreenText = "Restoring session...";
    [SerializeField] private TMP_FontAsset loadingScreenFont;

    [Header("Loading Screen Fade")]
    [SerializeField, Min(0f)] private float loadingFadeDuration = 0.45f;

    private bool loadingFadeActive;
    private float loadingFadeElapsed;
    private float loadingFadeStartAlpha;

    private void Update()
    {
        if (loadingScreenRoot == null || !loadingScreenRoot.activeInHierarchy)
            return;

        if (loadingSpinner != null)
        {
            loadingSpinner.Rotate(
                0f,
                0f,
                -loadingSpinnerSpeed * Time.unscaledDeltaTime
            );
        }

        UpdateFade();
    }

    /// <summary>Displays the loading UI while session data is restored.</summary>
    public void Show()
    {
        if (loadingScreenRoot == null)
        {
            Debug.LogWarning("Loading Screen Root is not assigned.");
            return;
        }

        loadingFadeActive = false;
        loadingFadeElapsed = 0f;
        loadingScreenRoot.SetActive(true);

        if (loadingScreenCanvasGroup != null)
        {
            loadingScreenCanvasGroup.alpha = 1f;
            loadingScreenCanvasGroup.interactable = false;
            loadingScreenCanvasGroup.blocksRaycasts = true;
        }

        if (loadingBackground != null)
            loadingBackground.color = loadingBackgroundColor;

        if (loadingSpinner != null)
        {
            Image[] images = loadingSpinner.GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                Color color = loadingSpinnerColor;
                color.a *= image.color.a;
                image.color = color;
            }
        }

        if (loadingScreenLabel != null)
        {
            loadingScreenLabel.text = loadingScreenText;
            if (loadingScreenFont != null)
                loadingScreenLabel.font = loadingScreenFont;
        }
    }

    /// <summary>Reveals the main canvas and smoothly fades away the loading UI.</summary>
    public void Dismiss()
    {
        if (mainCanvas != null)
            mainCanvas.SetActive(true);

        if (loadingScreenRoot == null || !loadingScreenRoot.activeSelf)
            return;

        if (loadingFadeActive)
            return;

        if (loadingScreenCanvasGroup == null || loadingFadeDuration <= 0f)
        {
            HideImmediately();
            return;
        }

        loadingFadeElapsed = 0f;
        loadingFadeStartAlpha = loadingScreenCanvasGroup.alpha;
        loadingFadeActive = true;
    }

    /// <summary>Hides the overlay without a fade (e.g., when unloading the controller).</summary>
    public void HideImmediately()
    {
        loadingFadeActive = false;
        loadingFadeElapsed = 0f;

        if (loadingScreenCanvasGroup != null)
        {
            loadingScreenCanvasGroup.alpha = 0f;
            loadingScreenCanvasGroup.interactable = false;
            loadingScreenCanvasGroup.blocksRaycasts = false;
        }

        if (loadingScreenRoot != null)
            loadingScreenRoot.SetActive(false);
    }

    private void UpdateFade()
    {
        if (!loadingFadeActive)
            return;

        if (loadingScreenRoot == null ||
            !loadingScreenRoot.activeInHierarchy ||
            loadingScreenCanvasGroup == null)
        {
            loadingFadeActive = false;
            return;
        }

        loadingFadeElapsed += Time.unscaledDeltaTime;

        float t = Mathf.Clamp01(
            loadingFadeElapsed / Mathf.Max(0.0001f, loadingFadeDuration)
        );

        float eased = Mathf.SmoothStep(0f, 1f, t);

        loadingScreenCanvasGroup.alpha =
            Mathf.Lerp(loadingFadeStartAlpha, 0f, eased);

        if (t >= 1f)
            HideImmediately();
    }
}
