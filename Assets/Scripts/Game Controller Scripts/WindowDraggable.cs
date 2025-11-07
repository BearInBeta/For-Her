using UnityEngine;
using UnityEngine.EventSystems;

public class WindowDraggable : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    [SerializeField] private RectTransform topArea;   // Assign the "top" child here
    private RectTransform windowRect;
    private Canvas canvas;
    private Vector2 offset;
    private bool draggingAllowed = false;

    private void Awake()
    {
        windowRect = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // Only allow dragging if clicked inside the top bar
        if (IsPointerInsideTop(eventData))
        {
            draggingAllowed = true;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                windowRect,
                eventData.position,
                eventData.pressEventCamera,
                out offset
            );
        }
        else
        {
            draggingAllowed = false;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!draggingAllowed)
            return;

        Vector2 localPoint;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            windowRect.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out localPoint))
        {
            windowRect.anchoredPosition = localPoint - offset;
        }
    }

    private bool IsPointerInsideTop(PointerEventData eventData)
    {
        return RectTransformUtility.RectangleContainsScreenPoint(
            topArea,
            eventData.position,
            eventData.pressEventCamera
        );
    }
}
