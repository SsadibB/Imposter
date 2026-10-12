using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives one flock sheep from the local player's buttons. Used when a friend is not the wolf sheep.
/// </summary>
public class SheepPilot : MonoBehaviour
{
    public SheepStatus status;
    public RectTransform flockBounds;
    public Transform visual;
    public Transform head;
    public GameObject grazeFX;
    public GameObject bleatBubble;
    public VirtualJoystick joystick;
    public HoldButton btnGraze;
    public HoldButton btnLook;
    public Button btnDash;
    public RoundController roundController;
    public HUDController hudController;

    public float moveSpeed = 150f;
    public float dashSpeed = 1200f;
    public float dashDuration = 0.25f;
    public float dashCooldown = 1.5f;

    float _bleatTimer;
    bool _facingRight = true;
    bool _hasCommand;
    bool _grazeOn;
    float _headZ;
    CommandType _activeCmd;
    float _dashTimeLeft;
    float _dashCooldownLeft;
    Vector2 _dashDir = Vector2.right;
    Image _dashRest;

    void Awake()
    {
        if (status == null) status = GetComponent<SheepStatus>();
        if (visual == null) visual = transform.Find("Visual");
        if (head == null && visual != null) head = visual.Find("Head");
        if (grazeFX == null && visual != null)
        {
            Transform g = visual.Find("GrazeFX");
            if (g != null) grazeFX = g.gameObject;
        }
        if (bleatBubble == null)
        {
            Transform b = transform.Find("BleatBubble");
            if (b != null) bleatBubble = b.gameObject;
        }
    }

    public void BindDash(Button dash)
    {
        btnDash = dash;
        if (dash == null) return;
        Transform rest = dash.transform.Find("DashRest");
        if (rest != null) _dashRest = rest.GetComponent<Image>();
        RefreshDashButton();
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

        if (_dashCooldownLeft > 0f)
        {
            _dashCooldownLeft -= Time.deltaTime;
            if (_dashCooldownLeft < 0f) _dashCooldownLeft = 0f;
        }

        if (!status.alive)
        {
            _dashTimeLeft = 0f;
            RefreshDashButton();
            return;
        }

        if (_bleatTimer > 0f)
        {
            _bleatTimer -= Time.deltaTime;
            if (_bleatTimer <= 0f && bleatBubble)
                bleatBubble.SetActive(false);
        }
        if (Input.GetKeyDown(KeyCode.B))
            OnBleatClicked();
        if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.K))
            OnDashClicked();

        bool isLooking = Input.GetKey(KeyCode.L) || Input.GetKey(KeyCode.Space) || (btnLook != null && btnLook.IsHeld);
        bool isGrazing = (Input.GetKey(KeyCode.G) || (btnGraze != null && btnGraze.IsHeld)) && !isLooking;
        Vector2 moveInput = ReadMoveInput();

        float curSpeed = moveSpeed;
        if (_hasCommand && roundController && roundController.ActiveCommand == CommandType.WalkSlowly)
            curSpeed = moveSpeed * 0.5f;

        bool canMove = !isGrazing && !isLooking;
        bool dashing = _dashTimeLeft > 0f;
        Vector2 startPos = status.rect.anchoredPosition;
        if (dashing)
        {
            _dashTimeLeft -= Time.deltaTime;
            if (_dashTimeLeft < 0f) _dashTimeLeft = 0f;
            status.rect.anchoredPosition = ClampToFlock(startPos + _dashDir * dashSpeed * Time.deltaTime);
            if (Mathf.Abs(_dashDir.x) > 0.1f)
                _facingRight = _dashDir.x > 0f;
        }
        else if (canMove && moveInput.magnitude > 0.01f)
        {
            status.rect.anchoredPosition = ClampToFlock(startPos + moveInput * curSpeed * Time.deltaTime);
            if (Mathf.Abs(moveInput.x) > 0.1f)
                _facingRight = moveInput.x > 0f;
        }

        ApplyBodyScale();
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

        Vector2 delta = status.rect.anchoredPosition - startPos;
        status.velocity = (Time.deltaTime > 0f) ? (delta / Time.deltaTime) : Vector2.zero;
        status.Tick(Time.deltaTime);

        if (_hasCommand && roundController && roundController.CommandActive)
        {
            float moveMag = dashing ? 1f : moveInput.magnitude;
            bool performing = WolfController.IsPerforming(_activeCmd, isGrazing && !dashing, isLooking && !dashing, moveMag, _bleatTimer > 0f);
            if (roundController.GraceOver)
            {
                status.isBad = !performing;
                if (hudController) hudController.ShowStatus(performing);
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

        if (hudController)
            hudController.UpdateSuspicion(status.suspicion);

        RefreshDashButton();
        FlockDepthSort.RequestTick(Time.deltaTime);

        if (MatchSession.Playing)
            MatchSession.Instance.NoteSheep(isGrazing && !dashing, isLooking && !dashing, _bleatTimer > 0f, dashing ? 1f : moveInput.magnitude, _facingRight);
    }

    Vector2 ReadMoveInput()
    {
        float kx = 0f, ky = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) ky += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) ky -= 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) kx -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) kx += 1f;
        Vector2 kb = new Vector2(kx, ky);
        if (kb.magnitude > 1f) kb.Normalize();
        Vector2 joy = joystick ? joystick.Output : Vector2.zero;
        Vector2 move = (kb.magnitude > 0.1f) ? kb : joy;
        if (move.magnitude > 1f) move.Normalize();
        return move;
    }

    Vector2 ClampToFlock(Vector2 pos)
    {
        if (!flockBounds) return pos;
        Vector2 half = flockBounds.sizeDelta * 0.5f;
        Vector2 center = flockBounds.anchoredPosition;
        pos.x = Mathf.Clamp(pos.x, center.x - half.x + 60f, center.x + half.x - 60f);
        pos.y = Mathf.Clamp(pos.y, center.y - half.y + 40f, center.y + half.y - 40f);
        return pos;
    }

    void ApplyBodyScale()
    {
        if (!visual) return;
        float sx = _facingRight ? 1f : -1f;
        visual.localScale = _dashTimeLeft > 0f ? new Vector3(sx * 1.2f, 0.86f, 1f) : new Vector3(sx, 1f, 1f);
    }

    void SetGraze(bool on)
    {
        if (_grazeOn == on) return;
        _grazeOn = on;
        if (grazeFX) grazeFX.SetActive(on);
    }

    void SetHead(float z)
    {
        if (Mathf.Abs(_headZ - z) < 0.01f) return;
        _headZ = z;
        if (head) head.localEulerAngles = new Vector3(0f, 0f, z);
    }

    void RefreshDashButton()
    {
        if (btnDash == null) return;
        bool ready = _dashCooldownLeft <= 0f && _dashTimeLeft <= 0f;
        if (btnDash.interactable != ready)
            btnDash.interactable = ready;
        if (_dashRest == null) return;
        float fill = _dashCooldownLeft <= 0f ? 0f : Mathf.Clamp01(_dashCooldownLeft / Mathf.Max(0.01f, dashCooldown));
        _dashRest.fillAmount = fill;
        _dashRest.enabled = fill > 0.001f;
    }
}
