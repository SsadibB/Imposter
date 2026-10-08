using UnityEngine;
using UnityEngine.UI;

public class AimController : MonoBehaviour
{
    [Header("Settings")]
    public float lockThreshold = 30f;
    public float closeDistance = 45f;
    public float shotCooldown = 1.5f;
    public float wobbleMagnitude = 25f;

    [Header("Difficulty Curves (R1 to R12)")]
    public float lockTimeR1 = 1.2f;
    public float lockTimeR12 = 0.7f;
    public float aimSpeedR1 = 500f;
    public float aimSpeedR12 = 900f;

    [Header("Aim & Owner References")]
    public RectTransform aimRect;
    public Image aimLockFill;
    public RectTransform ownerGun;
    public GameObject ownerMuzzleFlash;
    public GameObject ownerAlertMark;
    public CanvasGroup warningAimed;
    public GameObject flashShot;
    public LaserAimVisual laserVisual;

    [Header("Flock")]
    public SheepStatus[] allSheep; // All 9 sheep
    public GameObject[] fxPoofs;

    [Header("Controllers")]
    public GameManager gameManager;
    public RoundController roundController;
    public HUDController hudController;

    private SheepStatus _currentTarget = null;
    private float _targetCheckTimer = 0f;
    private float _scanTimer = 0f;
    private int _scanIndex = 0;
    private float _cooldownLeft = 0f;
    private float _lockProgress = 0f;
    private float _wobbleTime = 0f;
    private float _muzzleFlashTimer = 0f;
    private int _poofIndex = 0;

    // Preallocated buffer for sorting without GC
    private SheepStatus[] _aliveBuffer;

    void Awake()
    {
        _aliveBuffer = new SheepStatus[9];
        if (laserVisual == null)
        {
            laserVisual = FindFirstObjectByType<LaserAimVisual>();
        }
    }

    void Update()
    {
        if (gameManager == null || !gameManager.IsPlaying())
        {
            if (warningAimed && warningAimed.gameObject.activeSelf)
                warningAimed.gameObject.SetActive(false);
            if (ownerAlertMark && ownerAlertMark.activeSelf)
                ownerAlertMark.SetActive(false);
            if (laserVisual != null)
                laserVisual.SetLaserVisible(false);
            return;
        }

        float dt = Time.deltaTime;
        _cooldownLeft -= dt;

        // Muzzle flash timer
        if (_muzzleFlashTimer > 0f)
        {
            _muzzleFlashTimer -= dt;
            if (_muzzleFlashTimer <= 0f && ownerMuzzleFlash)
            {
                ownerMuzzleFlash.SetActive(false);
            }
        }

        // Current round difficulty
        int round = roundController ? roundController.Round : 1;
        float diffT = Mathf.Clamp01((round - 1) / 11f);
        float currentLockTime = Mathf.Lerp(lockTimeR1, lockTimeR12, diffT);
        float currentAimSpeed = Mathf.Lerp(aimSpeedR1, aimSpeedR12, diffT);

        // 1. Choose Target (re-check every 0.25 s)
        _targetCheckTimer -= dt;
        if (_targetCheckTimer <= 0f)
        {
            _targetCheckTimer = 0.25f;
            UpdateTargetSelection(dt);
        }

        if (_currentTarget == null || !_currentTarget.alive)
        {
            _lockProgress = 0f;
            if (aimLockFill) aimLockFill.fillAmount = 0f;
            if (laserVisual != null) laserVisual.SetLaserVisible(false);
            return;
        }

        // 2. Move the Aim
        _wobbleTime += dt * 3f;
        float wobbleCurrent = Mathf.Lerp(wobbleMagnitude, 6f, _lockProgress);
        Vector2 wobbleOffset = new Vector2(Mathf.Sin(_wobbleTime) * wobbleCurrent, Mathf.Cos(_wobbleTime * 1.3f) * wobbleCurrent);

        Vector2 desiredPos = _currentTarget.rect.anchoredPosition + _currentTarget.velocity * 0.2f + wobbleOffset;
        Vector2 currentAimPos = aimRect.anchoredPosition;
        aimRect.anchoredPosition = Vector2.MoveTowards(currentAimPos, desiredPos, currentAimSpeed * dt);

        // 3. Lock-On
        float distToTarget = Vector2.Distance(aimRect.anchoredPosition, _currentTarget.rect.anchoredPosition);
        bool isClose = distToTarget < closeDistance;
        bool commandActive = roundController && roundController.CommandActive;

        bool canLock = commandActive && isClose && _currentTarget.isBad &&
                       _currentTarget.suspicion >= lockThreshold && _cooldownLeft <= 0f;

        if (canLock)
        {
            _lockProgress += dt / currentLockTime;
        }
        else
        {
            _lockProgress -= (2f * dt) / currentLockTime;
        }
        _lockProgress = Mathf.Clamp01(_lockProgress);

        if (aimLockFill)
        {
            aimLockFill.fillAmount = _lockProgress;
        }

        // Check if locked and shoot
        if (_lockProgress >= 1f)
        {
            Shoot(_currentTarget);
            return;
        }

        // 4. Visuals (Gun rotation, Alert Mark, Warning, Laser Line)
        Vector3 worldGun = ownerGun ? ownerGun.position : Vector3.zero;
        Vector3 worldAim = aimRect ? aimRect.position : Vector3.zero;

        if (ownerGun)
        {
            Vector3 dir = worldAim - worldGun;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            ownerGun.eulerAngles = new Vector3(0f, 0f, angle);
        }

        if (ownerAlertMark)
        {
            ownerAlertMark.SetActive(_lockProgress > 0f);
        }

        // Laser aiming line visible from weapon to target
        if (laserVisual != null)
        {
            Vector3 muzzlePos = (ownerMuzzleFlash && ownerMuzzleFlash.transform != null) 
                ? ownerMuzzleFlash.transform.position 
                : worldGun;
            laserVisual.UpdateLaser(true, _lockProgress, muzzlePos, worldAim);
        }

        // Warning_Aimed for player wolf
        bool targetIsWolf = _currentTarget.isWolf;
        bool showWarning = targetIsWolf && (_lockProgress > 0f || (isClose && _currentTarget.suspicion >= lockThreshold));
        if (warningAimed)
        {
            if (showWarning)
            {
                if (!warningAimed.gameObject.activeSelf) warningAimed.gameObject.SetActive(true);
                warningAimed.alpha = 0.6f + Mathf.PingPong(Time.time * 5f, 0.4f);
            }
            else if (warningAimed.gameObject.activeSelf)
            {
                warningAimed.gameObject.SetActive(false);
            }
        }
    }

