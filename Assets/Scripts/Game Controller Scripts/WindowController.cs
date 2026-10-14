
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class WindowController : MonoBehaviour
{
    public enum WindowState { Open, Minimized, Closed }
    public WindowState State { get; private set; } = WindowState.Open;
    private Animator animator;
    private CanvasGroup canvasGroup;

    private string minimizeTrigger = "Minimize";
    private string maximizeTrigger = "Maximize";
    private string closeTrigger = "Close";

    private Vector3 originalPosition;
    private Coroutine disableCoroutine;

    private void Awake()
    {
        originalPosition = transform.position;
        animator = GetComponent<Animator>();
        canvasGroup = GetComponent<CanvasGroup>();

        if (gameObject.activeSelf)
        {
            animator.Play("Idle", 0, 1f);
            canvasGroup.alpha = 1;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

   

    public void Minimize()
    {
        if (!gameObject.activeSelf)
            return;

        State = WindowState.Minimized;
        StopDisableCoroutine();

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        animator.ResetTrigger(maximizeTrigger);
        animator.SetTrigger(minimizeTrigger);

        disableCoroutine = StartCoroutine(
            DisableAfterAnimation("Minimize", false)
        );
    }

    public void Close()
    {
        if (!gameObject.activeSelf)
            return;

        State = WindowState.Closed;
        StopDisableCoroutine();

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        animator.ResetTrigger(maximizeTrigger);
        animator.ResetTrigger(minimizeTrigger);
        animator.SetTrigger(closeTrigger);

        disableCoroutine = StartCoroutine(
            DisableAfterAnimation("Close", true)
        );
    }

    public void Maximize()
    {
        State = WindowState.Open;
        StopDisableCoroutine();

        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
            transform.position = originalPosition;
        }

        gameObject.SetActive(true);

        // Bring this window to the front.
        transform.SetAsLastSibling();

        animator.ResetTrigger(minimizeTrigger);
        animator.ResetTrigger(closeTrigger);
        animator.SetTrigger(maximizeTrigger);

        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    public void RestoreState(WindowState state, bool active)
    {
        StopDisableCoroutine();
        State = state;
        // Preserve the restored window position when it is opened again.
        // Maximize() uses originalPosition when reactivating a minimized window.
        originalPosition = transform.position;
        bool show = active && state == WindowState.Open;
        gameObject.SetActive(show);
        if (show && animator != null)
        {
            animator.Play("Idle", 0, 1f);
            animator.Update(0f);
        }
        if (canvasGroup != null)
        {
            canvasGroup.alpha = show ? 1f : 0f;
            canvasGroup.interactable = show;
            canvasGroup.blocksRaycasts = show;
        }
    }

    private IEnumerator DisableAfterAnimation(
        string stateName,
        bool resetPosition
    )
    {
        // Wait until Animator enters the requested state.
        yield return new WaitUntil(() =>
            animator.GetCurrentAnimatorStateInfo(0).IsName(stateName)
        );

        // Wait until the animation finishes.
        yield return new WaitUntil(() =>
            animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f
        );

        if (resetPosition)
        {
            transform.position = originalPosition;
        }

        disableCoroutine = null;
        gameObject.SetActive(false);
    }

    private void StopDisableCoroutine()
    {
        if (disableCoroutine != null)
        {
            StopCoroutine(disableCoroutine);
            disableCoroutine = null;
        }
    }
}
