using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Opt-in persistence for a scene element. Attach this component only to objects
/// whose presentation state should be saved. Supported components are optional:
/// Transform / RectTransform, CanvasGroup, Graphic, TMP_Text, TMP_InputField,
/// Animator and WindowController.
///
/// The optional Save Key keeps the identity stable if the object is reparented or
/// renamed. If omitted, GameSaveSystem uses its scene-hierarchy path instead.
/// Do not reuse a Save Key on more than one SavedElement in the same scene.
/// </summary>
[DisallowMultipleComponent]
public sealed class SavedElement : MonoBehaviour
{
    [Header("Identity (optional, unique per scene)")]
    [SerializeField] private string saveKey;

    [Header("What to save (when component exists)")]
    [SerializeField] private bool saveTransform = true;
    [SerializeField] private bool saveVisibility = true;
    [SerializeField] private bool saveCanvasGroup = true;
    [SerializeField] private bool saveGraphicColor = true;
    [SerializeField] private bool saveText = false;
    [SerializeField] private bool saveAnimation = true;
    [SerializeField] private bool saveWindowState = true;

    public string SaveKey { get { return saveKey; } }

    [Serializable]
    public sealed class State
    {
        public string key;
        public bool hasTransform;
        public bool isRectTransform;
        public Vector3 localPosition, localScale;
        public Quaternion localRotation;
        public Vector2 anchoredPosition, sizeDelta, anchorMin, anchorMax, pivot;
        public int siblingIndex;

        public bool hasVisibility;
        public bool activeSelf;

        public bool hasCanvasGroup;
        public float alpha;
        public bool interactable, blocksRaycasts;

        public bool hasGraphicColor;
        public Color graphicColor;

        public bool hasText;
        public bool isInputField;
        public string text;

        public bool hasAnimator;
        public bool hasAnimation;
        public int animationStateHash;
        public float animationTime, animationSpeed;
        public bool animatorEnabled;

        public bool hasWindow;
        public int windowState;
    }

    internal State Capture(string key)
    {
        var state = new State { key = key };
        if (saveTransform)
        {
            state.hasTransform = true;
            state.localPosition = transform.localPosition;
            state.localRotation = transform.localRotation;
            state.localScale = transform.localScale;
            state.siblingIndex = transform.GetSiblingIndex();
            var rect = transform as RectTransform;
            if (rect != null)
            {
                state.isRectTransform = true;
                state.anchoredPosition = rect.anchoredPosition;
                state.sizeDelta = rect.sizeDelta;
                state.anchorMin = rect.anchorMin;
                state.anchorMax = rect.anchorMax;
                state.pivot = rect.pivot;
            }
        }

        if (saveVisibility)
        {
            state.hasVisibility = true;
            state.activeSelf = gameObject.activeSelf;
        }
        if (saveCanvasGroup)
        {
            var group = GetComponent<CanvasGroup>();
            if (group != null)
            {
                state.hasCanvasGroup = true;
                state.alpha = group.alpha;
                state.interactable = group.interactable;
                state.blocksRaycasts = group.blocksRaycasts;
            }
        }
        if (saveGraphicColor)
        {
            var graphic = GetComponent<Graphic>();
            if (graphic != null)
            {
                state.hasGraphicColor = true;
                state.graphicColor = graphic.color;
            }
        }
        if (saveText)
        {
            var input = GetComponent<TMP_InputField>();
            if (input != null)
            {
                state.hasText = true;
                state.isInputField = true;
                state.text = input.text;
            }
            else
            {
                var tmp = GetComponent<TMP_Text>();
                if (tmp != null)
                {
                    state.hasText = true;
                    state.text = tmp.text;
                }
            }
        }
        if (saveAnimation)
        {
            var animator = GetComponent<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                state.hasAnimator = true;
                state.animatorEnabled = animator.enabled;
                // Inactive or uninitialized animators cannot provide reliable
                // current state; simply omit the animation checkpoint.
                if (animator.isActiveAndEnabled && animator.isInitialized && animator.layerCount > 0)
                {
                    var info = animator.GetCurrentAnimatorStateInfo(0);
                    if (info.fullPathHash != 0)
                    {
                        state.hasAnimation = true;
                        state.animationStateHash = info.fullPathHash;
                        state.animationTime = info.normalizedTime;
                        state.animationSpeed = animator.speed;
                    }
                }
            }
        }
        if (saveWindowState)
        {
            var window = GetComponent<WindowController>();
            if (window != null)
            {
                state.hasWindow = true;
                state.windowState = (int)window.State;
            }
        }
        return state;
    }

    internal void Restore(State state)
    {
        if (state == null) return;
        // Geometry is restored before activation. This avoids visible movement
        // for UI elements which are initially disabled.
        if (saveTransform && state.hasTransform)
        {
            transform.localPosition = state.localPosition;
            transform.localRotation = state.localRotation;
            transform.localScale = state.localScale;
            var rect = transform as RectTransform;
            if (rect != null && state.isRectTransform)
            {
                rect.anchorMin = state.anchorMin;
                rect.anchorMax = state.anchorMax;
                rect.pivot = state.pivot;
                rect.sizeDelta = state.sizeDelta;
                rect.anchoredPosition = state.anchoredPosition;
            }
            if (transform.parent != null)
                transform.SetSiblingIndex(Mathf.Clamp(state.siblingIndex, 0, transform.parent.childCount - 1));
        }

        var window = saveWindowState && state.hasWindow ? GetComponent<WindowController>() : null;
        if (window != null)
        {
            // WindowController must own its visibility, CanvasGroup and animator
            // so its internal WindowState remains consistent with the UI.
            var mode = (WindowController.WindowState)state.windowState;
            if (mode >= WindowController.WindowState.Open && mode <= WindowController.WindowState.Closed)
                window.RestoreState(mode, !state.hasVisibility || state.activeSelf);
        }
        else if (saveVisibility && state.hasVisibility)
        {
            gameObject.SetActive(state.activeSelf);
        }

        if (saveGraphicColor && state.hasGraphicColor)
        {
            var graphic = GetComponent<Graphic>();
            if (graphic != null) graphic.color = state.graphicColor;
        }
        if (saveText && state.hasText)
        {
            if (state.isInputField)
            {
                var input = GetComponent<TMP_InputField>();
                if (input != null) input.SetTextWithoutNotify(state.text ?? "");
            }
            else
            {
                var tmp = GetComponent<TMP_Text>();
                if (tmp != null) tmp.text = state.text ?? "";
            }
        }
        if (window == null && saveCanvasGroup && state.hasCanvasGroup)
        {
            var group = GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = state.alpha;
                group.interactable = state.interactable;
                group.blocksRaycasts = state.blocksRaycasts;
            }
        }
        if (window == null && saveAnimation && state.hasAnimator)
        {
            var animator = GetComponent<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.enabled = state.animatorEnabled;
                if (state.hasAnimation && animator.isActiveAndEnabled && animator.isInitialized &&
                    animator.HasState(0, state.animationStateHash))
                {
                    animator.speed = state.animationSpeed;
                    animator.Play(state.animationStateHash, 0, state.animationTime);
                    animator.Update(0f);
                }
            }
        }
    }
}