    private void UpdateTargetSelection(float dt)
    {
        int aliveCount = 0;
        for (int i = 0; i < allSheep.Length; i++)
        {
            if (allSheep[i] != null && allSheep[i].alive && allSheep[i].gameObject.activeSelf)
            {
                _aliveBuffer[aliveCount++] = allSheep[i];
            }
        }

        if (aliveCount == 0) return;

        // Simple insertion sort by suspicion descending (max 9 items -> extremely fast, zero GC)
        for (int i = 1; i < aliveCount; i++)
        {
            SheepStatus key = _aliveBuffer[i];
            int j = i - 1;
            while (j >= 0 && _aliveBuffer[j].suspicion < key.suspicion)
            {
                _aliveBuffer[j + 1] = _aliveBuffer[j];
                j--;
            }
            _aliveBuffer[j + 1] = key;
        }

        SheepStatus top = _aliveBuffer[0];

        // If everyone calm (< 8), scan top 3
        if (top.suspicion < 8f)
        {
            _scanTimer -= 0.25f;
            if (_scanTimer <= 0f)
            {
                _scanTimer = Random.Range(0.5f, 0.9f);
                int scanLimit = Mathf.Min(3, aliveCount);
                _scanIndex = (_scanIndex + 1) % scanLimit;
                _currentTarget = _aliveBuffer[_scanIndex];
            }
        }
        else
        {
            if (_currentTarget == null || !_currentTarget.alive || top.suspicion > _currentTarget.suspicion + 10f)
            {
                _currentTarget = top;
            }
        }
    }

    private void Shoot(SheepStatus target)
    {
        _lockProgress = 0f;
        if (aimLockFill) aimLockFill.fillAmount = 0f;
        _cooldownLeft = shotCooldown;

        // Muzzle Flash
        if (ownerMuzzleFlash)
        {
            ownerMuzzleFlash.SetActive(true);
            _muzzleFlashTimer = 0.1f;
        }

        Vector3 muzzlePos = (ownerMuzzleFlash && ownerMuzzleFlash.transform != null) 
            ? ownerMuzzleFlash.transform.position 
            : (ownerGun ? ownerGun.position : Vector3.zero);
        Vector3 targetPos = target.rect.position;

        // Play fire effect along the line
        if (laserVisual != null)
        {
            laserVisual.SetLaserVisible(false);
            laserVisual.PlayFireShotAlongLine(muzzlePos, targetPos, () =>
            {
                ApplyHitToTarget(target);
            });
        }
        else
        {
            ApplyHitToTarget(target);
        }
    }

    private void ApplyHitToTarget(SheepStatus target)
    {
        if (target == null) return;

        // Play Poof VFX
        if (fxPoofs != null && fxPoofs.Length > 0)
        {
            GameObject poof = fxPoofs[_poofIndex % fxPoofs.Length];
            _poofIndex++;
            if (poof)
            {
                poof.GetComponent<RectTransform>().anchoredPosition = target.rect.anchoredPosition;
                poof.SetActive(false);
                poof.SetActive(true);
                StartCoroutine(HidePoofAfter(poof, 0.5f));
            }
        }

        // Apply burned effect
        target.ApplyBurnedEffect();

        if (target.isWolf)
        {
            // Flash shot full screen
            if (flashShot)
            {
                flashShot.SetActive(true);
                StartCoroutine(HideFlashShotAfter(0.15f));
            }
            if (gameManager)
            {
                gameManager.Lose();
            }
        }
        // Normal sheep: they will recover automatically via SheepStatus.BurnRecoverRoutine
        // No suspicion wipe — rounds are infinite and the flock stays in play
    }

    private System.Collections.IEnumerator HidePoofAfter(GameObject poof, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (poof) poof.SetActive(false);
    }

    private System.Collections.IEnumerator HideFlashShotAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (flashShot) flashShot.SetActive(false);
    }
}
