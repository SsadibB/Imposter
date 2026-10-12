using UnityEngine;

public enum CommandType { HeadsUp, HeadsDownGraze, WalkSlowly, Scatter, MakeSounds, AllStop }
public enum SheepBehaviorMode { Normal, Late, Quitter }

public class RoundController : MonoBehaviour
{
    [Header("Settings")]
    // Game is now infinite — rounds increment forever until the wolf is caught
    public float graceDuration = 0.4f;

    [Header("References")]
    public GameManager gameManager;
    public HUDController hudController;
    public WolfController wolf;
    public SheepAI[] normalSheep;

    public int Round { get; private set; } = 0;
    public bool CommandActive { get; private set; } = false;
    public bool GraceOver { get; private set; } = false;
    public CommandType ActiveCommand { get; private set; } = CommandType.HeadsDownGraze;
    public float WindowTime { get; private set; } = 4f;
    public float TimerRemaining { get; private set; } = 0f;

    private float _intermissionTimer = 0f;
    private bool _inIntermission = false;
    private float _graceTimer = 0f;
    private CommandType _lastCommand = (CommandType)(-1);

    // Correct_Command plays after a streak of 3 or 4 correct commands (target picked at random)
    private const int StreakMin = 3;
    private const int StreakMax = 4; // inclusive
    private int _correctStreak = 0;
    private int _streakTarget = StreakMin;
    private int[] _aliveOrder = new int[8];

    private static readonly string[] CommandDisplayTexts = {
        "HEADS UP",
        "HEADS DOWN + GRAZE",
        "FLOCKS WALK SLOWLY",
        "SCATTER",
        "MAKE SOUNDS",
        "ALL STOP"
    };

    public void ResetForMenu()
    {
        Round = 0;
        _inIntermission = false;
        _intermissionTimer = 0f;
        CommandActive = false;
        GraceOver = false;
        TimerRemaining = 0f;
        _correctStreak = 0;

        if (normalSheep != null)
        {
            for (int i = 0; i < normalSheep.Length; i++)
            {
                if (normalSheep[i] != null)
                    normalSheep[i].EndCommand();
            }
        }
        if (wolf != null) wolf.EndCommand();
        if (hudController) hudController.HideCommand();
        if (SoundLibrary.Instance != null)
            SoundLibrary.Instance.StopSFX("Sheep_Graze");
    }

    public void OnPlay()
    {
        Round = 0;
        _correctStreak = 0;
        _streakTarget = Random.Range(StreakMin, StreakMax + 1);
        if (SoundLibrary.Instance != null)
            SoundLibrary.Instance.StopSFX("Sheep_Graze");
        StartIntermission();
    }

    public void ApplyRemote(int round, int cmd, bool active, bool grace, float remaining, float window)
    {
        CommandType next = (CommandType)Mathf.Clamp(cmd, 0, 5);
        bool changed = (active != CommandActive) || (active && next != ActiveCommand);
        Round = round;
        ActiveCommand = next;
        CommandActive = active;
        GraceOver = grace;
        TimerRemaining = remaining;
        WindowTime = Mathf.Max(0.25f, window);

        if (hudController)
        {
            if (active)
            {
                if (changed)
                    hudController.ShowCommand(CommandDisplayTexts[(int)next], round, round);
                hudController.UpdateCommandBar(Mathf.Clamp01(remaining / WindowTime));
            }
            else if (changed)
            {
                hudController.HideCommand();
            }
        }

        if (changed && SoundLibrary.Instance != null)
        {
            SoundLibrary.Instance.StopSFX("Sheep_Graze");
            if (active && next == CommandType.HeadsDownGraze)
                SoundLibrary.Instance.PlaySFX("Sheep_Graze", true);
        }
    }

    void Update()
    {
        if (gameManager == null || !gameManager.IsPlaying()) return;
        if (MatchSession.Playing && !MatchSession.Instance.IsAuthority)
            return;

        if (_inIntermission)
        {
            _intermissionTimer -= Time.deltaTime;
            if (_intermissionTimer <= 0f)
            {
                Round++;
                StartCommand(); // No upper limit — game runs until wolf is caught
            }
        }
        else if (CommandActive)
        {
            TimerRemaining -= Time.deltaTime;
            _graceTimer -= Time.deltaTime;
            GraceOver = (_graceTimer <= 0f);

            if (hudController)
            {
                hudController.UpdateCommandBar(Mathf.Clamp01(TimerRemaining / WindowTime));
            }

            if (TimerRemaining <= 0f)
            {
                EndCommand();
            }
        }
    }

