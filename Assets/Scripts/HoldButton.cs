using UnityEngine;
using UnityEngine.EventSystems;

public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public bool IsHeld { get; private set; } = false;

    public void OnPointerDown(PointerEventData e) { IsHeld = true; }
    public void OnPointerUp(PointerEventData e) { IsHeld = false; }
}
