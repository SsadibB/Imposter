using UnityEngine;
using UnityEngine.UI;

public class WolfController : MonoBehaviour
{
    [Header("Settings")]
    public float moveSpeed = 150f;

    [Header("Dash")]
    public Button btnDash;

    [Tooltip("Seconds the dash button stays inactive after one dash.")]
    public float dashCooldown = 1.5f;

    public float dashSpeed = 1200f;

    [Tooltip("How long the dash burst lasts, in seconds.")]
    public float dashDuration = 0.25f;

    [Header("References")]
    public SheepStatus status;
    public RectTransform flockBounds;
    public Transform visual;
    public Transform head;
    public GameObject grazeFX;
    public GameObject bleatBubble;
    public VirtualJoystick joystick;
    public HoldButton btnGraze;
    public HoldButton btnLook;
    public HoldButton btnWalkSlowly;
    public HoldButton btnScatter;
    public Button btnBleat;
    public RoundController roundController;
    public HUDController hudController;

    private float _bleatTimer = 0f;
    private bool _facingRight = true;
    private bool _hasCommand = false;
    private bool _grazeOn = false;
    private float _headZ = 0f;
    private int _bodyScaleState = 0;
    private CommandType _activeCmd;

    private float _dashTimeLeft = 0f;
    private float _dashCooldownLeft = 0f;
    private Vector2 _dashDir = Vector2.right;
    private Image _dashRest;
    private float _airEmit = 0f;
    private RectTransform _airRoot;
    private AirStreak[] _streaks;
    private int _liveStreaks;

    void Awake()
    {
        if (status == null) status = GetComponent<SheepStatus>();
        if (status != null) status.isWolf = true;
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
    }

    void Start()
    {
        if (btnBleat)
        {
            btnBleat.onClick.AddListener(OnBleatClicked);
        }

        var shield = GetComponent<ShieldPickup>();
        if (shield == null) shield = gameObject.AddComponent<ShieldPickup>();
        if (shield.flockBounds == null) shield.flockBounds = flockBounds;
        if (shield.imposter == null) shield.imposter = status;
        if (shield.hudController == null) shield.hudController = hudController;

        EnsureDashButton();
        EnsureAirflow();
    }

    public void OnBleatClicked()
    {
        _bleatTimer = 1.2f;
        if (bleatBubble) bleatBubble.SetActive(true);

        if (SoundLibrary.Instance != null && GameManager.Instance != null && GameManager.Instance.IsPlaying())
            SoundLibrary.Instance.PlaySFX("Sheep_Bleat");
    }

    public void OnDashClicked()
    {
        if (_dashCooldownLeft > 0f || _dashTimeLeft > 0f) return;
        if (status == null || !status.alive || status.rect == null) return;
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying()) return;

        Vector2 dir = ReadMoveInput();
        if (dir.sqrMagnitude < 0.04f)
            dir = _facingRight ? Vector2.right : Vector2.left;

        _dashDir = dir.normalized;
        _dashTimeLeft = Mathf.Max(0.05f, dashDuration);
        _dashCooldownLeft = Mathf.Max(0f, dashCooldown);

        if (Mathf.Abs(_dashDir.x) > 0.1f)
            _facingRight = _dashDir.x > 0f;

        if (SoundLibrary.Instance != null)
            SoundLibrary.Instance.PlaySFX("Dashing");

