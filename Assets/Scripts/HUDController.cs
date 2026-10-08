using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HUDController : MonoBehaviour
{
    [Header("Colors (Serialized)")]
    public Color colorLow = new Color(0.42f, 0.75f, 0.35f);    // Green below 30
    public Color colorMedium = new Color(0.95f, 0.76f, 0.31f); // Yellow 30-60
    public Color colorHigh = new Color(0.90f, 0.28f, 0.30f);   // Red above 60

    [Header("Header Texts")]
    public TextMeshProUGUI textRound;
    public TextMeshProUGUI textScore;
    public TextMeshProUGUI textBest;
    public TextMeshProUGUI textSheepLeft;

    [Header("Command Panel")]
    public GameObject commandPanel;
    public TextMeshProUGUI textCommand;
    public Image barCommandFill;
    public TextMeshProUGUI textStatus;

    [Header("Suspicion Bar")]
    public Image barSuspFill;

    [Header("Panels")]
    public GameObject panelStart;
    public GameObject panelSpotted;
    public GameObject panelWin;
    public GameObject panelPause;

    [Header("Panel Spotted Texts")]
    public TextMeshProUGUI spottedFinalScore;
    public TextMeshProUGUI spottedRoundsLine;
    public TextMeshProUGUI spottedBestLine;

    [Header("Panel Win Texts")]
    public TextMeshProUGUI winFinalScore;
    public TextMeshProUGUI winBestLine;

    // Change-tracking cache for zero allocations
    private int _cachedRound = -1;
    private int _cachedScore = -1;
    private int _cachedBest = -1;
    private int _cachedSheepLeft = -1;
    private float _cachedSuspNormalized = -1f;
    private float _cachedCommandFill = -1f;

    public void ShowCommand(string commandName, int round, int totalRounds)
    {
        if (commandPanel && !commandPanel.activeSelf) commandPanel.SetActive(true);
        if (textCommand) textCommand.text = commandName;
        UpdateRound(round, totalRounds);
        UpdateCommandBar(1f);
        HideStatus();
    }

    public void HideCommand()
    {
        if (textCommand) textCommand.text = "";
        UpdateCommandBar(0f);
        HideStatus();
    }

    public void ShowStatus(bool isGood)
    {
        if (textStatus == null) return;
        if (!textStatus.gameObject.activeSelf) textStatus.gameObject.SetActive(true);
        textStatus.text = isGood ? "GOOD!" : "WRONG!";
        textStatus.color = isGood ? colorLow : colorHigh;
    }

    public void HideStatus()
    {
        if (textStatus && textStatus.gameObject.activeSelf)
        {
            textStatus.gameObject.SetActive(false);
        }
    }

    public void UpdateRound(int currentRound, int maxRounds)
    {
        if (_cachedRound == currentRound) return;
        _cachedRound = currentRound;
        if (textRound) textRound.text = "ROUND " + currentRound;
    }

    public void UpdateScore(int score)
    {
        if (_cachedScore == score) return;
        _cachedScore = score;
        if (textScore) textScore.text = score.ToString();
    }

    public void UpdateBest(int best)
    {
        if (_cachedBest == best) return;
        _cachedBest = best;
        if (textBest) textBest.text = "BEST " + best;
    }

    public void UpdateSheepLeft(int count)
    {
        if (_cachedSheepLeft == count) return;
        _cachedSheepLeft = count;
        if (textSheepLeft) textSheepLeft.text = "SHEEP LEFT " + count;
    }

    public void UpdateCommandBar(float normalized)
    {
        if (Mathf.Abs(_cachedCommandFill - normalized) < 0.005f) return;
        _cachedCommandFill = normalized;
        if (barCommandFill) barCommandFill.fillAmount = normalized;
    }

    public void UpdateSuspicion(float value)
    {
        float normalized = Mathf.Clamp01(value / 100f);
        if (Mathf.Abs(_cachedSuspNormalized - normalized) < 0.005f) return;
        _cachedSuspNormalized = normalized;

        if (barSuspFill)
        {
            barSuspFill.fillAmount = normalized;
            if (value < 30f)
                barSuspFill.color = colorLow;
            else if (value < 60f)
                barSuspFill.color = colorMedium;
            else
                barSuspFill.color = colorHigh;
        }
    }

    public void ShowPanel(GameState state, int finalScore, int bestScore, int roundsSurvived)
    {
        if (panelStart) panelStart.SetActive(state == GameState.Start);
        if (panelPause) panelPause.SetActive(state == GameState.Paused);
        if (panelWin) panelWin.SetActive(state == GameState.Won);
        if (panelSpotted) panelSpotted.SetActive(state == GameState.Lost);

        if (state == GameState.Lost)
        {
            if (spottedFinalScore) spottedFinalScore.text = finalScore.ToString();
            if (spottedRoundsLine) spottedRoundsLine.text = "SURVIVED " + roundsSurvived + " ROUNDS";
            if (spottedBestLine) spottedBestLine.text = "BEST " + bestScore;
        }
        else if (state == GameState.Won)
        {
            if (winFinalScore) winFinalScore.text = finalScore.ToString();
            if (winBestLine) winBestLine.text = "BEST " + bestScore;
        }
    }
}
