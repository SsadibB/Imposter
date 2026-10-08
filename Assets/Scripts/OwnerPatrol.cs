using UnityEngine;

public class OwnerPatrol : MonoBehaviour
{
    [Header("Patrol Settings")]
    public float minX = -650f;
    public float maxX = 650f;
    public float patrolSpeed = 130f;
    public float yPosition = 115f;
    public float pauseAtEdgeDuration = 0.5f;

    [Header("Walk Bobbing Visuals")]
    public float bobFrequency = 9f;
    public float bobHeight = 3.5f;
    public RectTransform bodyTransform;
    public RectTransform legsTransform;

    private RectTransform _rect;
    private bool _movingRight = true;
    private float _pauseTimer = 0f;
    private Vector2 _initialBodyPos;
    private Vector2 _initialLegsPos;

    void Awake()
    {
        _rect = GetComponent<RectTransform>();
        if (bodyTransform == null)
        {
            var b = transform.Find("Owner_Body");
            if (b != null) bodyTransform = b.GetComponent<RectTransform>();
        }
        if (legsTransform == null)
        {
            var l = transform.Find("Owner_Legs");
            if (l != null) legsTransform = l.GetComponent<RectTransform>();
        }

        if (bodyTransform != null) _initialBodyPos = bodyTransform.anchoredPosition;
        if (legsTransform != null) _initialLegsPos = legsTransform.anchoredPosition;

        if (_rect != null)
        {
            Vector2 pos = _rect.anchoredPosition;
            pos.y = yPosition;
            _rect.anchoredPosition = pos;
        }
    }

    void Update()
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsPlaying())
        {
            ResetBob();
            return;
        }

        if (_pauseTimer > 0f)
        {
            _pauseTimer -= Time.deltaTime;
            ResetBob();
            return;
        }

        float currentX = _rect.anchoredPosition.x;
        float targetX = _movingRight ? maxX : minX;
        float nextX = Mathf.MoveTowards(currentX, targetX, patrolSpeed * Time.deltaTime);

        _rect.anchoredPosition = new Vector2(nextX, yPosition);

        // Walking bob effect
        float bob = Mathf.Sin(Time.time * bobFrequency) * bobHeight;
        if (bodyTransform != null)
        {
            bodyTransform.anchoredPosition = new Vector2(_initialBodyPos.x, _initialBodyPos.y + bob);
        }
        if (legsTransform != null)
        {
            legsTransform.localEulerAngles = new Vector3(0f, 0f, Mathf.Sin(Time.time * bobFrequency) * 8f);
        }

        // Check if reached destination edge
        if (Mathf.Abs(nextX - targetX) < 0.5f)
        {
            _movingRight = !_movingRight;
            _pauseTimer = pauseAtEdgeDuration;
        }
    }

    private void ResetBob()
    {
        if (bodyTransform != null)
            bodyTransform.anchoredPosition = _initialBodyPos;
        if (legsTransform != null)
            legsTransform.localEulerAngles = Vector3.zero;
    }
}
