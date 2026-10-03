using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("References")]
    public RectTransform JoyBackground;
    public RectTransform JoyHandle;

    [Header("Settings")]
    [SerializeField] public float MaxRadius = 140f;

    public Vector2 Output { get; private set; } = Vector2.zero;
    private bool _active = false;
    private Vector2 _center;
    private Canvas _canvas;

    void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
    }

    public void OnPointerDown(PointerEventData e)
    {
        _active = true;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            JoyBackground, e.position, e.pressEventCamera, out _center);
        UpdateHandle(e.position, e.pressEventCamera);
    }

    public void OnDrag(PointerEventData e)
    {
        if (!_active) return;
        UpdateHandle(e.position, e.pressEventCamera);
    }

    public void OnPointerUp(PointerEventData e)
    {
        _active = false;
        Output = Vector2.zero;
        if (JoyHandle) JoyHandle.anchoredPosition = Vector2.zero;
    }

    private void UpdateHandle(Vector2 screenPos, Camera cam)
    {
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            JoyBackground, screenPos, cam, out localPoint);
        Vector2 delta = localPoint - _center;
        float dist = delta.magnitude;
        Vector2 dir = dist > 0.01f ? delta / dist : Vector2.zero;
        float clampedDist = Mathf.Min(dist, MaxRadius);
        if (JoyHandle)
            JoyHandle.anchoredPosition = dir * clampedDist;
        Output = dir * (clampedDist / MaxRadius);
    }
}
