using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum MatchKind { OneVsOne = 0, Friends = 1 }

/// <summary>
/// Photon Fusion rooms for one-versus-one and a friends flock.
/// The host runs the owner, the commands, and the shots. Players send their sheep or their aim.
/// </summary>
public class MatchSession : MonoBehaviour, INetworkRunnerCallbacks
{
    public static MatchSession Instance { get; private set; }
    public static bool Playing => Instance != null && Instance._phase == Phase.Playing;

    public bool IsAuthority => _runner != null && _runner.IsRunning && _runner.IsServer;
    public bool AcceptsFire => _phase == Phase.Playing && !_ending;
    public bool UsesHumanAim => _phase == Phase.Playing && _mode == MatchKind.OneVsOne;
    public bool LocalIsOwner => UsesHumanAim && LocalId == _ownerId;
    public MatchKind Mode => _mode;

    public SheepStatus LocalSheep
    {
        get
        {
            Seat seat = Find(LocalId);
            if (seat == null) return null;
            return SheepAt(seat.Sheep);
        }
    }

    public bool LocalOut
    {
        get
        {
            Seat seat = Find(LocalId);
            return seat != null && seat.Out;
        }
    }

    enum Phase { Menu, Connecting, Room, Playing, Board, Solo }

    class Seat
    {
        public int Id;
        public string Name = "Sheep";
        public int Sheep = -1;
        public bool Out;
        public int DeathSeq;
        public int DeathRound;
        public bool HasPose;
        public float X, Y, Mag;
        public byte Flags;
    }

    class Msg
    {
        public readonly byte[] B = new byte[640];
        public int N;
        public void Clear() { N = 0; }
        public void U8(int v) { B[N++] = (byte)v; }
        public void I16(int v)
        {
            short s = (short)Mathf.Clamp(v, short.MinValue, short.MaxValue);
            B[N++] = (byte)(s & 255);
            B[N++] = (byte)((s >> 8) & 255);
        }
        public void F32(float v)
        {
            byte[] raw = BitConverter.GetBytes(v);
            for (int i = 0; i < 4; i++) B[N++] = raw[i];
        }
        public void Str(string s)
        {
            if (string.IsNullOrEmpty(s)) { U8(0); return; }
            if (s.Length > 12) s = s.Substring(0, 12);
            byte[] raw = Encoding.UTF8.GetBytes(s);
            if (raw.Length > 36) { U8(0); return; }
            U8(raw.Length);
            for (int i = 0; i < raw.Length; i++) B[N++] = raw[i];
        }
        public byte[] Copy()
        {
            byte[] d = new byte[N];
            Array.Copy(B, d, N);
            return d;
        }
    }

    class Rd
    {
        public byte[] D;
        public int I;
        static readonly byte[] Four = new byte[4];
        public bool Ok(int n) { return D != null && I + n <= D.Length; }
        public int U8() { return D[I++]; }
        public int I16()
        {
            int v = D[I] | (D[I + 1] << 8);
            I += 2;
            if ((v & 0x8000) != 0) v -= 65536;
            return v;
        }
        public float F32()
        {
            Four[0] = D[I];
            Four[1] = D[I + 1];
            Four[2] = D[I + 2];
            Four[3] = D[I + 3];
            I += 4;
            return BitConverter.ToSingle(Four, 0);
        }
        public string Str()
        {
            int n = U8();
            if (n <= 0 || !Ok(n)) return "";
            string s = Encoding.UTF8.GetString(D, I, n);
            I += n;
            return s;
        }
    }

    const int None = -1;
    const string NameKey = "ImposterOnlineName";
    static readonly ReliableKey Key = ReliableKey.FromInts(1, 0, 0, 0);

    Phase _phase = Phase.Menu;
    MatchKind _mode = MatchKind.Friends;
    NetworkRunner _runner;
    bool _busy;
    bool _closing;
    bool _ending;
    int _token;
    int _ownerId = None;
    int _imposterId = None;
    int _winnerId = None;
    int _endRound;
    int _wrongShots;
    int _deathClock;
    string _roomCode = "";
    string _localName = "Sheep";

    readonly List<Seat> _seats = new List<Seat>();
    readonly Msg _msg = new Msg();

    GameManager _gm;
    RoundController _rounds;
    AimController _aim;
    HUDController _hud;
    WolfController _wolf;
    OwnerPatrol _owner;
    RectTransform _ownerRect;
    ShieldPickup _shield;
    SheepStatus[] _sheep;
    GameObject _controls;
    SheepPilot _pilot;
    bool _hooked;

    Vector2[] _homePoints;
    Vector2 _ownerHome;
    bool _homesCached;

    bool _noted;
    byte _noteFlags;
    float _noteMag;
    bool _shootEdge;
    bool _aimReady;
    Vector2 _aimPos;
    Vector2 _remoteAim;
    bool _remoteShoot;
    float _netTimer;
    float _snapTimer;
    bool _seenActive;
    CommandType _seenCmd;
    bool _appliedActive;
    CommandType _appliedCmd;

    Font _font;
    GameObject _root;
    GameObject _dim;
    GameObject _modes;
    GameObject _solo;
    GameObject _home;
    GameObject _duel;
    GameObject _friends;
    GameObject _room;
    GameObject _sheet;
    GameObject _grid;
    Text _duelStatus;
    Text _friendsStatus;
    Text _roomStatus;
    Text _roomCodeText;
    Text _roomList;
    Text _roomHint;
    Text _startLabel;
    Button _startButton;
    Button _swapButton;
    Text _roleText;
    Text _watchText;
    Button _fireButton;
    InputField _nameField;
    InputField _duelCode;
    InputField _friendsCode;

    bool _suppressStart;

    public static MatchSession Ensure()
    {
        if (Instance != null) return Instance;
        MatchSession found = FindObjectOfType<MatchSession>();
        if (found != null) return found;
        return new GameObject("MatchSession").AddComponent<MatchSession>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindObjectOfType<GameManager>() == null) return;
        if (Instance != null) return;
        if (FindObjectOfType<MatchSession>() != null) return;
        new GameObject("MatchSession").AddComponent<MatchSession>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        NetworkRunner[] leftover = FindObjectsOfType<NetworkRunner>();
        for (int i = 0; i < leftover.Length; i++)
        {
            if (leftover[i] != null)
                leftover[i].Shutdown();
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        _localName = PlayerPrefs.GetString(NameKey, "Sheep");
        BindScene();
        CacheHomes();
        if (_root == null) BuildUi();
        if (!_suppressStart)
            ShowStartPanel();
    }

    public void OpenModes()
    {
        _suppressStart = true;
        if (_root == null)
        {
            BindScene();
            BuildUi();
        }
        ShowModes();
    }

    public void RestartSolo()
    {
        BindScene();
        ResetWorld();
        _phase = Phase.Solo;
        if (_root != null) _root.SetActive(true);
        if (_dim != null) _dim.SetActive(false);
        HideLobby();
        if (_room != null) _room.SetActive(false);
        if (_sheet != null) _sheet.SetActive(false);
        RefreshBanners();
    }

    public void ReturnToStart()
    {
        ResetWorld();
        ShowStartPanel();
    }

