using UnityEngine;

public class SheepAI : MonoBehaviour
{
    [Header("References")]
    public SheepStatus status;
    public RectTransform flockBounds;
    public Transform visual;
    public Transform head;
    public GameObject grazeFX;
    public GameObject bleatBubble;
    public GameObject badMark;
    public RoundController roundController;

    [Header("Speeds")]
    public float wanderSpeed = 60f;
    public float scatterSpeed = 160f;

    private SheepBehaviorMode _mode = SheepBehaviorMode.Normal;
    private CommandType _activeCmd;
    private bool _hasCommand = false;
    private float _delay = 0f;
    private float _breakAt = 0f;
    private float _commandTime = 0f;
    private bool _performing = false;

    // Movement & wander
    private Vector2 _targetPos;
    private float _wanderTimer = 0f;
    private bool _idle = false;
    private float _scatterTimer = 0f;
    private Vector2 _scatterDir = Vector2.zero;
    private float _bleatInterval = 0f;
    private bool _facingRight = true;
    private bool _grazeOn = false;
    private bool _badOn = false;
    private float _headZ = 0f;

    // Shared across all sheep so a whole flock bleating together doesn't spam the audio pool
    private static float _lastBleatSfxTime = -10f;
    private const float BleatSfxMinGap = 0.15f;

    void Awake()
    {
        if (status == null) status = GetComponent<SheepStatus>();
        if (visual == null) visual = transform.Find("Visual");
        if (head == null && visual != null) head = visual.Find("Head");
        if (grazeFX == null && visual != null)
        {
            var g = visual.Find("GrazeFX");
            if (g != null) grazeFX = g.gameObject;
        }
        if (bleatBubble == null)
        {
            var b = transform.Find("BleatBubble");
            if (b != null) bleatBubble = b.gameObject;
        }
        if (badMark == null)
        {
            var bm = transform.Find("BadMark");
            if (bm != null) badMark = bm.gameObject;
        }
        _targetPos = status && status.rect ? status.rect.anchoredPosition : Vector2.zero;
        _wanderTimer = Random.Range(1f, 3f);
    }

    public void BeginCommand(CommandType cmd, SheepBehaviorMode mode)
    {
        _activeCmd = cmd;
        _mode = mode;
        _hasCommand = true;
        _commandTime = 0f;
        _performing = false;

        switch (mode)
        {
            case SheepBehaviorMode.Late:
                _delay = Random.Range(1.4f, 2.2f);
                break;
            case SheepBehaviorMode.Quitter:
                _delay = Random.Range(0.2f, 0.8f);
                _breakAt = Random.Range(1.0f, 2.0f);
                break;
            case SheepBehaviorMode.Normal:
            default:
                _delay = Random.Range(0.2f, 0.8f);
                break;
        }

        if (cmd == CommandType.Scatter)
        {
            PickNewScatterDir();
        }
        else if (cmd == CommandType.WalkSlowly)
        {
            PickRandomPoint();
            _idle = false;
        }
    }

    public void EndCommand()
    {
        _hasCommand = false;
        _performing = false;
        if (status != null) status.isBad = false;
        SetBadMark(false);
        SetGraze(false);
        if (bleatBubble && bleatBubble.activeSelf) bleatBubble.SetActive(false);
        SetHead(0f);
    }

    void Update()
    {
        if (status == null || !status.alive || !gameObject.activeSelf) return;
        if (MatchSession.Playing && !MatchSession.Instance.ShouldRunAI(status))
            return;
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying() && _hasCommand)
            EndCommand();

        Vector2 startPos = status.rect.anchoredPosition;

        if (_hasCommand)
        {
            UpdateCommandBehavior();
        }
        else
        {
            UpdateWander();
        }

        // Velocity & Tick
        Vector2 delta = status.rect.anchoredPosition - startPos;
        status.velocity = (Time.deltaTime > 0f) ? (delta / Time.deltaTime) : Vector2.zero;
        status.Tick(Time.deltaTime);

        // Facing flip
        if (Mathf.Abs(status.velocity.x) > 5f)
        {
            bool right = status.velocity.x > 0f;
            if (right != _facingRight)
            {
                _facingRight = right;
                if (visual)
                {
                    visual.localScale = new Vector3(_facingRight ? 1f : -1f, 1f, 1f);
                }
            }
        }

