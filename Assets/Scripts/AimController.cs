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
    private bool _alertSoundPlayed = false;
    private float _lastAlertSoundTime = -10f;
    private const float AlertSoundMinGap = 0.6f;

    // Preallocated buffer for sorting without GC
    private SheepStatus[] _aliveBuffer;

    private RectTransform[] _poofRects;

    void Awake()
    {
        _aliveBuffer = new SheepStatus[9];
        if (laserVisual == null)
            laserVisual = FindFirstObjectByType<LaserAimVisual>();

        if (fxPoofs != null)
        {
            _poofRects = new RectTransform[fxPoofs.Length];
            for (int i = 0; i < fxPoofs.Length; i++)
            {
                if (fxPoofs[i] != null)
                    _poofRects[i] = fxPoofs[i].GetComponent<RectTransform>();
            }
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
            _alertSoundPlayed = false;
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

        if (MatchSession.Playing)
        {
            MatchSession match = MatchSession.Instance;
            if (match.UsesHumanAim)
            {
                if (match.IsAuthority)
                    match.TickHumanAim(this);
                else if (match.LocalIsOwner)
                    match.TickLocalOwnerAim(this);
                PresentAimVisuals();
                return;
            }
            if (!match.IsAuthority)
            {
                PresentAimVisuals();
                return;
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

        // A full bar pulls the gun onto that sheep. The shot waits until the gun arrives.
        SheepStatus fullSuspicion = FindFullSuspicionSheep();
        if (fullSuspicion != null)
            _currentTarget = fullSuspicion;

        if (_currentTarget == null || !_currentTarget.alive)
        {
            _lockProgress = 0f;
            _alertSoundPlayed = false;
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

        // Fire only when the gun is on the suspicious sheep, not while it is still sweeping past the others.
        if (fullSuspicion != null && _currentTarget == fullSuspicion && _cooldownLeft <= 0f &&
            !(MatchSession.Playing && !MatchSession.Instance.AcceptsFire) &&
            GunIsOn(fullSuspicion))
        {
            PresentAimVisuals();
            Shoot(fullSuspicion);
            return;
        }

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
        // The lock ring can only finish when the suspicion bar itself is full.
        float suspicionCap = Mathf.Clamp01(_currentTarget.suspicion / 100f);
        _lockProgress = Mathf.Clamp(_lockProgress, 0f, suspicionCap);

        if (aimLockFill)
        {
            aimLockFill.fillAmount = _lockProgress;
        }

        // Alert_Lock: play once each time the owner starts locking on
        if (_lockProgress > 0f)
        {
            if (!_alertSoundPlayed && Time.time - _lastAlertSoundTime >= AlertSoundMinGap)
            {
                _alertSoundPlayed = true;
                _lastAlertSoundTime = Time.time;
                if (SoundLibrary.Instance != null)
                    SoundLibrary.Instance.PlaySFX("Alert_Lock");
            }
        }
        else
        {
            _alertSoundPlayed = false;
        }

        PresentAimVisuals();
    }

    public void PlaceAim(Vector2 anchored)
    {
        if (aimRect)
            aimRect.anchoredPosition = anchored;
    }

    public void ApplyRemoteLock(float lock01)
    {
        _lockProgress = Mathf.Clamp01(lock01);
        if (aimLockFill)
            aimLockFill.fillAmount = _lockProgress;
        if (_currentTarget == null && allSheep != null)
        {
            for (int i = 0; i < allSheep.Length; i++)
            {
                if (allSheep[i] != null && allSheep[i].isWolf)
                {
                    _currentTarget = allSheep[i];
                    break;
                }
            }
        }
    }

    public void FireAtPoint(Vector2 anchored)
    {
        if (_cooldownLeft > 0f || allSheep == null)
            return;
        if (MatchSession.Playing && !MatchSession.Instance.AcceptsFire)
            return;

        SheepStatus best = null;
        float bestD = 130f;
        for (int i = 0; i < allSheep.Length; i++)
        {
            SheepStatus sheep = allSheep[i];
            if (sheep == null || !sheep.alive || sheep.rect == null)
                continue;
            float d = Vector2.Distance(anchored, sheep.rect.anchoredPosition);
            if (d < bestD)
            {
                bestD = d;
                best = sheep;
            }
        }
        if (best == null)
            return;
        _currentTarget = best;
        Shoot(best);
    }

    private void PresentAimVisuals()
    {
        Vector3 worldGun = ownerGun ? ownerGun.position : Vector3.zero;
        Vector3 worldAim = aimRect ? aimRect.position : Vector3.zero;

        if (ownerGun)
        {
            Vector3 dir = worldAim - worldGun;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            ownerGun.eulerAngles = new Vector3(0f, 0f, angle);
        }

        if (ownerAlertMark && ownerAlertMark.activeSelf != (_lockProgress > 0f))
            ownerAlertMark.SetActive(_lockProgress > 0f);

        if (laserVisual != null)
        {
            Vector3 muzzlePos = (ownerMuzzleFlash && ownerMuzzleFlash.transform != null)
                ? ownerMuzzleFlash.transform.position
                : worldGun;
            laserVisual.UpdateLaser(aimRect != null, _lockProgress, muzzlePos, worldAim);
        }

        bool targetIsWolf = _currentTarget != null && _currentTarget.isWolf;
        float dist = (_currentTarget != null && aimRect != null && _currentTarget.rect != null)
            ? Vector2.Distance(aimRect.anchoredPosition, _currentTarget.rect.anchoredPosition)
            : 999f;
        bool isCloseNow = dist < closeDistance;
        bool showWarning = targetIsWolf && _currentTarget != null &&
                           (_lockProgress > 0f || (isCloseNow && _currentTarget.suspicion >= lockThreshold));
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

    bool GunIsOn(SheepStatus sheep)
    {
        if (sheep == null || sheep.rect == null) return false;
        if (aimRect == null) return true;
        float reach = Mathf.Max(closeDistance, 80f);
        return Vector2.Distance(aimRect.anchoredPosition, sheep.rect.anchoredPosition) <= reach;
    }

    private SheepStatus FindFullSuspicionSheep()
    {
        if (allSheep == null) return null;

        SheepStatus found = null;
        for (int i = 0; i < allSheep.Length; i++)
        {
            SheepStatus sheep = allSheep[i];
            if (sheep == null || !sheep.alive || !sheep.gameObject.activeSelf) continue;
            if (sheep.suspicion < 100f) continue;

            if (found == null || sheep.isWolf || (!found.isWolf && sheep.suspicion > found.suspicion))
                found = sheep;
        }
        return found;
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
        _alertSoundPlayed = false;
        if (aimLockFill) aimLockFill.fillAmount = 0f;
        _cooldownLeft = shotCooldown;

        // Gun_Shot fires the moment the owner shoots
        if (SoundLibrary.Instance != null)
            SoundLibrary.Instance.PlaySFX("Gun_Shot");

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

    public void ReplayShot(SheepStatus target)
    {
        if (target == null || !target.alive) return;

        if (SoundLibrary.Instance != null)
            SoundLibrary.Instance.PlaySFX("Gun_Shot");
        if (ownerMuzzleFlash)
        {
            ownerMuzzleFlash.SetActive(true);
            _muzzleFlashTimer = 0.1f;
        }

        if (fxPoofs != null && fxPoofs.Length > 0)
        {
            int slot = _poofIndex % fxPoofs.Length;
            _poofIndex++;
            GameObject poof = fxPoofs[slot];
            if (poof)
            {
                RectTransform poofRect = (_poofRects != null && slot < _poofRects.Length) ? _poofRects[slot] : poof.GetComponent<RectTransform>();
                if (poofRect != null && target.rect != null)
                    poofRect.anchoredPosition = target.rect.anchoredPosition;
                poof.SetActive(false);
                poof.SetActive(true);
                StartCoroutine(HidePoofAfter(poof, 0.5f));
            }
        }

        target.ApplyBurnedEffect();
        if (target.isWolf && flashShot)
        {
            flashShot.SetActive(true);
            StartCoroutine(HideFlashShotAfter(0.15f));
        }
        else if (!target.diesForGood && SoundLibrary.Instance != null)
        {
            SoundLibrary.Instance.PlaySFX("Wrong_Kill");
        }
    }

    private void ApplyHitToTarget(SheepStatus target)
    {
        if (target == null) return;

        // Play Poof VFX
        if (fxPoofs != null && fxPoofs.Length > 0)
        {
            int slot = _poofIndex % fxPoofs.Length;
            _poofIndex++;
            GameObject poof = fxPoofs[slot];
            if (poof)
            {
                RectTransform poofRect = (_poofRects != null && slot < _poofRects.Length) ? _poofRects[slot] : poof.GetComponent<RectTransform>();
                if (poofRect != null)
                    poofRect.anchoredPosition = target.rect.anchoredPosition;
                poof.SetActive(false);
                poof.SetActive(true);
                StartCoroutine(HidePoofAfter(poof, 0.5f));
            }
        }

        if (MatchSession.Playing)
            MatchSession.Instance.PrepareHit(target);

        // Apply burned effect
        target.ApplyBurnedEffect();

        if (MatchSession.Playing)
        {
            MatchSession.Instance.AuthorityHit(target);
            return;
        }

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
        else
        {
            // Wrong_Kill: the owner shot an innocent sheep
            if (SoundLibrary.Instance != null)
                SoundLibrary.Instance.PlaySFX("Wrong_Kill");
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