using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public enum GameState { Start, Playing, Paused, Won, Lost }

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("State")]
    public GameState state = GameState.Start;

    [Header("Buttons")]
    public Button btnPlay;
    public Button btnRetry;
    public Button btnReplay;
    public Button btnResume;
    public Button btnRestart;
    public Button btnPause;

    [Header("Controllers")]
    public RoundController roundController;
    public AimController aimController;
    public HUDController hudController;
    public SheepStatus[] allSheep;

    [Header("Score")]
    [SerializeField] private int _score = 0;
    private int _bestScore = 0;
    private const string PrefsBest = "BestScore";

    public int Score => _score;
    public int BestScore => _bestScore;

    void Awake()
    {
        Instance = this;
        _bestScore = PlayerPrefs.GetInt(PrefsBest, 0);
    }

    void Start()
    {
        AudioListener.pause = false;
        SetState(GameState.Start);

        // Background music plays from the start screen onward
        if (SoundLibrary.Instance != null)
            SoundLibrary.Instance.PlayMusic("Main_BGM");

        if (btnPlay)
        {
            btnPlay.onClick.RemoveAllListeners();
            btnPlay.onClick.AddListener(OpenModeSelect);
        }
        EnsureGameOverMenu();
        EnsurePauseMenu();
        if (btnRetry)
        {
            btnRetry.onClick.RemoveAllListeners();
            btnRetry.onClick.AddListener(RetryRound);
        }
        if (btnReplay)
        {
            btnReplay.onClick.RemoveAllListeners();
            btnReplay.onClick.AddListener(RetryRound);
        }
        if (btnResume) btnResume.onClick.AddListener(OnResumeClicked);
        if (btnRestart)
        {
            btnRestart.onClick.RemoveAllListeners();
            btnRestart.onClick.AddListener(RetryRound);
        }
        if (btnPause) btnPause.onClick.AddListener(OnPauseClicked);

        if (hudController)
        {
            hudController.UpdateBest(_bestScore);
            hudController.UpdateScore(0);
            hudController.UpdateSheepLeft(8);
        }
    }

    void Update()
    {
        if (MatchSession.Playing)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (state == GameState.Playing)
                SetState(GameState.Paused);
            else if (state == GameState.Paused)
                SetState(GameState.Playing);
        }
    }

    public void OpenModeSelect()
    {
        MatchSession.Ensure().OpenModes();
    }

    public void RetryRound()
    {
        StopAllCoroutines();
        _losingInProgress = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        _score = 0;
        if (hudController) hudController.UpdateScore(0);
        if (roundController) roundController.ResetForMenu();
        if (SoundLibrary.Instance != null)
        {
            SoundLibrary.Instance.StopSFX("Sheep_Graze");
            SoundLibrary.Instance.PlayMusic("Main_BGM");
        }
        if (MatchSession.Instance != null)
            MatchSession.Instance.RestartSolo();
        OnPlayClicked();
    }

    public void OnPlayClicked()
    {
        SetState(GameState.Playing);
        if (roundController)
        {
            roundController.OnPlay();
        }
    }

    public void OnPauseClicked()
    {
        if (MatchSession.Playing)
            return;
        if (state == GameState.Playing)
            SetState(GameState.Paused);
    }

    public void OnResumeClicked()
    {
        if (state == GameState.Paused)
            SetState(GameState.Playing);
    }

    void EnsureGameOverMenu()
    {
        if (btnRetry == null || btnRetry.transform.parent == null) return;
        if (btnRetry.transform.parent.Find("Btn_Menu") != null) return;

        RectTransform retry = btnRetry.GetComponent<RectTransform>();
        float y = retry.anchoredPosition.y;
        retry.anchoredPosition = new Vector2(-160f, y);
        retry.sizeDelta = new Vector2(300f, retry.sizeDelta.y);

        GameObject go = Instantiate(btnRetry.gameObject, btnRetry.transform.parent);
        go.name = "Btn_Menu";
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(160f, y);
        rt.sizeDelta = new Vector2(300f, retry.sizeDelta.y);

        Image image = go.GetComponent<Image>();
        if (image != null) image.color = new Color(0.28f, 0.3f, 0.34f);

        TMPro.TextMeshProUGUI label = go.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (label != null) label.text = "MAIN MENU";

        Button button = go.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(ReturnToStartPanel);
    }

    void EnsurePauseMenu()
    {
        if (btnRestart == null || btnResume == null || btnRestart.transform.parent == null) return;
        if (btnRestart.transform.parent.Find("Btn_MainMenu") != null) return;

        RectTransform resume = btnResume.GetComponent<RectTransform>();
        RectTransform restart = btnRestart.GetComponent<RectTransform>();
        resume.anchoredPosition = new Vector2(0f, 20f);
        restart.anchoredPosition = new Vector2(0f, -100f);

        GameObject go = Instantiate(btnRestart.gameObject, btnRestart.transform.parent);
        go.name = "Btn_MainMenu";
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(0f, -210f);
        rt.sizeDelta = new Vector2(restart.sizeDelta.x, 80f);

        Image image = go.GetComponent<Image>();
        if (image != null) image.color = new Color(0.28f, 0.3f, 0.34f);

        TMPro.TextMeshProUGUI label = go.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (label != null) label.text = "MAIN MENU";

        Button button = go.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(ReturnToStartPanel);
    }

    public void ReturnToStartPanel()
    {
        StopAllCoroutines();
        _losingInProgress = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        _score = 0;
        if (hudController) hudController.UpdateScore(0);
        if (roundController) roundController.ResetForMenu();
        if (SoundLibrary.Instance != null)
        {
            SoundLibrary.Instance.StopSFX("Sheep_Graze");
            SoundLibrary.Instance.PlayMusic("Main_BGM");
        }
        SetState(GameState.Start);
        if (btnPlay != null)
        {
            btnPlay.onClick.RemoveAllListeners();
            btnPlay.onClick.AddListener(OpenModeSelect);
        }
        if (MatchSession.Instance != null)
            MatchSession.Instance.ReturnToStart();
    }

    public void ReloadScene()
    {
        AudioListener.pause = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene(0);
    }

    public void SetState(GameState newState)
    {
        state = newState;
        Time.timeScale = (newState == GameState.Paused) ? 0f : 1f;

        // Pause/resume all audio together with the game
        AudioListener.pause = (newState == GameState.Paused);

        int roundsSurvived = roundController ? Mathf.Max(0, roundController.Round - 1) : 0;
        bool inGame = newState == GameState.Playing || newState == GameState.Paused;
        if (btnPause) btnPause.gameObject.SetActive(inGame);
        if (hudController)
        {
            hudController.ShowPanel(newState, _score, _bestScore, roundsSurvived);
            hudController.SetPlayChrome(inGame);
        }
        if (MatchSession.Instance != null)
            MatchSession.Instance.RefreshControls();
    }

    public void AddScore(int amount)
    {
        _score += amount;
        if (hudController)
        {
            hudController.UpdateScore(_score);
        }
    }

    public void Win()
    {
        // Win is no longer reachable — game is infinite.
        // Kept as a safety stub in case other code calls it.
        Debug.Log("[GameManager] Win() called but game is infinite.");
    }

    private bool _losingInProgress = false;

    public void Lose()
    {
        if (state != GameState.Playing || _losingInProgress) return;
        _losingInProgress = true;

        // Cut the music right away so the defeat jingle stands out
        if (SoundLibrary.Instance != null)
            SoundLibrary.Instance.StopMusic();

        StartCoroutine(LoseAfterDelay(2f));
    }

    private System.Collections.IEnumerator LoseAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SaveBestScore();
        SetState(GameState.Lost);

        if (SoundLibrary.Instance != null)
        {
            SoundLibrary.Instance.StopAllSFX();
            SoundLibrary.Instance.PlaySFX("Defeat_Jingle");
        }
        _losingInProgress = false;
    }

    private void SaveBestScore()
    {
        if (_score > _bestScore)
        {
            _bestScore = _score;
            PlayerPrefs.SetInt(PrefsBest, _bestScore);
            PlayerPrefs.Save();
            if (hudController)
            {
                hudController.UpdateBest(_bestScore);
            }
        }
    }

    public bool IsPlaying() => state == GameState.Playing;
}