        FlockDepthSort.RequestTick(Time.deltaTime);
    }

    private void UpdateCommandBehavior()
    {
        _commandTime += Time.deltaTime;
        bool shouldPerform = (_commandTime >= _delay);

        if (_mode == SheepBehaviorMode.Quitter && _commandTime >= _breakAt && _commandTime <= _breakAt + 2.0f)
        {
            shouldPerform = false;
        }

        _performing = shouldPerform;

        // Is Bad?
        bool graceOver = roundController ? roundController.GraceOver : false;
        status.isBad = (graceOver && !_performing);
        SetBadMark(status.isBad);

        if (_performing)
        {
            switch (_activeCmd)
            {
                case CommandType.HeadsUp:
                    SetGraze(false);
                    SetHead(25f);
                    break;
                case CommandType.HeadsDownGraze:
                    SetGraze(true);
                    SetHead(-25f);
                    break;
                case CommandType.WalkSlowly:
                    SetGraze(false);
                    SetHead(0f);
                    UpdateWanderSlow();
                    break;
                case CommandType.AllStop:
                    SetGraze(false);
                    SetHead(0f);
                    break;
                case CommandType.Scatter:
                    SetGraze(false);
                    SetHead(0f);
                    _scatterTimer -= Time.deltaTime;
                    if (_scatterTimer <= 0f) PickNewScatterDir();
                    MoveTowardsPosition(status.rect.anchoredPosition + _scatterDir * scatterSpeed * Time.deltaTime, scatterSpeed);
                    break;
                case CommandType.MakeSounds:
                    SetGraze(false);
                    SetHead(0f);
                    _bleatInterval -= Time.deltaTime;
                    if (_bleatInterval <= 0f)
                    {
                        _bleatInterval = 1.0f;
                        if (bleatBubble && !bleatBubble.activeSelf) bleatBubble.SetActive(true);
                        PlayBleatSound();
                    }
                    else if (_bleatInterval < 0.3f && bleatBubble && bleatBubble.activeSelf)
                    {
                        bleatBubble.SetActive(false);
                    }
                    break;
            }
        }
        else
        {
            SetGraze(false);
            if (bleatBubble && bleatBubble.activeSelf) bleatBubble.SetActive(false);
            SetHead(0f);
            UpdateWander();
        }
    }

    private void SetGraze(bool on)
    {
        if (_grazeOn == on) return;
        _grazeOn = on;
        if (grazeFX) grazeFX.SetActive(on);
    }

    private void SetHead(float z)
    {
        if (Mathf.Abs(_headZ - z) < 0.01f) return;
        _headZ = z;
        if (head) head.localEulerAngles = new Vector3(0f, 0f, z);
    }

    private void SetBadMark(bool on)
    {
        if (_badOn == on) return;
        _badOn = on;
        if (badMark) badMark.SetActive(on);
    }

    private void PlayBleatSound()
    {
        if (SoundLibrary.Instance == null) return;
        if (Time.time - _lastBleatSfxTime < BleatSfxMinGap) return;
        _lastBleatSfxTime = Time.time;

        AudioSource src = SoundLibrary.Instance.PlaySFX("Sheep_Bleat");
        if (src != null)
        {
            // Slight pitch variation so each sheep sounds different, and a bit quieter than the player's bleat
            src.pitch = Random.Range(0.9f, 1.15f);
            src.volume *= 0.7f;
        }
    }

    private void UpdateWander()
    {
        _wanderTimer -= Time.deltaTime;
        if (_wanderTimer <= 0f)
        {
            _wanderTimer = Random.Range(2f, 4f);
            _idle = (Random.value < 0.35f);
            if (!_idle)
            {
                PickRandomPoint();
            }
        }

        if (!_idle)
        {
            MoveTowardsPosition(_targetPos, wanderSpeed);
        }
    }

    private void UpdateWanderSlow()
    {
        _wanderTimer -= Time.deltaTime;
        if (_wanderTimer <= 0f)
        {
            _wanderTimer = Random.Range(2f, 4f);
            _idle = false;
            PickRandomPoint();
        }
        MoveTowardsPosition(_targetPos, wanderSpeed * 0.5f);
    }

    private void MoveTowardsPosition(Vector2 target, float speed)
    {
        Vector2 cur = status.rect.anchoredPosition;
        Vector2 next = Vector2.MoveTowards(cur, target, speed * Time.deltaTime);

        if (flockBounds)
        {
            Vector2 half = flockBounds.sizeDelta * 0.5f;
            Vector2 center = flockBounds.anchoredPosition;
            next.x = Mathf.Clamp(next.x, center.x - half.x + 60f, center.x + half.x - 60f);
            next.y = Mathf.Clamp(next.y, center.y - half.y + 40f, center.y + half.y - 40f);
        }

        status.rect.anchoredPosition = next;
    }

    private void PickRandomPoint()
    {
        if (flockBounds)
        {
            Vector2 half = flockBounds.sizeDelta * 0.5f;
            Vector2 center = flockBounds.anchoredPosition;
            _targetPos = new Vector2(
                Random.Range(center.x - half.x + 80f, center.x + half.x - 80f),
                Random.Range(center.y - half.y + 60f, center.y + half.y - 60f)
            );
        }
    }

    private void PickNewScatterDir()
    {
        _scatterTimer = 0.7f;
        _scatterDir = Random.insideUnitCircle.normalized;
    }
}