    void Update()
    {
        if (_phase != Phase.Playing || _ending) return;

        if (LocalIsOwner && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.F)))
            _shootEdge = true;

        if (!IsAuthority || _rounds == null) return;

        bool changed = _rounds.CommandActive != _seenActive || (_rounds.CommandActive && _rounds.ActiveCommand != _seenCmd);
        if (changed)
        {
            _seenActive = _rounds.CommandActive;
            _seenCmd = _rounds.ActiveCommand;
            NotifyLocalDriver();
        }

        float dt = Time.deltaTime;
        int local = LocalId;
        for (int i = 0; i < _seats.Count; i++)
        {
            Seat seat = _seats[i];
            if (seat.Id == local || seat.Out || seat.Sheep < 0 || !seat.HasPose) continue;
            SheepStatus sheep = SheepAt(seat.Sheep);
            if (sheep == null || !sheep.alive || sheep.rect == null) continue;
            Vector2 prev = sheep.rect.anchoredPosition;
            ApplyPose(sheep, new Vector2(seat.X, seat.Y), seat.Flags);
            sheep.velocity = dt > 0f ? (sheep.rect.anchoredPosition - prev) / dt : Vector2.zero;
            bool grace = _rounds.CommandActive && _rounds.GraceOver;
            bool performing = _rounds.CommandActive && WolfController.IsPerforming(
                _rounds.ActiveCommand, (seat.Flags & 1) != 0, (seat.Flags & 2) != 0, seat.Mag, (seat.Flags & 4) != 0);
            sheep.isBad = grace && !performing;
            sheep.Tick(dt);
        }
    }

    void LateUpdate()
    {
        if (_phase != Phase.Playing || _runner == null || !_runner.IsRunning) return;
        if (!IsAuthority)
        {
            _netTimer -= Time.deltaTime;
            if (LocalIsOwner)
            {
                if (_shootEdge || _netTimer <= 0f)
                {
                    _netTimer = 0.05f;
                    SendOwnerInput();
                }
            }
            else if (_noted && LocalSheep != null && !LocalOut && _netTimer <= 0f)
            {
                _netTimer = 0.05f;
                SendSheepInput();
            }
            return;
        }

        _snapTimer -= Time.deltaTime;
        if (_snapTimer <= 0f)
        {
            _snapTimer = 0.07f;
            BroadcastSnapshot();
        }
    }

    public bool RunsLocalSheep(SheepStatus sheep)
    {
        if (!Playing) return true;
        if (LocalOut) return false;
        return sheep != null && sheep == LocalSheep;
    }

    public bool ShouldRunAI(SheepStatus sheep)
    {
        if (!Playing) return true;
        if (!IsAuthority) return false;
        return PlayerOf(sheep) < 0;
    }

    public void NoteSheep(bool graze, bool look, bool bleat, float mag, bool faceRight)
    {
        byte flags = 0;
        if (graze) flags |= 1;
        if (look) flags |= 2;
        if (bleat) flags |= 4;
        if (faceRight) flags |= 8;
        _noteFlags = flags;
        _noteMag = Mathf.Clamp01(mag);
        _noted = true;
    }

    public SheepStatus SheepNear(Vector2 pos, float radius)
    {
        SheepStatus best = null;
        float bestD = radius;
        for (int i = 0; i < _seats.Count; i++)
        {
            Seat seat = _seats[i];
            if (seat.Out || seat.Sheep < 0) continue;
            SheepStatus sheep = SheepAt(seat.Sheep);
            if (sheep == null || !sheep.alive || sheep.rect == null) continue;
            float d = Vector2.Distance(pos, sheep.rect.anchoredPosition);
            if (d > bestD) continue;
            bestD = d;
            best = sheep;
        }
        return best;
    }

    public void PrepareHit(SheepStatus target)
    {
        if (!IsAuthority || target == null) return;
        if (_mode == MatchKind.Friends && PlayerOf(target) >= 0)
            target.diesForGood = true;
        if (_mode == MatchKind.OneVsOne && target.isWolf)
            target.diesForGood = true;
    }

    public void AuthorityHit(SheepStatus target)
    {
        if (!IsAuthority || target == null || _ending) return;
        SendShot(target);

        if (_mode == MatchKind.OneVsOne)
        {
            if (target.isWolf)
            {
                MarkOut(Find(_imposterId));
                BeginFinish(_ownerId);
            }
            else
            {
                if (SoundLibrary.Instance != null)
                    SoundLibrary.Instance.PlaySFX("Wrong_Kill");
                _wrongShots++;
                RefreshBanners();
                if (_wrongShots >= 3)
                {
                    MarkOut(Find(_ownerId));
                    BeginFinish(_imposterId);
                }
            }
            return;
        }

        int id = PlayerOf(target);
        if (id >= 0)
        {
            MarkOut(Find(id));
            if (AliveCount() <= 1)
                BeginFinish(FirstAlive());
        }
        else if (SoundLibrary.Instance != null)
        {
            SoundLibrary.Instance.PlaySFX("Wrong_Kill");
        }
    }

    public void TickHumanAim(AimController aim)
    {
        if (aim == null) return;
        Vector2 pos = LocalIsOwner ? StepAim(aim) : _remoteAim;
        aim.PlaceAim(pos);
        MoveOwnerX(pos.x);
        if (LocalIsOwner && _shootEdge)
        {
            _shootEdge = false;
            aim.FireAtPoint(pos);
        }
        else if (!LocalIsOwner && _remoteShoot)
        {
            _remoteShoot = false;
            aim.FireAtPoint(pos);
        }
    }

    public void TickLocalOwnerAim(AimController aim)
    {
        if (aim == null) return;
        Vector2 pos = StepAim(aim);
        aim.PlaceAim(pos);
        MoveOwnerX(pos.x);
    }

    int LocalId => _runner != null ? _runner.LocalPlayer.PlayerId : None;

    void BindScene()
    {
        if (_gm == null) _gm = GameManager.Instance != null ? GameManager.Instance : FindObjectOfType<GameManager>();
        if (_gm != null)
        {
            if (_rounds == null) _rounds = _gm.roundController;
            if (_aim == null) _aim = _gm.aimController;
            if (_hud == null) _hud = _gm.hudController;
            if (_sheep == null || _sheep.Length == 0) _sheep = _gm.allSheep;
        }
        if (_aim == null) _aim = FindObjectOfType<AimController>();
        if (_wolf == null) _wolf = FindObjectOfType<WolfController>();
        if ((_sheep == null || _sheep.Length == 0) && _aim != null) _sheep = _aim.allSheep;
        if (_owner == null) _owner = FindObjectOfType<OwnerPatrol>();
        if (_owner != null && _ownerRect == null) _ownerRect = _owner.GetComponent<RectTransform>();
        if (_shield == null && _wolf != null) _shield = _wolf.GetComponent<ShieldPickup>();
        if (_controls == null)
        {
            GameObject controls = GameObject.Find("Controls");
            if (controls != null) _controls = controls;
        }
        if (_wolf != null && _wolf.status != null && WolfIndex() < 0)
        {
            var list = new List<SheepStatus>();
            if (_sheep != null) list.AddRange(_sheep);
            list.Add(_wolf.status);
            _sheep = list.ToArray();
        }
    }

    int WolfIndex()
    {
        if (_sheep == null) return -1;
        for (int i = 0; i < _sheep.Length; i++)
        {
            if (_sheep[i] != null && _sheep[i].isWolf) return i;
        }
        return -1;
    }

    SheepStatus SheepAt(int index)
    {
        if (_sheep == null || index < 0 || index >= _sheep.Length) return null;
        return _sheep[index];
    }

    int IndexOf(SheepStatus sheep)
    {
        if (_sheep == null || sheep == null) return -1;
        for (int i = 0; i < _sheep.Length; i++)
        {
            if (_sheep[i] == sheep) return i;
        }
        return -1;
    }

    int PlayerOf(SheepStatus sheep)
    {
        int index = IndexOf(sheep);
        if (index < 0) return None;
        for (int i = 0; i < _seats.Count; i++)
        {
            if (_seats[i].Sheep == index) return _seats[i].Id;
        }
        return None;
    }

    Seat Find(int id)
    {
        for (int i = 0; i < _seats.Count; i++)
        {
            if (_seats[i].Id == id) return _seats[i];
        }
        return null;
    }

    Seat EnsureSeat(int id, string name)
    {
        Seat seat = Find(id);
        if (seat == null)
        {
            seat = new Seat { Id = id, Name = string.IsNullOrEmpty(name) ? "Sheep" : name };
            _seats.Add(seat);
        }
        else if (!string.IsNullOrEmpty(name))
        {
            seat.Name = name;
        }

        if (IsAuthority && _mode == MatchKind.OneVsOne && _phase != Phase.Playing && _phase != Phase.Board)
        {
            if (_ownerId < 0) _ownerId = id;
            if (id != _ownerId && _imposterId < 0) _imposterId = id;
        }
        return seat;
    }

    void CacheHomes()
    {
        if (_homesCached || _sheep == null) return;
        _homePoints = new Vector2[_sheep.Length];
        for (int i = 0; i < _sheep.Length; i++)
        {
            if (_sheep[i] != null && _sheep[i].rect != null)
                _homePoints[i] = _sheep[i].rect.anchoredPosition;
        }
        if (_ownerRect != null) _ownerHome = _ownerRect.anchoredPosition;
        _homesCached = true;
    }

    void CreateRoom(MatchKind mode)
    {
        _mode = mode;
        _roomCode = RandomCode();
        RememberName();
        StartRunner(GameMode.Host, SessionOf(mode, _roomCode));
    }

    void JoinRoom(MatchKind mode)
    {
        InputField field = mode == MatchKind.Friends ? _friendsCode : _duelCode;
        string code = field != null ? field.text : "";
        code = code.Trim().ToUpperInvariant();
        if (code.Length < 4)
        {
            SetStatus("Enter the 4 letter code.");
            return;
        }
        _mode = mode;
        _roomCode = code.Substring(0, 4);
        RememberName();
        StartRunner(GameMode.Client, SessionOf(mode, _roomCode));
    }

    async void StartRunner(GameMode gameMode, string session)
    {
        if (_busy) return;
        _busy = true;
        int token = ++_token;
        SetStatus("Connecting...");
        _phase = Phase.Connecting;
        ShowModePanel();
        try
        {
            ShutdownRunner();
            GameObject go = new GameObject("ImposterRunner");
            DontDestroyOnLoad(go);
            _runner = go.AddComponent<NetworkRunner>();
            _runner.ProvideInput = false;
            go.AddComponent<NetworkSceneManagerDefault>();
            _runner.AddCallbacks(this);
            await _runner.StartGame(new StartGameArgs
            {
                GameMode = gameMode,
                SessionName = session,
                PlayerCount = _mode == MatchKind.OneVsOne ? 2 : 8,
                IsVisible = false,
                IsOpen = true
            });
            if (token != _token || this == null) return;
            if (_runner == null || !_runner.IsRunning)
            {
                SetStatus("Could not join that room.");
                _phase = Phase.Menu;
                ShowModePanel();
                _busy = false;
                return;
            }

            _phase = Phase.Room;
            _ownerId = IsAuthority ? LocalId : None;
            _imposterId = None;
            EnsureSeat(LocalId, _localName);
            SendHello();
            if (IsAuthority) BroadcastRoster();
            ShowRoom();
            SetStatus("");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Match] " + e.Message);
            if (token == _token && this != null)
            {
                SetStatus(NiceError(e));
                _phase = Phase.Menu;
                ShowModePanel();
                ShutdownRunner();
            }
        }
        _busy = false;
    }

    void ShutdownRunner()
    {
        if (_runner == null) return;
        _closing = true;
        NetworkRunner runner = _runner;
        _runner = null;
        runner.RemoveCallbacks(this);
        runner.Shutdown();
    }

    void LeaveRoom()
    {
        _token++;
        _busy = false;
        if (_phase == Phase.Playing || _phase == Phase.Board)
            ResetWorld();
        if (_gm != null && _gm.state != GameState.Start)
            _gm.SetState(GameState.Start);
        _phase = Phase.Menu;
        _seats.Clear();
        _ownerId = None;
        _imposterId = None;
        ShutdownRunner();
        ShowModePanel();
        SetStatus("");
    }

    void PlayAlone()
    {
        _phase = Phase.Solo;
        if (_root != null) _root.SetActive(true);
        if (_dim != null) _dim.SetActive(false);
        HideLobby();
        if (_room != null) _room.SetActive(false);
        if (_sheet != null) _sheet.SetActive(false);
        if (_gm != null)
            _gm.OnPlayClicked();
    }

    void HostStart()
    {
        if (!IsAuthority || _phase != Phase.Room) return;
        if (_mode == MatchKind.OneVsOne && _seats.Count != 2) return;
        if (_mode == MatchKind.Friends && _seats.Count < 2) return;
        AssignSeats();
        Send(BuildStart());
        BeginMatch();
    }

    void SwapRoles()
    {
        if (!IsAuthority || _mode != MatchKind.OneVsOne || _phase != Phase.Room) return;
        if (_ownerId < 0 || _imposterId < 0) return;
        int swap = _ownerId;
        _ownerId = _imposterId;
        _imposterId = swap;
        BroadcastRoster();
    }

    void AssignSeats()
    {
        BindScene();
        for (int i = 0; i < _seats.Count; i++)
            _seats[i].Sheep = -1;

        if (_mode == MatchKind.OneVsOne)
        {
            if (_ownerId < 0 && _seats.Count > 0) _ownerId = _seats[0].Id;
            if (_imposterId < 0)
            {
                for (int i = 0; i < _seats.Count; i++)
                {
                    if (_seats[i].Id != _ownerId)
                    {
                        _imposterId = _seats[i].Id;
                        break;
                    }
                }
            }
            Seat imposter = Find(_imposterId);
            if (imposter != null) imposter.Sheep = WolfIndex();
            return;
        }

        int sheepCursor = 0;
        for (int i = 0; i < _seats.Count && _sheep != null && sheepCursor < _sheep.Length; i++)
        {
            _seats[i].Sheep = sheepCursor;
            sheepCursor++;
        }
    }

    void BeginMatch()
    {
        BindScene();
        CacheHomes();
        _phase = Phase.Playing;
        _ending = false;
        _wrongShots = 0;
        _deathClock = 0;
        _winnerId = None;
        _noted = false;
        _shootEdge = false;
        _remoteShoot = false;
        _seenActive = false;
        _appliedActive = false;
        _netTimer = 0f;
        _snapTimer = 0f;
        if (_aim != null && _aim.aimRect != null)
            _remoteAim = _aimPos = _aim.aimRect.anchoredPosition;

        for (int i = 0; i < _seats.Count; i++)
        {
            _seats[i].Out = false;
            _seats[i].DeathSeq = 0;
            _seats[i].DeathRound = 0;
            _seats[i].HasPose = false;
        }
        SeedPoses();
        SetupLocalSheep();
        ShowTags();
        if (_dim != null) _dim.SetActive(false);
        HideLobby();
        if (_room != null) _room.SetActive(false);
        if (_sheet != null) _sheet.SetActive(false);
        RefreshControls();
        RefreshBanners();
        if (LocalIsOwner && _hud != null)
            _hud.UpdateSuspicion(0f);
        if (_gm != null)
            _gm.OnPlayClicked();
    }

    void SeedPoses()
    {
        for (int i = 0; i < _seats.Count; i++)
        {
            SheepStatus sheep = SheepAt(_seats[i].Sheep);
            if (sheep == null || sheep.rect == null) continue;
            _seats[i].X = sheep.rect.anchoredPosition.x;
            _seats[i].Y = sheep.rect.anchoredPosition.y;
            _seats[i].HasPose = true;
            _seats[i].Flags = 8;
        }
    }

    void SetupLocalSheep()
    {
        UnhookPilot();
        SheepPilot[] old = FindObjectsOfType<SheepPilot>();
        for (int i = 0; i < old.Length; i++)
        {
            if (old[i] != null) Destroy(old[i]);
        }
        _pilot = null;

        SheepStatus mine = LocalSheep;
        if (mine == null || LocalIsOwner) return;
        if (mine.GetComponent<WolfController>() != null) return;

        SheepPilot pilot = mine.gameObject.AddComponent<SheepPilot>();
        if (_wolf != null)
        {
            pilot.flockBounds = _wolf.flockBounds;
            pilot.joystick = _wolf.joystick;
            pilot.btnGraze = _wolf.btnGraze;
            pilot.btnLook = _wolf.btnLook;
            pilot.roundController = _wolf.roundController;
            pilot.hudController = _wolf.hudController;
            pilot.BindDash(_wolf.btnDash);
        }
        _pilot = pilot;
        HookPilot(pilot);
    }

    void HookPilot(SheepPilot pilot)
    {
        if (_hooked || _wolf == null || pilot == null) return;
        if (_wolf.btnBleat != null)
        {
            _wolf.btnBleat.onClick.RemoveListener(_wolf.OnBleatClicked);
            _wolf.btnBleat.onClick.AddListener(pilot.OnBleatClicked);
        }
        if (_wolf.btnDash != null)
        {
            _wolf.btnDash.onClick.RemoveListener(_wolf.OnDashClicked);
            _wolf.btnDash.onClick.AddListener(pilot.OnDashClicked);
        }
        _hooked = true;
    }

    void UnhookPilot()
    {
        if (!_hooked || _wolf == null) return;
        if (_pilot != null)
        {
            if (_wolf.btnBleat != null)
            {
                _wolf.btnBleat.onClick.RemoveListener(_pilot.OnBleatClicked);
                _wolf.btnBleat.onClick.AddListener(_wolf.OnBleatClicked);
            }
            if (_wolf.btnDash != null)
            {
                _wolf.btnDash.onClick.RemoveListener(_pilot.OnDashClicked);
                _wolf.btnDash.onClick.AddListener(_wolf.OnDashClicked);
            }
        }
        _hooked = false;
    }

    void NotifyLocalDriver()
    {
        SheepStatus mine = LocalSheep;
        if (mine == null || LocalOut) return;
        WolfController wolf = mine.GetComponent<WolfController>();
        SheepPilot pilot = mine.GetComponent<SheepPilot>();
        if (_rounds != null && _rounds.CommandActive)
        {
            if (wolf != null) wolf.BeginCommand(_rounds.ActiveCommand);
            if (pilot != null) pilot.BeginCommand(_rounds.ActiveCommand);
        }
        else
        {
            if (wolf != null) wolf.EndCommand();
            if (pilot != null) pilot.EndCommand();
        }
    }

    public void RefreshControls()
    {
        bool inRound = _gm != null && (_gm.state == GameState.Playing || _gm.state == GameState.Paused);
        bool sheepPlay = inRound && (_phase == Phase.Solo || (_phase == Phase.Playing && LocalSheep != null && !LocalOut && !LocalIsOwner));
        bool ownerPlay = inRound && _phase == Phase.Playing && LocalIsOwner && !LocalOut;
        if (_controls != null)
            _controls.SetActive(sheepPlay);
        if (_fireButton != null)
            _fireButton.gameObject.SetActive(ownerPlay);
    }

    void RefreshBanners()
    {
        if (_roleText == null) return;
        if (_phase == Phase.Board || _phase == Phase.Menu || _phase == Phase.Room || _phase == Phase.Solo)
        {
            _roleText.text = "";
            if (_watchText != null) _watchText.text = "";
            return;
        }
        if (_watchText != null)
            _watchText.text = LocalOut ? "YOU ARE OUT  -  watching until the game ends" : "";

        if (_mode == MatchKind.OneVsOne && LocalIsOwner)
            _roleText.text = "YOU ARE THE OWNER  -  misses " + _wrongShots + "/3";
        else if (_mode == MatchKind.OneVsOne)
            _roleText.text = "YOU ARE THE IMPOSTER";
        else if (LocalSheep != null && !LocalOut)
            _roleText.text = "YOU ARE A SHEEP  -  last one standing wins";
        else
            _roleText.text = "";
    }

    Vector2 StepAim(AimController aim)
    {
        Vector2 pos = aim.aimRect != null ? aim.aimRect.anchoredPosition : _aimPos;
        Vector2 move = ReadStick();
        if (move.sqrMagnitude > 0.0001f)
            pos += move * 720f * Time.deltaTime;
        pos = ClampField(pos);
        _aimPos = pos;
        _aimReady = true;
        return pos;
    }

    Vector2 ReadStick()
    {
        float kx = 0f, ky = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) ky += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) ky -= 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) kx -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) kx += 1f;
        Vector2 kb = new Vector2(kx, ky);
        if (kb.magnitude > 1f) kb.Normalize();
        Vector2 joy = (_wolf != null && _wolf.joystick != null) ? _wolf.joystick.Output : Vector2.zero;
        Vector2 move = kb.magnitude > 0.1f ? kb : joy;
        if (move.magnitude > 1f) move.Normalize();
        return move;
    }

    Vector2 ClampField(Vector2 pos)
    {
        RectTransform flock = _wolf != null ? _wolf.flockBounds : null;
        if (flock == null) return pos;
        Vector2 half = flock.sizeDelta * 0.5f;
        Vector2 center = flock.anchoredPosition;
        pos.x = Mathf.Clamp(pos.x, center.x - half.x + 40f, center.x + half.x - 40f);
        pos.y = Mathf.Clamp(pos.y, center.y - half.y + 30f, center.y + half.y - 30f);
        return pos;
    }

    void MoveOwnerX(float x)
    {
        if (_ownerRect == null || _owner == null) return;
        float clamped = Mathf.Clamp(x, _owner.minX, _owner.maxX);
        Vector2 p = _ownerRect.anchoredPosition;
        p.x = clamped;
        p.y = _owner.yPosition;
        _ownerRect.anchoredPosition = p;
    }

    void MarkOut(Seat seat)
    {
        if (seat == null || seat.Out) return;
        seat.Out = true;
        _deathClock++;
        seat.DeathSeq = _deathClock;
        seat.DeathRound = _rounds != null ? _rounds.Round : 0;
        if (seat.Id == LocalId)
            OnLocalOut();
    }

    void OnLocalOut()
    {
        RefreshControls();
        RefreshBanners();
        if (_hud != null) _hud.HideStatus();
    }

    int AliveCount()
    {
        int n = 0;
        for (int i = 0; i < _seats.Count; i++)
        {
            if (!_seats[i].Out) n++;
        }
        return n;
    }

    int FirstAlive()
    {
        int bestId = None;
        int bestSeq = -1;
        for (int i = 0; i < _seats.Count; i++)
        {
            if (!_seats[i].Out) return _seats[i].Id;
            if (_seats[i].DeathSeq >= bestSeq)
            {
                bestSeq = _seats[i].DeathSeq;
                bestId = _seats[i].Id;
            }
        }
        return bestId;
    }

    void BeginFinish(int winnerId)
    {
        if (_ending) return;
        _ending = true;
        _winnerId = winnerId;
        StartCoroutine(FinishSoon());
    }

    IEnumerator FinishSoon()
    {
        yield return new WaitForSeconds(2f);
        if (this == null || _phase != Phase.Playing) yield break;
        _endRound = _rounds != null ? _rounds.Round : 0;
        Send(BuildEnd());
        EnterBoard(_winnerId);
    }

    void EnterBoard(int winnerId)
    {
        if (_phase == Phase.Board) return;
        _winnerId = winnerId;
        _phase = Phase.Board;
        _ending = true;
        if (_endRound <= 0 && _rounds != null) _endRound = _rounds.Round;
        RefreshControls();
        RefreshBanners();
        if (_dim != null) _dim.SetActive(true);
        if (_sheet != null) _sheet.SetActive(true);
        BuildRows();
        if (SoundLibrary.Instance != null)
        {
            SoundLibrary.Instance.StopMusic();
            SoundLibrary.Instance.StopAllSFX();
            SoundLibrary.Instance.PlaySFX(LocalId == _winnerId ? "Correct_Command" : "Defeat_Jingle");
        }
    }

    void RequestReturn()
    {
        if (_phase != Phase.Board && _phase != Phase.Playing) return;
        if (IsAuthority)
            DoReturn();
        else
        {
            _msg.Clear();
            _msg.U8(7);
            Send(_msg.Copy());
        }
    }

    void DoReturn()
    {
        _msg.Clear();
        _msg.U8(7);
        Send(_msg.Copy());
        ReturnLocal();
    }

    void ReturnLocal()
    {
        if (_phase == Phase.Room || _phase == Phase.Menu) return;
        ResetWorld();
        _phase = Phase.Room;
        _ending = false;
        _wrongShots = 0;
        if (_gm != null)
            _gm.SetState(GameState.Start);
        if (SoundLibrary.Instance != null)
            SoundLibrary.Instance.PlayMusic("Main_BGM");
        ShowRoom();
        if (IsAuthority) BroadcastRoster();
    }

    void ResetWorld()
    {
        UnhookPilot();
        SheepPilot[] pilots = FindObjectsOfType<SheepPilot>();
        for (int i = 0; i < pilots.Length; i++)
        {
            if (pilots[i] != null) Destroy(pilots[i]);
        }
        _pilot = null;
        HideTags();
        if (_sheep != null)
        {
            for (int i = 0; i < _sheep.Length; i++)
            {
                SheepStatus sheep = _sheep[i];
                if (sheep == null) continue;
                SheepAI ai = sheep.GetComponent<SheepAI>();
                if (ai != null) ai.EndCommand();
                WolfController wolf = sheep.GetComponent<WolfController>();
                if (wolf != null) wolf.EndCommand();
                sheep.ResetStatus();
                if (_homePoints != null && i < _homePoints.Length && sheep.rect != null)
                    sheep.rect.anchoredPosition = _homePoints[i];
                Transform bad = sheep.transform.Find("BadMark");
                if (bad != null) bad.gameObject.SetActive(false);
                Transform bleat = sheep.transform.Find("BleatBubble");
                if (bleat != null) bleat.gameObject.SetActive(false);
            }
        }
        if (_ownerRect != null && _homesCached)
            _ownerRect.anchoredPosition = _ownerHome;
        for (int i = 0; i < _seats.Count; i++)
        {
            _seats[i].Out = false;
            _seats[i].DeathSeq = 0;
            _seats[i].DeathRound = 0;
            _seats[i].Sheep = -1;
            _seats[i].HasPose = false;
        }
        RefreshControls();
    }

    void ApplyPose(SheepStatus sheep, Vector2 pos, byte flags)
    {
        if (sheep.rect != null) sheep.rect.anchoredPosition = pos;
        Transform visual = sheep.transform.Find("Visual");
        if (visual != null)
        {
            float sx = (flags & 8) != 0 ? 1f : -1f;
            visual.localScale = new Vector3(sx, 1f, 1f);
            Transform graze = visual.Find("GrazeFX");
            if (graze != null) graze.gameObject.SetActive((flags & 1) != 0);
            Transform head = visual.Find("Head");
            if (head != null)
            {
                float z = (flags & 1) != 0 ? -25f : ((flags & 2) != 0 ? 25f : 0f);
                head.localEulerAngles = new Vector3(0f, 0f, z);
            }
        }
        Transform bleat = sheep.transform.Find("BleatBubble");
        if (bleat != null) bleat.gameObject.SetActive((flags & 4) != 0);
    }

    byte ReadPose(SheepStatus sheep)
    {
        int index = IndexOf(sheep);
        for (int i = 0; i < _seats.Count; i++)
        {
            if (_seats[i].Sheep == index && _seats[i].HasPose && _seats[i].Id != LocalId)
                return _seats[i].Flags;
        }
        byte flags = 8;
        if (sheep == null) return flags;
        Transform visual = sheep.transform.Find("Visual");
        if (visual != null)
        {
            flags = visual.localScale.x < 0f ? (byte)0 : (byte)8;
            Transform graze = visual.Find("GrazeFX");
            if (graze != null && graze.gameObject.activeSelf) flags |= 1;
            Transform head = visual.Find("Head");
            if (head != null)
            {
                float z = head.localEulerAngles.z;
                if (z > 180f) z -= 360f;
                if (z > 10f) flags |= 2;
                if (z < -10f) flags |= 1;
            }
        }
        Transform bleat = sheep.transform.Find("BleatBubble");
        if (bleat != null && bleat.gameObject.activeSelf) flags |= 4;
        return flags;
    }

    void SendHello()
    {
        _msg.Clear();
        _msg.U8(1);
        _msg.Str(_localName);
        Send(_msg.Copy());
    }

    void BroadcastRoster()
    {
        if (!IsAuthority) return;
        _msg.Clear();
        _msg.U8(2);
        _msg.U8((int)_mode);
        _msg.I16(_ownerId);
        _msg.I16(_imposterId);
        _msg.U8(_seats.Count);
        for (int i = 0; i < _seats.Count; i++)
        {
            Seat seat = _seats[i];
            _msg.I16(seat.Id);
            _msg.I16(seat.Sheep);
            _msg.U8(seat.Out ? 1 : 0);
            _msg.Str(seat.Name);
        }
        Send(_msg.Copy());
        RefreshRoom();
    }

    byte[] BuildStart()
    {
        _msg.Clear();
        _msg.U8(9);
        _msg.U8((int)_mode);
        _msg.I16(_ownerId);
        _msg.I16(_imposterId);
        _msg.U8(_seats.Count);
        for (int i = 0; i < _seats.Count; i++)
        {
            _msg.I16(_seats[i].Id);
            _msg.I16(_seats[i].Sheep);
        }
        return _msg.Copy();
    }

    void SendOwnerInput()
    {
        if (!_aimReady && _aim != null && _aim.aimRect != null)
            _aimPos = _aim.aimRect.anchoredPosition;
        _msg.Clear();
        _msg.U8(3);
        _msg.U8(1);
        _msg.F32(_aimPos.x);
        _msg.F32(_aimPos.y);
        _msg.U8(_shootEdge ? 1 : 0);
        _shootEdge = false;
        Send(_msg.Copy());
    }

    void SendSheepInput()
    {
        SheepStatus mine = LocalSheep;
        if (mine == null || mine.rect == null) return;
        _msg.Clear();
        _msg.U8(3);
        _msg.U8(0);
        _msg.F32(mine.rect.anchoredPosition.x);
        _msg.F32(mine.rect.anchoredPosition.y);
        _msg.U8(_noteFlags);
        _msg.U8(Mathf.RoundToInt(_noteMag * 255f));
        Send(_msg.Copy());
    }

    void BroadcastSnapshot()
    {
        if (_sheep == null) return;
        _msg.Clear();
        _msg.U8(4);
        _msg.I16(_rounds != null ? _rounds.Round : 0);
        _msg.U8(_rounds != null ? (int)_rounds.ActiveCommand : 0);
        int flags = 0;
        if (_rounds != null && _rounds.CommandActive) flags |= 1;
        if (_rounds != null && _rounds.GraceOver) flags |= 2;
        _msg.U8(flags);
        _msg.F32(_rounds != null ? _rounds.TimerRemaining : 0f);
        _msg.F32(_rounds != null ? _rounds.WindowTime : 1f);
        Vector2 aim = (_aim != null && _aim.aimRect != null) ? _aim.aimRect.anchoredPosition : Vector2.zero;
        _msg.I16(Mathf.RoundToInt(aim.x));
        _msg.I16(Mathf.RoundToInt(aim.y));
        _msg.I16(_ownerRect != null ? Mathf.RoundToInt(_ownerRect.anchoredPosition.x) : 0);
        float lock01 = (_aim != null && _aim.aimLockFill != null) ? _aim.aimLockFill.fillAmount : 0f;
        _msg.U8(Mathf.RoundToInt(Mathf.Clamp01(lock01) * 100f));
        _msg.U8(_wrongShots);
        bool shieldOn = _shield != null && _shield.NetVisible;
        _msg.U8(shieldOn ? 1 : 0);
        _msg.I16(shieldOn ? Mathf.RoundToInt(_shield.NetPos.x) : 0);
        _msg.I16(shieldOn ? Mathf.RoundToInt(_shield.NetPos.y) : 0);
        _msg.U8(_sheep.Length);
        for (int i = 0; i < _sheep.Length; i++)
        {
            SheepStatus sheep = _sheep[i];
            Vector2 pos = (sheep != null && sheep.rect != null) ? sheep.rect.anchoredPosition : Vector2.zero;
            _msg.I16(Mathf.RoundToInt(pos.x));
            _msg.I16(Mathf.RoundToInt(pos.y));
            _msg.U8(sheep != null ? Mathf.Clamp(Mathf.RoundToInt(sheep.suspicion), 0, 100) : 0);
            int state = 0;
            if (sheep != null && sheep.alive) state |= 1;
            if (sheep != null && sheep.isBad) state |= 2;
            if (sheep != null && sheep.diesForGood) state |= 4;
            _msg.U8(state);
            _msg.U8(sheep != null ? ReadPose(sheep) : 8);
        }
        Send(_msg.Copy());
    }

    void SendShot(SheepStatus target)
    {
        int index = IndexOf(target);
        if (index < 0) return;
        _msg.Clear();
        _msg.U8(5);
        _msg.U8(index);
        _msg.U8(target.diesForGood ? 1 : 0);
        Send(_msg.Copy());
    }

    byte[] BuildEnd()
    {
        _msg.Clear();
        _msg.U8(6);
        _msg.I16(_winnerId);
        _msg.I16(_endRound);
        _msg.U8(_seats.Count);
        for (int i = 0; i < _seats.Count; i++)
        {
            Seat seat = _seats[i];
            _msg.I16(seat.Id);
            _msg.I16(seat.Sheep);
            _msg.U8(seat.Out ? 1 : 0);
            _msg.U8(seat.DeathSeq);
            _msg.I16(seat.DeathRound);
        }
        return _msg.Copy();
    }

    void Send(byte[] data)
    {
        if (_runner == null || !_runner.IsRunning || data == null) return;
        if (_runner.IsServer)
        {
            foreach (PlayerRef player in _runner.ActivePlayers)
            {
                if (player == _runner.LocalPlayer) continue;
                _runner.SendReliableDataToPlayer(player, Key, data);
            }
        }
        else
        {
            _runner.SendReliableDataToServer(Key, data);
        }
    }

    void OnPacket(PlayerRef from, byte[] data)
    {
        if (data == null || data.Length == 0) return;
        Rd rd = new Rd { D = data, I = 0 };
        if (!rd.Ok(1)) return;
        int type = rd.U8();
        if (type == 1 && rd.Ok(1))
        {
            EnsureSeat(from.PlayerId, rd.Str());
            if (IsAuthority) BroadcastRoster();
            else RefreshRoom();
        }
        else if (type == 2)
        {
            ReadRoster(rd);
        }
        else if (type == 3 && IsAuthority)
        {
            ReadInput(from, rd);
        }
        else if (type == 4 && !IsAuthority && _phase == Phase.Playing)
        {
            ReadSnapshot(rd);
        }
        else if (type == 5 && !IsAuthority)
        {
            if (!rd.Ok(2)) return;
            SeeShot(rd.U8(), rd.U8() != 0);
        }
        else if (type == 6)
        {
            ReadEnd(rd);
            EnterBoard(_winnerId);
        }
        else if (type == 7)
        {
            if (IsAuthority) DoReturn();
            else ReturnLocal();
        }
        else if (type == 9 && _phase != Phase.Playing && _phase != Phase.Board)
        {
            ReadStart(rd);
            BeginMatch();
        }
    }

    void ReadRoster(Rd rd)
    {
        if (!rd.Ok(6)) return;
        _mode = (MatchKind)rd.U8();
        _ownerId = rd.I16();
        _imposterId = rd.I16();
        int count = rd.U8();
        var keep = new List<int>();
        for (int i = 0; i < count; i++)
        {
            if (!rd.Ok(5)) break;
            int id = rd.I16();
            int sheep = rd.I16();
            bool outsider = rd.U8() != 0;
            string name = rd.Str();
            Seat seat = EnsureSeat(id, name);
            if (_phase != Phase.Playing) seat.Sheep = sheep;
            seat.Out = outsider;
            keep.Add(id);
        }
        if (!IsAuthority)
        {
            for (int i = _seats.Count - 1; i >= 0; i--)
            {
                if (!keep.Contains(_seats[i].Id))
                    _seats.RemoveAt(i);
            }
        }
        RefreshRoom();
    }

    void ReadStart(Rd rd)
    {
        if (!rd.Ok(6)) return;
        _mode = (MatchKind)rd.U8();
        _ownerId = rd.I16();
        _imposterId = rd.I16();
        int count = rd.U8();
        for (int i = 0; i < count; i++)
        {
            if (!rd.Ok(4)) break;
            int id = rd.I16();
            int sheep = rd.I16();
            Seat seat = EnsureSeat(id, null);
            seat.Sheep = sheep;
        }
    }

    void ReadInput(PlayerRef from, Rd rd)
    {
        if (!rd.Ok(1)) return;
        int kind = rd.U8();
        if (kind == 1)
        {
            if (!rd.Ok(9)) return;
            _remoteAim = new Vector2(rd.F32(), rd.F32());
            if (rd.U8() != 0) _remoteShoot = true;
            return;
        }
        if (!rd.Ok(10)) return;
        float x = rd.F32();
        float y = rd.F32();
        byte flags = (byte)rd.U8();
        float mag = rd.U8() / 255f;
        Seat seat = Find(from.PlayerId);
        if (seat == null) return;
        seat.X = x;
        seat.Y = y;
        seat.Flags = flags;
        seat.Mag = mag;
        seat.HasPose = true;
    }

    void ReadSnapshot(Rd rd)
    {
        if (_sheep == null || !rd.Ok(26)) return;
        int round = rd.I16();
        int cmd = rd.U8();
        int bits = rd.U8();
        float remaining = rd.F32();
        float window = rd.F32();
        int aimX = rd.I16();
        int aimY = rd.I16();
        int ownerX = rd.I16();
        int lockByte = rd.U8();
        _wrongShots = rd.U8();
        bool shieldOn = rd.U8() != 0;
        int shieldX = rd.I16();
        int shieldY = rd.I16();
        int count = rd.U8();

        bool wasActive = _appliedActive;
        CommandType wasCmd = _appliedCmd;
        if (_rounds != null)
            _rounds.ApplyRemote(round, cmd, (bits & 1) != 0, (bits & 2) != 0, remaining, window);
        _appliedActive = _rounds != null && _rounds.CommandActive;
        _appliedCmd = _rounds != null ? _rounds.ActiveCommand : 0;
        if (wasActive != _appliedActive || (_appliedActive && wasCmd != _appliedCmd))
            NotifyLocalDriver();

        if (_aim != null && !(UsesHumanAim && LocalIsOwner))
            _aim.PlaceAim(new Vector2(aimX, aimY));
        if (_aim != null)
            _aim.ApplyRemoteLock(lockByte / 100f);
        if (_ownerRect != null && !(UsesHumanAim && LocalIsOwner))
        {
            Vector2 p = _ownerRect.anchoredPosition;
            p.x = ownerX;
            if (_owner != null) p.y = _owner.yPosition;
            _ownerRect.anchoredPosition = p;
        }
        if (_shield != null)
            _shield.ApplyRemoteShield(shieldOn, new Vector2(shieldX, shieldY));

        int n = Mathf.Min(count, _sheep.Length);
        for (int i = 0; i < n; i++)
        {
            if (!rd.Ok(7)) break;
            int x = rd.I16();
            int y = rd.I16();
            int suspicion = rd.U8();
            int state = rd.U8();
            int pose = rd.U8();
            SheepStatus sheep = _sheep[i];
            if (sheep == null) continue;
            bool alive = (state & 1) != 0;
            bool mine = sheep == LocalSheep;
            if (!mine)
            {
                ApplyPose(sheep, new Vector2(x, y), (byte)pose);
                sheep.isBad = (state & 2) != 0;
            }
            sheep.suspicion = suspicion;
            if (mine && _hud != null && !LocalIsOwner)
                _hud.UpdateSuspicion(suspicion);
            if (!alive && sheep.alive)
            {
                sheep.diesForGood = (state & 4) != 0;
                if (_aim != null) _aim.ReplayShot(sheep);
                else sheep.ApplyBurnedEffect();
                int who = PlayerOf(sheep);
                if (who >= 0) MarkOut(Find(who));
            }
            else if (alive && !sheep.alive && (state & 4) == 0)
            {
                sheep.ResetStatus();
            }
        }
        RefreshBanners();
    }

    void SeeShot(int index, bool perm)
    {
        SheepStatus sheep = SheepAt(index);
        if (sheep == null) return;
        sheep.diesForGood = perm;
        if (sheep.alive)
        {
            if (_aim != null) _aim.ReplayShot(sheep);
            else sheep.ApplyBurnedEffect();
        }
        int who = PlayerOf(sheep);
        if (who >= 0) MarkOut(Find(who));
    }

    void ReadEnd(Rd rd)
    {
        if (!rd.Ok(5)) return;
        _winnerId = rd.I16();
        _endRound = rd.I16();
        int count = rd.U8();
        for (int i = 0; i < count; i++)
        {
            if (!rd.Ok(8)) break;
            int id = rd.I16();
            int sheep = rd.I16();
            bool outsider = rd.U8() != 0;
            int seq = rd.U8();
            int round = rd.I16();
            Seat seat = EnsureSeat(id, null);
            seat.Sheep = sheep;
            seat.Out = outsider;
            seat.DeathSeq = seq;
            seat.DeathRound = round;
        }
    }

    string RandomCode()
    {
        const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        char[] chars = new char[4];
        for (int i = 0; i < 4; i++)
            chars[i] = letters[UnityEngine.Random.Range(0, letters.Length)];
        return new string(chars);
    }

    static string SessionOf(MatchKind mode, string code)
    {
        return (mode == MatchKind.OneVsOne ? "imp1-" : "impf-") + code.ToLowerInvariant();
    }

    static string NiceError(Exception e)
    {
        string message = e != null && e.Message != null ? e.Message : "";
        if (message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("GameNotFound", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Room not found. Use the same mode and code as the host.";
        if (message.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0)
            return "That room is full.";
        if (message.IndexOf("AlreadyExists", StringComparison.OrdinalIgnoreCase) >= 0)
            return "That code is already in use. Create the room again.";
        return "Could not connect. Check the code and try again.";
    }

    void RememberName()
    {
        if (_nameField != null && !string.IsNullOrEmpty(_nameField.text))
            _localName = _nameField.text.Trim();
        if (string.IsNullOrEmpty(_localName)) _localName = "Sheep";
        if (_localName.Length > 12) _localName = _localName.Substring(0, 12);
        PlayerPrefs.SetString(NameKey, _localName);
        PlayerPrefs.Save();
    }

    void SetStatus(string message)
    {
        if (_duelStatus != null) _duelStatus.text = message;
        if (_friendsStatus != null) _friendsStatus.text = message;
        if (_roomStatus != null) _roomStatus.text = message;
    }

    void HideLobby()
    {
        if (_modes != null) _modes.SetActive(false);
        if (_solo != null) _solo.SetActive(false);
        if (_home != null) _home.SetActive(false);
        if (_duel != null) _duel.SetActive(false);
        if (_friends != null) _friends.SetActive(false);
    }

    void ShowLobby(GameObject panel)
    {
        if (_root != null) _root.SetActive(true);
        if (_dim != null) _dim.SetActive(true);
        HideLobby();
        if (panel != null) panel.SetActive(true);
        if (_room != null) _room.SetActive(false);
        if (_sheet != null) _sheet.SetActive(false);
        RefreshBanners();
        RefreshControls();
    }

    void ShowStartPanel()
    {
        _phase = Phase.Menu;
        if (_root != null) _root.SetActive(true);
        if (_dim != null) _dim.SetActive(false);
        HideLobby();
        if (_room != null) _room.SetActive(false);
        if (_sheet != null) _sheet.SetActive(false);
        RefreshControls();
        RefreshBanners();
    }

    void ShowModes()
    {
        if (_phase == Phase.Solo)
            _phase = Phase.Menu;
        ShowLobby(_modes);
    }

    void ShowSolo()
    {
        ShowLobby(_solo);
    }

    void ShowMulti()
    {
        ShowLobby(_home);
    }

    void ShowDuel()
    {
        ShowLobby(_duel);
    }

    void ShowFriends()
    {
        ShowLobby(_friends);
    }

    void ShowModePanel()
    {
        if (_mode == MatchKind.Friends) ShowFriends();
        else ShowDuel();
    }

    void ShowRoom()
    {
        if (_root != null) _root.SetActive(true);
        if (_dim != null) _dim.SetActive(true);
        HideLobby();
        if (_room != null) _room.SetActive(true);
        if (_sheet != null) _sheet.SetActive(false);
        RefreshRoom();
        RefreshBanners();
        RefreshControls();
    }

    void RefreshRoom()
    {
        if (_roomCodeText != null)
            _roomCodeText.text = _roomCode;
        if (_roomHint != null)
        {
            _roomHint.text = _mode == MatchKind.OneVsOne
                ? "One player aims and shoots. The other is the imposter sheep. Three misses and the imposter wins."
                : "Everyone is a sheep. A full suspicion bar gets you shot. First out watches. The last sheep wins.";
        }
        if (_roomList != null)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < _seats.Count; i++)
            {
                Seat seat = _seats[i];
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(seat.Name.ToUpperInvariant());
                builder.Append("    ");
                builder.Append(RoleWord(seat));
            }
            if (_seats.Count == 0) builder.Append("Waiting for players");
            _roomList.text = builder.ToString();
        }
        bool host = IsAuthority;
        bool ready = host && ((_mode == MatchKind.OneVsOne && _seats.Count == 2) || (_mode == MatchKind.Friends && _seats.Count >= 2));
        if (_startButton != null) _startButton.interactable = ready;
        if (_startLabel != null)
            _startLabel.text = host ? "START" : "WAITING FOR HOST";
        if (_swapButton != null)
            _swapButton.gameObject.SetActive(host && _mode == MatchKind.OneVsOne && _phase == Phase.Room);
    }

    string RoleWord(Seat seat)
    {
        if (_mode == MatchKind.OneVsOne)
        {
            if (seat.Id == _ownerId) return "OWNER";
            if (seat.Id == _imposterId) return "IMPOSTER";
        }
        return "SHEEP";
    }

    void ShowTags()
    {
        HideTags();
        for (int i = 0; i < _seats.Count; i++)
        {
            SheepStatus sheep = SheepAt(_seats[i].Sheep);
            if (sheep == null) continue;
            GameObject go = new GameObject("PlayerTag", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Shadow));
            go.transform.SetParent(sheep.transform, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(0f, 96f);
            rt.sizeDelta = new Vector2(240f, 40f);
            Text text = go.GetComponent<Text>();
            text.font = UiFont();
            text.fontSize = 22;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 0.96f, 0.88f);
            text.raycastTarget = false;
            text.text = _seats[i].Name.ToUpperInvariant();
            Shadow shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }
    }

    void HideTags()
    {
        if (_sheep == null) return;
        for (int i = 0; i < _sheep.Length; i++)
        {
            if (_sheep[i] == null) continue;
            Transform tag = _sheep[i].transform.Find("PlayerTag");
            if (tag != null) Destroy(tag.gameObject);
        }
    }

    void BuildRows()
    {
        if (_grid == null) return;
        for (int i = _grid.transform.childCount - 1; i >= 0; i--)
            Destroy(_grid.transform.GetChild(i).gameObject);

        var rows = new List<Seat>(_seats);
        rows.Sort(CompareSeats);
        float y = 150f;
        AddSheetRow("RANK", "PLAYER", "SHEEP", "ROUNDS", "RESULT", y, true, false);
        y -= 58f;
        for (int i = 0; i < rows.Count; i++)
        {
            Seat seat = rows[i];
            bool winner = seat.Id == _winnerId;
            int rounds = winner ? _endRound : seat.DeathRound;
            AddSheetRow(
                (i + 1).ToString(),
                seat.Name.ToUpperInvariant(),
                SheepCell(seat),
                rounds.ToString(),
                ResultWord(seat),
                y,
                false,
                winner);
            y -= 52f;
        }
    }

    int CompareSeats(Seat a, Seat b)
    {
        if (a.Id == _winnerId && b.Id != _winnerId) return -1;
        if (b.Id == _winnerId && a.Id != _winnerId) return 1;
        return b.DeathSeq.CompareTo(a.DeathSeq);
    }

    string SheepCell(Seat seat)
    {
        if (_mode == MatchKind.OneVsOne)
        {
            if (seat.Id == _ownerId) return "OWNER";
            if (seat.Id == _imposterId) return "IMPOSTER";
        }
        SheepStatus sheep = SheepAt(seat.Sheep);
        if (sheep == null) return "-";
        string name = sheep.gameObject.name;
        if (name.StartsWith("Sheep_")) name = "SHEEP " + name.Substring(6).ToUpperInvariant();
        if (name == "SHEEP PLAYER") name = "SHEEP";
        return name;
    }

    string ResultWord(Seat seat)
    {
        if (seat.Id == _winnerId) return "WINNER";
        if (_mode == MatchKind.OneVsOne && seat.Id == _imposterId && _winnerId == _ownerId) return "CAUGHT";
        if (_mode == MatchKind.OneVsOne && seat.Id == _ownerId) return "MISSED";
        return "OUT";
    }

    void AddSheetRow(string rank, string player, string sheep, string rounds, string result, float y, bool header, bool winner)
    {
        GameObject row = new GameObject(header ? "Head" : "Row", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        row.transform.SetParent(_grid.transform, false);
        RectTransform rt = row.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(920f, header ? 54f : 48f);
        Image image = row.GetComponent<Image>();
        image.raycastTarget = false;
        if (header) image.color = new Color(0.86f, 0.42f, 0.16f, 1f);
        else if (winner) image.color = new Color(0.55f, 0.72f, 0.48f, 0.95f);
        else image.color = new Color(0.96f, 0.93f, 0.86f, 0.92f);

        Color ink = header ? Color.white : new Color(0.22f, 0.14f, 0.1f);
        Cell(row.transform, rank, -405f, 110f, ink);
        Cell(row.transform, player, -210f, 270f, ink);
        Cell(row.transform, sheep, 30f, 200f, ink);
        Cell(row.transform, rounds, 205f, 140f, ink);
        Cell(row.transform, result, 370f, 170f, ink);
    }

    void Cell(Transform parent, string value, float x, float width, Color color)
    {
        GameObject go = new GameObject("C", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(width, 40f);
        Text text = go.GetComponent<Text>();
        text.font = UiFont();
        text.fontSize = 22;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.raycastTarget = false;
        text.text = value;
    }

    void BuildUi()
    {
        if (FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        _root = new GameObject("OnlineUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _dim = ImageObject(_root.transform, "Dim", Vector2.zero, new Vector2(2400f, 1400f), new Color(0.05f, 0.06f, 0.08f, 0.78f));
        Stretch(_dim.GetComponent<RectTransform>());

        _modes = Panel(_root.transform, "Modes", Vector2.zero, new Vector2(760f, 640f), new Color(0.12f, 0.1f, 0.09f, 0.96f));
        Label(_modes.transform, "PLAY", 48, new Vector2(0f, 220f), new Vector2(680f, 70f), Color.white);
        MakeButton(_modes.transform, "SINGLE PLAYER", new Vector2(0f, 80f), new Vector2(520f, 80f), new Color(0.28f, 0.3f, 0.34f), ShowSolo);
        MakeButton(_modes.transform, "MULTIPLAYER", new Vector2(0f, -20f), new Vector2(520f, 80f), new Color(0.9f, 0.42f, 0.16f), ShowMulti);
        MakeButton(_modes.transform, "BACK", new Vector2(0f, -140f), new Vector2(520f, 64f), new Color(0.28f, 0.3f, 0.34f), ShowStartPanel);

        _solo = Panel(_root.transform, "SinglePlayer", Vector2.zero, new Vector2(760f, 560f), new Color(0.12f, 0.1f, 0.09f, 0.96f));
        Label(_solo.transform, "SINGLE PLAYER", 42, new Vector2(0f, 180f), new Vector2(680f, 60f), Color.white);
        Label(_solo.transform, "Play the flock on your own. The owner shoots when a sheep's suspicion is full.", 22, new Vector2(0f, 70f), new Vector2(620f, 90f), new Color(0.9f, 0.88f, 0.82f));
        MakeButton(_solo.transform, "PLAY", new Vector2(0f, -50f), new Vector2(520f, 80f), new Color(0.9f, 0.42f, 0.16f), PlayAlone);
        MakeButton(_solo.transform, "BACK", new Vector2(0f, -160f), new Vector2(520f, 64f), new Color(0.28f, 0.3f, 0.34f), ShowModes);
        _solo.SetActive(false);

        _home = Panel(_root.transform, "Multiplayer", Vector2.zero, new Vector2(760f, 640f), new Color(0.12f, 0.1f, 0.09f, 0.96f));
        Label(_home.transform, "MULTIPLAYER", 42, new Vector2(0f, 230f), new Vector2(680f, 60f), Color.white);
        Label(_home.transform, "YOUR NAME", 18, new Vector2(0f, 150f), new Vector2(680f, 28f), new Color(1f, 0.85f, 0.7f));
        _nameField = Field(_home.transform, _localName, 12, new Vector2(0f, 80f), new Vector2(520f, 64f));
        MakeButton(_home.transform, "1 VS 1", new Vector2(0f, -30f), new Vector2(520f, 74f), new Color(0.9f, 0.42f, 0.16f), ShowDuel);
        MakeButton(_home.transform, "PLAY WITH FRIENDS", new Vector2(0f, -120f), new Vector2(520f, 74f), new Color(0.9f, 0.42f, 0.16f), ShowFriends);
        MakeButton(_home.transform, "BACK", new Vector2(0f, -230f), new Vector2(520f, 64f), new Color(0.28f, 0.3f, 0.34f), ShowModes);
        _home.SetActive(false);

        _duel = ModePanel("OneVsOne", "1 VS 1", "One player aims and shoots. The other is the imposter sheep.", MatchKind.OneVsOne, out _duelCode, out _duelStatus);
        _friends = ModePanel("Friends", "PLAY WITH FRIENDS", "Everyone is a sheep. The last sheep standing wins.", MatchKind.Friends, out _friendsCode, out _friendsStatus);

        _room = Panel(_root.transform, "Room", Vector2.zero, new Vector2(820f, 860f), new Color(0.12f, 0.1f, 0.09f, 0.96f));
        Label(_room.transform, "ROOM", 28, new Vector2(0f, 360f), new Vector2(700f, 40f), new Color(1f, 0.85f, 0.7f));
        _roomCodeText = Label(_room.transform, "----", 64, new Vector2(0f, 280f), new Vector2(700f, 80f), Color.white);
        _roomHint = Label(_room.transform, "", 20, new Vector2(0f, 180f), new Vector2(720f, 80f), new Color(0.9f, 0.88f, 0.82f));
        _roomList = Label(_room.transform, "", 28, new Vector2(0f, 20f), new Vector2(680f, 220f), Color.white);
        _roomList.alignment = TextAnchor.UpperCenter;
        _startButton = MakeButton(_room.transform, "START", new Vector2(0f, -160f), new Vector2(520f, 74f), new Color(0.9f, 0.42f, 0.16f), HostStart);
        _startLabel = _startButton.GetComponentInChildren<Text>();
        _swapButton = MakeButton(_room.transform, "SWAP OWNER / IMPOSTER", new Vector2(0f, -250f), new Vector2(520f, 64f), new Color(0.28f, 0.3f, 0.34f), SwapRoles);
        MakeButton(_room.transform, "LEAVE ROOM", new Vector2(0f, -340f), new Vector2(520f, 64f), new Color(0.28f, 0.3f, 0.34f), LeaveRoom);
        _roomStatus = Label(_room.transform, "", 22, new Vector2(0f, -410f), new Vector2(700f, 40f), new Color(1f, 0.8f, 0.55f));
        _room.SetActive(false);

        _sheet = Panel(_root.transform, "Sheet", Vector2.zero, new Vector2(1000f, 760f), new Color(0.9f, 0.86f, 0.78f, 0.98f));
        Label(_sheet.transform, "RESULTS", 40, new Vector2(0f, 320f), new Vector2(900f, 56f), new Color(0.25f, 0.14f, 0.08f)).fontStyle = FontStyle.Bold;
        _grid = new GameObject("Grid", typeof(RectTransform));
        _grid.transform.SetParent(_sheet.transform, false);
        RectTransform gridRt = _grid.GetComponent<RectTransform>();
        gridRt.anchorMin = gridRt.anchorMax = new Vector2(0.5f, 0.5f);
        gridRt.anchoredPosition = new Vector2(0f, 10f);
        gridRt.sizeDelta = new Vector2(940f, 520f);
        MakeButton(_sheet.transform, "BACK TO ROOM", new Vector2(0f, -320f), new Vector2(420f, 68f), new Color(0.86f, 0.42f, 0.16f), RequestReturn);
        _sheet.SetActive(false);

        _roleText = Label(_root.transform, "", 28, new Vector2(0f, 470f), new Vector2(1200f, 40f), Color.white);
        _roleText.fontStyle = FontStyle.Bold;
        _watchText = Label(_root.transform, "", 26, new Vector2(0f, 420f), new Vector2(1200f, 36f), new Color(1f, 0.82f, 0.45f));

        _fireButton = MakeButton(_root.transform, "FIRE", new Vector2(760f, -360f), new Vector2(180f, 180f), new Color(0.9f, 0.28f, 0.22f), () => _shootEdge = true);
        _fireButton.gameObject.SetActive(false);
    }

    Font UiFont()
    {
        if (_font != null) return _font;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return _font;
    }

    GameObject ImageObject(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = color;
        return go;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    GameObject Panel(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        return ImageObject(parent, name, pos, size, color);
    }

    GameObject ModePanel(string name, string title, string hint, MatchKind kind, out InputField code, out Text status)
    {
        GameObject panel = Panel(_root.transform, name, Vector2.zero, new Vector2(760f, 860f), new Color(0.12f, 0.1f, 0.09f, 0.96f));
        Label(panel.transform, title, 42, new Vector2(0f, 340f), new Vector2(680f, 60f), Color.white);
        Label(panel.transform, hint, 22, new Vector2(0f, 250f), new Vector2(640f, 80f), new Color(0.9f, 0.88f, 0.82f));
        MakeButton(panel.transform, "CREATE ROOM", new Vector2(0f, 140f), new Vector2(520f, 74f), new Color(0.9f, 0.42f, 0.16f), () => CreateRoom(kind));
        Label(panel.transform, "ROOM CODE", 18, new Vector2(0f, 40f), new Vector2(680f, 28f), new Color(1f, 0.85f, 0.7f));
        code = Field(panel.transform, "", 4, new Vector2(0f, -30f), new Vector2(320f, 64f));
        MakeButton(panel.transform, "JOIN", new Vector2(0f, -130f), new Vector2(520f, 64f), new Color(0.25f, 0.48f, 0.72f), () => JoinRoom(kind));
        status = Label(panel.transform, "", 22, new Vector2(0f, -230f), new Vector2(680f, 70f), new Color(1f, 0.8f, 0.55f));
        MakeButton(panel.transform, "BACK", new Vector2(0f, -340f), new Vector2(520f, 64f), new Color(0.28f, 0.3f, 0.34f), ShowMulti);
        panel.SetActive(false);
        return panel;
    }

    Text Label(Transform parent, string value, int size, Vector2 pos, Vector2 box, Color color)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        Text text = go.GetComponent<Text>();
        text.font = UiFont();
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.text = value;
        return text;
    }

    Button MakeButton(Transform parent, string value, Vector2 pos, Vector2 size, Color color, UnityEngine.Events.UnityAction click)
    {
        GameObject go = ImageObject(parent, value, pos, size, color);
        Button button = go.AddComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();
        button.onClick.AddListener(click);
        Label(go.transform, value, 26, Vector2.zero, size, Color.white).fontStyle = FontStyle.Bold;
        return button;
    }

    InputField Field(Transform parent, string value, int limit, Vector2 pos, Vector2 size)
    {
        GameObject go = ImageObject(parent, "Field", pos, size, Color.white);
        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textGo.transform.SetParent(go.transform, false);
        RectTransform rt = textGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(16f, 8f);
        rt.offsetMax = new Vector2(-16f, -8f);
        Text text = textGo.GetComponent<Text>();
        text.font = UiFont();
        text.fontSize = 28;
        text.color = new Color(0.15f, 0.1f, 0.08f);
        text.alignment = TextAnchor.MiddleCenter;
        text.supportRichText = false;
        InputField field = go.AddComponent<InputField>();
        field.textComponent = text;
        field.characterLimit = limit;
        field.text = value;
        return field;
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (player == runner.LocalPlayer)
            EnsureSeat(player.PlayerId, _localName);
        SendHello();
        if (runner.IsServer)
            StartCoroutine(PushRosterSoon());
    }

    IEnumerator PushRosterSoon()
    {
        yield return null;
        if (IsAuthority && _phase == Phase.Room)
            BroadcastRoster();
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Seat seat = Find(player.PlayerId);
        if (seat == null) return;
        if (_phase == Phase.Playing && !_ending)
        {
            MarkOut(seat);
            if (IsAuthority && (_mode == MatchKind.OneVsOne || AliveCount() <= 1))
                BeginFinish(FirstAlive());
        }
        else if (_phase == Phase.Room)
        {
            _seats.Remove(seat);
            if (seat.Id == _imposterId) _imposterId = None;
            if (seat.Id == _ownerId)
            {
                _ownerId = _seats.Count > 0 ? _seats[0].Id : None;
                _imposterId = None;
                for (int i = 0; i < _seats.Count; i++)
                {
                    if (_seats[i].Id != _ownerId)
                    {
                        _imposterId = _seats[i].Id;
                        break;
                    }
                }
            }
            if (IsAuthority) BroadcastRoster();
        }
        RefreshRoom();
    }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;
        byte[] copy = new byte[data.Length];
        data.CopyTo(copy);
        try
        {
            OnPacket(player, copy);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Match] packet " + e.Message);
        }
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        if (_runner == runner) _runner = null;
        if (_closing)
        {
            _closing = false;
            return;
        }
        if (_phase == Phase.Playing || _phase == Phase.Board)
            ResetWorld();
        if (_gm != null && _phase != Phase.Solo)
            _gm.SetState(GameState.Start);
        if (_phase != Phase.Solo)
        {
            _phase = Phase.Menu;
            ShowModePanel();
            SetStatus("Disconnected.");
        }
    }

    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
}
