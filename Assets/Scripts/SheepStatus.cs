using UnityEngine;

public class SheepStatus : MonoBehaviour
{
    public bool alive = true;
    public bool isWolf = false;
    public bool isBad = false;
    public float suspicion = 0f; // 0..100
    public Vector2 velocity = Vector2.zero;
    public RectTransform rect;
    public float badRate = 20f;
    public float goodDecay = 4f;

    void Awake()
    {
        if (rect == null)
            rect = GetComponent<RectTransform>();
    }

    public void Tick(float dt)
    {
        if (!alive) return;
        suspicion = Mathf.Clamp(suspicion + (isBad ? badRate : -goodDecay) * dt, 0f, 100f);
    }

    public void ResetStatus()
    {
        alive = true;
        isBad = false;
        suspicion = 0f;
        velocity = Vector2.zero;
    }
}