        ApplyBodyScale();
        SpawnDashBurst();
        RefreshDashButton();
    }

    public void BeginCommand(CommandType cmd)
    {
        _activeCmd = cmd;
        _hasCommand = true;
    }

    public void EndCommand()
    {
        _hasCommand = false;
        if (status != null) status.isBad = false;
        SetGraze(false);
        if (bleatBubble && bleatBubble.activeSelf) bleatBubble.SetActive(false);
        SetHead(0f);
        if (hudController) hudController.HideStatus();
    }

    void Update()
    {
        if (status == null || !gameObject.activeSelf) return;
        if (MatchSession.Playing && !MatchSession.Instance.RunsLocalSheep(status))
            return;
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying())
            return;

        if (_dashCooldownLeft > 0f)
        {
            _dashCooldownLeft -= Time.deltaTime;
            if (_dashCooldownLeft < 0f) _dashCooldownLeft = 0f;
        }

        TickAirflow(Time.deltaTime);

        if (!status.alive)
        {
            _dashTimeLeft = 0f;
            ApplyBodyScale();
            RefreshDashButton();
            return;
        }

        // Bleat timer countdown
        if (_bleatTimer > 0f)
        {
            _bleatTimer -= Time.deltaTime;
            if (_bleatTimer <= 0f && bleatBubble)
            {
                bleatBubble.SetActive(false);
            }
        }
        if (Input.GetKeyDown(KeyCode.B))
        {
            OnBleatClicked();
        }
        if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.K))
        {
            OnDashClicked();
        }

        // Look / Heads Up hold
        bool keyLook = Input.GetKey(KeyCode.L) || Input.GetKey(KeyCode.Space);
        bool btnLookHeld = btnLook != null && btnLook.IsHeld;
        bool isLooking = keyLook || btnLookHeld;

        // Graze hold
        bool keyGraze = Input.GetKey(KeyCode.G);
        bool btnGrazeHeld = btnGraze != null && btnGraze.IsHeld;
        bool isGrazing = (keyGraze || btnGrazeHeld) && !isLooking;

        Vector2 moveInput = ReadMoveInput();

        // Speed adjustment for Walk Slowly command
        float curSpeed = moveSpeed;
        if (_hasCommand && roundController && roundController.ActiveCommand == CommandType.WalkSlowly)
        {
            curSpeed = moveSpeed * 0.5f;
        }

        // While grazing or looking up, wolf cannot walk. A dash still breaks out.
        bool canMove = !isGrazing && !isLooking;
        bool dashing = _dashTimeLeft > 0f;
        Vector2 startPos = status.rect.anchoredPosition;
        if (dashing)
        {
            _dashTimeLeft -= Time.deltaTime;
            if (_dashTimeLeft < 0f) _dashTimeLeft = 0f;

            Vector2 newPos = ClampToFlock(startPos + _dashDir * dashSpeed * Time.deltaTime);
            status.rect.anchoredPosition = newPos;

            if (Mathf.Abs(_dashDir.x) > 0.1f)
                _facingRight = _dashDir.x > 0f;

            _airEmit -= Time.deltaTime;
            if (_airEmit <= 0f)
            {
                _airEmit = 0.03f;
                for (int i = 0; i < 3; i++)
                    SpawnStreak(false);
            }
        }
        else if (canMove && moveInput.magnitude > 0.01f)
        {
            Vector2 newPos = ClampToFlock(startPos + moveInput * curSpeed * Time.deltaTime);
            status.rect.anchoredPosition = newPos;

            // Flip facing
            if (Mathf.Abs(moveInput.x) > 0.1f)
                _facingRight = moveInput.x > 0f;
        }

        ApplyBodyScale();

        // Head & Graze Visuals. Only write when the pose actually changes.
        if (dashing)
        {
            SetGraze(false);
            SetHead(0f);
        }
        else if (isGrazing)
        {
            SetGraze(true);
            SetHead(-25f);
        }
        else if (isLooking)
        {
            SetGraze(false);
            SetHead(25f);
        }
        else
        {
            SetGraze(false);
            SetHead(0f);
        }

        // Velocity & Tick
        Vector2 delta = status.rect.anchoredPosition - startPos;
        status.velocity = (Time.deltaTime > 0f) ? (delta / Time.deltaTime) : Vector2.zero;
        status.Tick(Time.deltaTime);

        // Command performance evaluation
        if (_hasCommand && roundController && roundController.CommandActive)
        {
            float moveMag = dashing ? 1f : moveInput.magnitude;
            bool performing = IsPerforming(_activeCmd, isGrazing && !dashing, isLooking && !dashing, moveMag, _bleatTimer > 0f);

            if (roundController.GraceOver)
            {
                status.isBad = !performing;
                if (hudController)
                {
                    hudController.ShowStatus(performing);
                }
            }
            else
            {
                status.isBad = false;
                if (hudController) hudController.HideStatus();
            }
        }
        else
        {
            status.isBad = false;
            if (hudController) hudController.HideStatus();
        }

        // Suspicion bar update
        if (hudController)
        {
            hudController.UpdateSuspicion(status.suspicion);
        }

        RefreshDashButton();
        FlockDepthSort.RequestTick(Time.deltaTime);

        if (MatchSession.Playing)
            MatchSession.Instance.NoteSheep(isGrazing && !dashing, isLooking && !dashing, _bleatTimer > 0f, dashing ? 1f : moveInput.magnitude, _facingRight);
    }

    private Vector2 ReadMoveInput()
    {
        float kx = 0f, ky = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) ky += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) ky -= 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) kx -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) kx += 1f;

        Vector2 kbInput = new Vector2(kx, ky);
        if (kbInput.magnitude > 1f) kbInput.Normalize();

        Vector2 joyInput = joystick ? joystick.Output : Vector2.zero;
        Vector2 moveInput = (kbInput.magnitude > 0.1f) ? kbInput : joyInput;
        if (moveInput.magnitude > 1f) moveInput.Normalize();
        return moveInput;
    }

    private Vector2 ClampToFlock(Vector2 pos)
    {
        if (!flockBounds) return pos;
        Vector2 half = flockBounds.sizeDelta * 0.5f;
        Vector2 center = flockBounds.anchoredPosition;
        pos.x = Mathf.Clamp(pos.x, center.x - half.x + 60f, center.x + half.x - 60f);
        pos.y = Mathf.Clamp(pos.y, center.y - half.y + 40f, center.y + half.y - 40f);
        return pos;
    }

    private void ApplyBodyScale()
    {
        if (!visual) return;
        int state = (_facingRight ? 1 : 2) + (_dashTimeLeft > 0f ? 2 : 0);
        if (state == _bodyScaleState) return;
        _bodyScaleState = state;

        float sx = _facingRight ? 1f : -1f;
        if (_dashTimeLeft > 0f)
            visual.localScale = new Vector3(sx * 1.2f, 0.86f, 1f);
        else
            visual.localScale = new Vector3(sx, 1f, 1f);
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

    private void EnsureDashButton()
    {
        if (btnDash == null) return;

        btnDash.onClick.RemoveListener(OnDashClicked);
        btnDash.onClick.AddListener(OnDashClicked);

        Transform rest = btnDash.transform.Find("DashRest");
        if (rest != null)
            _dashRest = rest.GetComponent<Image>();

        RefreshDashButton();
    }

    private void RefreshDashButton()
    {
        if (btnDash == null) return;
        bool ready = _dashCooldownLeft <= 0f && _dashTimeLeft <= 0f;
        if (btnDash.interactable != ready)
            btnDash.interactable = ready;

        if (_dashRest == null) return;
        float denom = Mathf.Max(0.01f, dashCooldown);
        float fill = _dashCooldownLeft <= 0f ? 0f : Mathf.Clamp01(_dashCooldownLeft / denom);
        _dashRest.fillAmount = fill;
        _dashRest.enabled = fill > 0.001f;
    }

    private void EnsureAirflow()
    {
        if (_streaks != null) return;

        RectTransform layer = FieldLayer.Get(transform);
        if (layer == null) return;

        var rootGo = new GameObject("AirBrakeFlow", typeof(RectTransform));
        rootGo.transform.SetParent(layer, false);
        _airRoot = rootGo.GetComponent<RectTransform>();
        _airRoot.anchorMin = Vector2.zero;
        _airRoot.anchorMax = Vector2.one;
        _airRoot.offsetMin = Vector2.zero;
        _airRoot.offsetMax = Vector2.zero;
        _airRoot.pivot = new Vector2(0.5f, 0.5f);

        _streaks = new AirStreak[48];
        for (int i = 0; i < _streaks.Length; i++)
        {
            var go = new GameObject("Streak", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_airRoot, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.GetComponent<Image>();
            img.sprite = RuntimeSprites.Circle;
            img.raycastTarget = false;
            go.SetActive(false);
            _streaks[i] = new AirStreak { go = go, rect = rt, image = img };
        }
    }

    private void SpawnDashBurst()
    {
        EnsureAirflow();
        SpawnStreak(true);
        for (int i = 0; i < 12; i++)
            SpawnStreak(false);
        _airEmit = 0.03f;
    }

    private void SpawnStreak(bool ring)
    {
        if (_streaks == null || status == null || status.rect == null) return;
        AirStreak s = GetFreeStreak();
        if (s == null) return;

        Vector2 sheepPos = status.rect.anchoredPosition;
        Vector2 side = new Vector2(-_dashDir.y, _dashDir.x);
        if (!s.go.activeSelf)
            _liveStreaks++;
        s.go.SetActive(true);
        s.ring = ring;

        if (ring)
        {
            s.pos = sheepPos;
            s.vel = Vector2.zero;
            s.maxLife = 0.34f;
            s.life = s.maxLife;
            s.baseLength = 100f;
            s.baseWidth = 100f;
            s.rect.sizeDelta = new Vector2(100f, 100f);
            s.rect.localScale = Vector3.one * 0.3f;
            s.rect.localEulerAngles = Vector3.zero;
            s.color = new Color(0.82f, 0.96f, 1f, 0.8f);
        }
        else
        {
            float lateral = Random.Range(-48f, 48f);
            float behind = Random.Range(-8f, 30f);
            s.pos = sheepPos - _dashDir * behind + side * lateral;
            s.vel = -_dashDir * Random.Range(160f, 380f) + side * Random.Range(-50f, 50f);
            s.maxLife = Random.Range(0.2f, 0.42f);
            s.life = s.maxLife;
            s.baseLength = Random.Range(90f, 190f);
            s.baseWidth = Random.Range(8f, 18f);
            s.rect.sizeDelta = new Vector2(s.baseLength, s.baseWidth);
            s.rect.localScale = Vector3.one;
            float ang = Mathf.Atan2(_dashDir.y, _dashDir.x) * Mathf.Rad2Deg + Random.Range(-12f, 12f);
            s.rect.localEulerAngles = new Vector3(0f, 0f, ang);
            bool bright = Random.value > 0.65f;
            s.color = bright
                ? new Color(1f, 1f, 1f, 0.95f)
                : new Color(0.62f, 0.9f, 1f, 0.85f);
        }

        s.image.color = s.color;
        s.rect.anchoredPosition = s.pos;
    }

    private void TickAirflow(float dt)
    {
        if (_streaks == null || _liveStreaks <= 0) return;
        for (int i = 0; i < _streaks.Length; i++)
        {
            AirStreak s = _streaks[i];
            if (s == null || !s.go.activeSelf) continue;

            s.life -= dt;
            if (s.life <= 0f)
            {
                s.go.SetActive(false);
                _liveStreaks--;
                continue;
            }

            s.pos += s.vel * dt;
            s.rect.anchoredPosition = s.pos;

            float lifeT = Mathf.Clamp01(s.life / s.maxLife);
            float age = 1f - lifeT;
            Color c = s.color;
            c.a = s.color.a * lifeT;
            if (age < 0.12f) c.a *= age / 0.12f;
            s.image.color = c;

            if (s.ring)
            {
                float sc = Mathf.Lerp(0.35f, 2.8f, age);
                s.rect.localScale = new Vector3(sc, sc, 1f);
            }
            else
            {
                float stretch = Mathf.Lerp(0.55f, 1.25f, age);
                s.rect.sizeDelta = new Vector2(s.baseLength * stretch, s.baseWidth);
            }
        }
    }

    private AirStreak GetFreeStreak()
    {
        if (_streaks == null) return null;
        AirStreak oldest = null;
        for (int i = 0; i < _streaks.Length; i++)
        {
            if (!_streaks[i].go.activeSelf)
                return _streaks[i];
            if (oldest == null || _streaks[i].life < oldest.life)
                oldest = _streaks[i];
        }
        return oldest;
    }

    public static bool IsPerforming(CommandType cmd, bool isGrazing, bool isLooking, float moveMag, bool isBleating)
    {
        switch (cmd)
        {
            case CommandType.HeadsUp:
                return isLooking && !isGrazing;
            case CommandType.HeadsDownGraze:
                return isGrazing && !isLooking;
            case CommandType.WalkSlowly:
                return moveMag > 0.05f;
            case CommandType.Scatter:
                return moveMag > 0.4f;
            case CommandType.MakeSounds:
                return isBleating;
            case CommandType.AllStop:
                return moveMag < 0.1f;
            default:
                return false;
        }
    }

    class AirStreak
    {
        public GameObject go;
        public RectTransform rect;
        public Image image;
        public float life;
        public float maxLife;
        public Vector2 pos;
        public Vector2 vel;
        public float baseLength;
        public float baseWidth;
        public Color color;
        public bool ring;
    }
}
