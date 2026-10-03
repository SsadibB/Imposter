using UnityEngine;

public enum CommandType { Graze, AllStop, Scatter, MakeSounds }
public enum SheepBehaviorMode { Normal, Late, Quitter }

public class RoundController : MonoBehaviour
{
    [Header("Settings")]
    public int roundsToWin = 12;
    public float graceDuration = 0.4f;

    [Header("References")]
    public GameManager gameManager;
    public HUDController hudController;
    public WolfController wolf;
    public SheepAI[] normalSheep;

    public int Round { get; private set; } = 0;
    public bool CommandActive { get; private set; } = false;
    public bool GraceOver { get; private set; } = false;
    public CommandType ActiveCommand { get; private set; } = CommandType.Graze;
    public float WindowTime { get; private set; } = 4f;
    public float TimerRemaining { get; private set; } = 0f;

    private float _intermissionTimer = 0f;
    private bool _inIntermission = false;
    private float _graceTimer = 0f;
    private CommandType _lastCommand = (CommandType)(-1);

    private static readonly string[] CommandDisplayTexts = {
        "HEADS DOWN, GRAZE",
        "ALL STOP",
        "SCATTER",
        "MAKE SOUNDS"
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
                if (Round > roundsToWin)
                {
                    gameManager.Win();
                }
                else
                {
                    StartCommand();
                }
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

        // Difficulty curve t = (Round - 1) / 11
        float t = Mathf.Clamp01((Round - 1) / 11f);
        WindowTime = Mathf.Lerp(4.0f, 2.5f, t);
        TimerRemaining = WindowTime;

        // Pick command different from last
        int nextCmd = Random.Range(0, 4);
        if ((CommandType)nextCmd == _lastCommand)
        {
            nextCmd = (nextCmd + 1 + Random.Range(0, 3)) % 4;
        }
        ActiveCommand = (CommandType)nextCmd;
        _lastCommand = ActiveCommand;

        // Pick slow sheep
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
            if (normalSheep[i] == null || !normalSheep[i].gameObject.activeSelf) continue;

            SheepBehaviorMode mode = SheepBehaviorMode.Normal;
            if (slowIndicesSet.Contains(i))
            {
                mode = (Random.value < 0.5f) ? SheepBehaviorMode.Late : SheepBehaviorMode.Quitter;
            }
            normalSheep[i].BeginCommand(ActiveCommand, mode);
        }

        if (wolf != null)
        {
            wolf.BeginCommand(ActiveCommand);
        }

        if (hudController)
        {
            hudController.ShowCommand(CommandDisplayTexts[(int)ActiveCommand], Round, roundsToWin);
        }
    }

    private void EndCommand()
    {
        CommandActive = false;
        GraceOver = false;

        for (int i = 0; i < normalSheep.Length; i++)
        {
            if (normalSheep[i] != null && normalSheep[i].gameObject.activeSelf)
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
