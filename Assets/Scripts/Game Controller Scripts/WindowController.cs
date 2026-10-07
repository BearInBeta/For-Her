using System.Collections;
using UnityEngine;

public class WindowController : MonoBehaviour
{
    private Animator animator;
    private CanvasGroup canvasGroup;

    private string minimizeTrigger = "Minimize";
    private string maximizeTrigger = "Maximize";
    private Vector3 originalPosition;
    private void Awake()
    {
        originalPosition = transform.position;
        animator = GetComponent<Animator>();
        canvasGroup = GetComponent<CanvasGroup>();

        // STARTUP: match animator state to actual GameObject state
        if (gameObject.activeSelf)
        {
            // Window should start fully open
            animator.Play("Idle", 0, 1f);
            canvasGroup.alpha = 1;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
        else
        {
            // Window should start fully closed
            animator.Play("Idle", 0, 1f);
        }
        
    }
    private void Start()
    {

        
    }




    public void Minimize()
    {
        if (!gameObject.activeSelf)
            return;


        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        animator.SetTrigger(minimizeTrigger);

        StartCoroutine(DisableAfterAnimation());

    }

    public void Maximize()
    {

        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
            transform.position = originalPosition;
        }

        // Enable object first
        gameObject.SetActive(true);


        // Disable interaction until animation is done


        animator.SetTrigger(maximizeTrigger);
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
       
    }



    private IEnumerator DisableAfterAnimation()
    {
        yield return new WaitForSeconds(animator.GetCurrentAnimatorStateInfo(0).length);
        gameObject.SetActive(false);
    }
}