    private void StartIntermission()
    {
        _inIntermission = true;
        CommandActive = false;
        GraceOver = false;
        _intermissionTimer = (Round >= 4) ? 0.3f : 1.2f;

        if (hudController)
        {
            hudController.HideCommand();
        }
    }

    private void StartCommand()
    {
        _inIntermission = false;
        CommandActive = true;
        _graceTimer = graceDuration;
        GraceOver = false;

        // Difficulty curve: gets gradually harder, caps at round 20
        float t = Mathf.Clamp01((Round - 1) / 19f);
        WindowTime = Mathf.Lerp(4.0f, 2.0f, t);
        TimerRemaining = WindowTime;

        // Pick command different from last
        int nextCmd = Random.Range(0, 6);
        if ((CommandType)nextCmd == _lastCommand)
            nextCmd = (nextCmd + 1 + Random.Range(0, 5)) % 6;
        ActiveCommand = (CommandType)nextCmd;
        _lastCommand = ActiveCommand;

        // Sheep_Graze loops after a Heads Down + Graze command and keeps going until the next command starts
        if (SoundLibrary.Instance != null)
        {
            SoundLibrary.Instance.StopSFX("Sheep_Graze");
            if (ActiveCommand == CommandType.HeadsDownGraze)
                SoundLibrary.Instance.PlaySFX("Sheep_Graze", true);
        }

        // Pick slow sheep — after round 12 all 3 slots are always hard
        int slowTargetCount = (Round <= 4) ? 1 : (Round <= 8) ? 2 : 3;
        int aliveCount = 0;
        int sheepCount = normalSheep != null ? normalSheep.Length : 0;
        if (_aliveOrder.Length < sheepCount)
            _aliveOrder = new int[sheepCount];

        for (int i = 0; i < sheepCount; i++)
        {
            SheepAI sheep = normalSheep[i];
            if (sheep != null && sheep.status != null && sheep.status.alive && sheep.gameObject.activeSelf)
                _aliveOrder[aliveCount++] = i;
        }

        for (int i = 0; i < aliveCount; i++)
        {
            int r = Random.Range(i, aliveCount);
            int tmp = _aliveOrder[i];
            _aliveOrder[i] = _aliveOrder[r];
            _aliveOrder[r] = tmp;
        }

        int actualSlowCount = Mathf.Min(slowTargetCount, aliveCount);

        for (int i = 0; i < sheepCount; i++)
        {
            SheepAI sheep = normalSheep[i];
            if (sheep == null || !sheep.gameObject.activeSelf || (sheep.status != null && !sheep.status.alive)) continue;

            SheepBehaviorMode mode = SheepBehaviorMode.Normal;
            for (int s = 0; s < actualSlowCount; s++)
            {
                if (_aliveOrder[s] != i) continue;
                mode = (Random.value < 0.5f) ? SheepBehaviorMode.Late : SheepBehaviorMode.Quitter;
                break;
            }
            sheep.BeginCommand(ActiveCommand, mode);
        }

        if (wolf != null && (wolf.status == null || wolf.status.alive))
        {
            wolf.BeginCommand(ActiveCommand);
        }

        if (hudController)
        {
            // Pass Round as both current and max — HUD shows "ROUND X" without an end cap
            hudController.ShowCommand(CommandDisplayTexts[(int)ActiveCommand], Round, Round);
        }
    }

    private void EndCommand()
    {
        CommandActive = false;
        GraceOver = false;

        for (int i = 0; i < normalSheep.Length; i++)
        {
            if (normalSheep[i] != null && normalSheep[i].gameObject.activeSelf && (normalSheep[i].status == null || normalSheep[i].status.alive))
                normalSheep[i].EndCommand();
        }

        // Was the player's wolf performing the command correctly when the window closed?
        bool wolfCorrect = wolf != null && wolf.status != null && wolf.status.alive && !wolf.status.isBad;
        if (wolfCorrect)
        {
            _correctStreak++;
            if (_correctStreak >= _streakTarget)
            {
                if (SoundLibrary.Instance != null)
                    SoundLibrary.Instance.PlaySFX("Correct_Command");
                _correctStreak = 0;
                _streakTarget = Random.Range(StreakMin, StreakMax + 1);
            }
        }
        else
        {
            _correctStreak = 0;
        }

        if (wolf != null)
            wolf.EndCommand();

        if (gameManager)
        {
            // +20 command survived + 100 round survived
            gameManager.AddScore(120);
        }

        StartIntermission();
    }
}