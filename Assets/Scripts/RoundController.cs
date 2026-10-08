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

    private static readonly string[] CommandDisplayTexts = {
        "HEADS UP",
        "HEADS DOWN + GRAZE",
        "FLOCKS WALK SLOWLY",
        "SCATTER",
        "MAKE SOUNDS",
        "ALL STOP"
    };

    public void OnPlay()
    {
        Round = 0;
        StartIntermission();
    }

    void Update()
    {
        if (gameManager == null || !gameManager.IsPlaying()) return;

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

        // Pick slow sheep — after round 12 all 3 slots are always hard
        int slowTargetCount = (Round <= 4) ? 1 : (Round <= 8) ? 2 : 3;
        var aliveSheepIndices = new System.Collections.Generic.List<int>();
        for (int i = 0; i < normalSheep.Length; i++)
        {
            if (normalSheep[i] != null && normalSheep[i].status != null && normalSheep[i].status.alive && normalSheep[i].gameObject.activeSelf)
            {
                aliveSheepIndices.Add(i);
            }
        }

        // Shuffle alive sheep indices
        for (int i = 0; i < aliveSheepIndices.Count; i++)
        {
            int r = Random.Range(i, aliveSheepIndices.Count);
            int tmp = aliveSheepIndices[i];
            aliveSheepIndices[i] = aliveSheepIndices[r];
            aliveSheepIndices[r] = tmp;
        }

        int actualSlowCount = Mathf.Min(slowTargetCount, aliveSheepIndices.Count);
        var slowIndicesSet = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < actualSlowCount; i++)
        {
            slowIndicesSet.Add(aliveSheepIndices[i]);
        }

        for (int i = 0; i < normalSheep.Length; i++)
        {
            if (normalSheep[i] == null || !normalSheep[i].gameObject.activeSelf || (normalSheep[i].status != null && !normalSheep[i].status.alive)) continue;

            SheepBehaviorMode mode = SheepBehaviorMode.Normal;
            if (slowIndicesSet.Contains(i))
            {
                mode = (Random.value < 0.5f) ? SheepBehaviorMode.Late : SheepBehaviorMode.Quitter;
            }
            normalSheep[i].BeginCommand(ActiveCommand, mode);
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
