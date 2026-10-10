using UnityEngine;
using UnityEngine.UI;

public class WolfController : MonoBehaviour
{
    [Header("Settings")]
    public float moveSpeed = 150f;

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
    private CommandType _activeCmd;
    private float _ySortTimer = 0f;
    private const float YSortInterval = 0.1f;

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
    }

    public void OnBleatClicked()
    {
        _bleatTimer = 1.2f;
        if (bleatBubble) bleatBubble.SetActive(true);

        if (SoundLibrary.Instance != null && GameManager.Instance != null && GameManager.Instance.IsPlaying())
            SoundLibrary.Instance.PlaySFX("Sheep_Bleat");
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
        if (grazeFX) grazeFX.SetActive(false);
        if (bleatBubble) bleatBubble.SetActive(false);
        ResetHeadVisual();
        if (hudController) hudController.HideStatus();
    }

    void Update()
    {
        if (status == null || !status.alive || !gameObject.activeSelf) return;

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

        // Look / Heads Up hold
        bool keyLook = Input.GetKey(KeyCode.L) || Input.GetKey(KeyCode.Space);
        bool btnLookHeld = btnLook != null && btnLook.IsHeld;
        bool isLooking = keyLook || btnLookHeld;

        // Graze hold
        bool keyGraze = Input.GetKey(KeyCode.G);
        bool btnGrazeHeld = btnGraze != null && btnGraze.IsHeld;
        bool isGrazing = (keyGraze || btnGrazeHeld) && !isLooking;

        // Movement Input
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

        // Speed adjustment for Walk Slowly command
        float curSpeed = moveSpeed;
        if (_hasCommand && roundController && roundController.ActiveCommand == CommandType.WalkSlowly)
        {
            curSpeed = moveSpeed * 0.5f;
        }

        // While grazing or looking up, wolf cannot walk
        bool canMove = !isGrazing && !isLooking;
        Vector2 startPos = status.rect.anchoredPosition;
        if (canMove && moveInput.magnitude > 0.01f)
        {
            Vector2 newPos = startPos + moveInput * curSpeed * Time.deltaTime;

            if (flockBounds)
            {
                Vector2 half = flockBounds.sizeDelta * 0.5f;
                Vector2 center = flockBounds.anchoredPosition;
                newPos.x = Mathf.Clamp(newPos.x, center.x - half.x + 60f, center.x + half.x - 60f);
                newPos.y = Mathf.Clamp(newPos.y, center.y - half.y + 40f, center.y + half.y - 40f);
            }

            status.rect.anchoredPosition = newPos;

            // Flip facing
            if (Mathf.Abs(moveInput.x) > 0.1f)
            {
                bool right = moveInput.x > 0f;
                if (right != _facingRight)
                {
                    _facingRight = right;
                    if (visual) visual.localScale = new Vector3(_facingRight ? 1f : -1f, 1f, 1f);
                }
            }
        }

        // Head & Graze Visuals
        if (isGrazing)
        {
            if (grazeFX) grazeFX.SetActive(true);
            if (head) head.localEulerAngles = new Vector3(0f, 0f, -25f);
        }
        else if (isLooking)
        {
            if (grazeFX) grazeFX.SetActive(false);
            if (head) head.localEulerAngles = new Vector3(0f, 0f, 25f);
        }
        else
        {
            if (grazeFX) grazeFX.SetActive(false);
            if (head) head.localEulerAngles = Vector3.zero;
        }

        // Velocity & Tick
        Vector2 delta = status.rect.anchoredPosition - startPos;
        status.velocity = (Time.deltaTime > 0f) ? (delta / Time.deltaTime) : Vector2.zero;
        status.Tick(Time.deltaTime);

        // Command performance evaluation
        if (_hasCommand && roundController && roundController.CommandActive)
        {
            bool performing = IsPerforming(_activeCmd, isGrazing, isLooking, moveInput.magnitude, _bleatTimer > 0f);

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

        // Y-Sort
        _ySortTimer -= Time.deltaTime;
        if (_ySortTimer <= 0f)
        {
            _ySortTimer = YSortInterval;
            SortByY();
        }
    }

    private bool IsPerforming(CommandType cmd, bool isGrazing, bool isLooking, float moveMag, bool isBleating)
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

    private void ResetHeadVisual()
    {
        if (head && head.localEulerAngles != Vector3.zero)
            head.localEulerAngles = Vector3.zero;
    }

    private void SortByY()
    {
        Transform parent = transform.parent;
        if (parent == null) return;

        float myY = status.rect.anchoredPosition.y;
        int currentIdx = transform.GetSiblingIndex();
        int newIdx = 0;
        int count = parent.childCount;

        for (int i = 0; i < count; i++)
        {
            if (i == currentIdx) continue;
            RectTransform sib = parent.GetChild(i) as RectTransform;
            if (sib != null && sib.anchoredPosition.y > myY)
            {
                newIdx = i + 1;
            }
        }

        if (newIdx != currentIdx)
        {
            transform.SetSiblingIndex(newIdx);
        }
    }
}