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

        if (btnPlay) btnPlay.onClick.AddListener(OnPlayClicked);
        if (btnRetry) btnRetry.onClick.AddListener(ReloadScene);
        if (btnReplay) btnReplay.onClick.AddListener(ReloadScene);
        if (btnResume) btnResume.onClick.AddListener(OnResumeClicked);
        if (btnRestart) btnRestart.onClick.AddListener(ReloadScene);
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
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (state == GameState.Playing)
                SetState(GameState.Paused);
            else if (state == GameState.Paused)
                SetState(GameState.Playing);
        }
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
        if (state == GameState.Playing)
            SetState(GameState.Paused);
    }

    public void OnResumeClicked()
    {
        if (state == GameState.Paused)
            SetState(GameState.Playing);
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
        if (hudController)
        {
            hudController.ShowPanel(newState, _score, _bestScore, roundsSurvived);
        }
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