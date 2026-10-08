using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class LaserAimVisual : MonoBehaviour
{
    [Header("Laser Line Settings")]
    public float baseLaserThickness = 3f;
    public float outerGlowThickness = 10f;
    public Color laserCoreColor = new Color(1f, 0.4f, 0.45f, 0.95f);
    public Color laserGlowColor = new Color(1f, 0.08f, 0.15f, 0.45f);
    public Color laserDotColor = new Color(1f, 0.2f, 0.25f, 0.9f);

    [Header("Fire Effect Settings")]
    public float fireBeamDuration = 0.35f;
    public float bulletSpeed = 4500f; // Pixels per second

    private RectTransform _canvasRect;
    public RectTransform CanvasRect
    {
        get
        {
            if (_canvasRect == null)
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    _canvasRect = canvas.GetComponent<RectTransform>();
                }
                else
                {
                    var dyn = GameObject.Find("Canvas_Dynamic");
                    if (dyn != null) _canvasRect = dyn.GetComponent<RectTransform>();
                }
            }
            return _canvasRect;
        }
    }

    private GameObject _laserRoot;
    private RectTransform _laserLineRect;
    private Image _laserOuterGlow;
    private Image _laserInnerCore;
    private Image _laserDot;

    // Fire line visual
    private GameObject _fireRoot;
    private RectTransform _fireLineRect;
    private Image _fireOuterGlow;
    private Image _fireMidGlow;
    private Image _fireCoreLine;

    // Fire bullet projectile
    private GameObject _bulletObj;
    private RectTransform _bulletRect;

    // Fire impact cluster
    private GameObject _impactRoot;
    private RectTransform _impactRect;
    private Image[] _impactPuffs;

    void Awake()
    {
        BuildVisuals();
    }

    private static Sprite FindBuiltinSprite(string name)
    {
        var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] != null && sprites[i].name == name) return sprites[i];
        }
        return null;
    }

    private void BuildVisuals()
    {
        // Clean up any previously created children to avoid duplicates
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }

        Sprite knobSprite = FindBuiltinSprite("Knob");
        Sprite uiSprite = FindBuiltinSprite("UISprite");

        // 1. Laser Beam Root
        _laserRoot = new GameObject("Laser_Beam", typeof(RectTransform));
        _laserRoot.transform.SetParent(transform, false);
        _laserLineRect = _laserRoot.GetComponent<RectTransform>();
        _laserLineRect.pivot = new Vector2(0f, 0.5f);
        _laserLineRect.anchorMin = new Vector2(0.5f, 0.5f);
        _laserLineRect.anchorMax = new Vector2(0.5f, 0.5f);

        // Outer glow
        GameObject outerObj = new GameObject("Laser_OuterGlow", typeof(RectTransform), typeof(Image));
        outerObj.transform.SetParent(_laserRoot.transform, false);
        RectTransform outerRect = outerObj.GetComponent<RectTransform>();
        outerRect.anchorMin = new Vector2(0f, 0f);
        outerRect.anchorMax = new Vector2(1f, 1f);
        outerRect.offsetMin = new Vector2(0f, -outerGlowThickness * 0.5f);
        outerRect.offsetMax = new Vector2(0f, outerGlowThickness * 0.5f);
        _laserOuterGlow = outerObj.GetComponent<Image>();
        _laserOuterGlow.sprite = uiSprite;
        _laserOuterGlow.color = laserGlowColor;
        _laserOuterGlow.raycastTarget = false;

        // Inner core
        GameObject coreObj = new GameObject("Laser_InnerCore", typeof(RectTransform), typeof(Image));
        coreObj.transform.SetParent(_laserRoot.transform, false);
        RectTransform coreRect = coreObj.GetComponent<RectTransform>();
        coreRect.anchorMin = new Vector2(0f, 0.5f);
        coreRect.anchorMax = new Vector2(1f, 0.5f);
        coreRect.sizeDelta = new Vector2(0f, baseLaserThickness);
        _laserInnerCore = coreObj.GetComponent<Image>();
        _laserInnerCore.sprite = uiSprite;
        _laserInnerCore.color = laserCoreColor;
        _laserInnerCore.raycastTarget = false;

        // Laser dot at target end
        GameObject dotObj = new GameObject("Laser_Dot", typeof(RectTransform), typeof(Image));
        dotObj.transform.SetParent(_laserRoot.transform, false);
        RectTransform dotRect = dotObj.GetComponent<RectTransform>();
        dotRect.anchorMin = new Vector2(1f, 0.5f);
        dotRect.anchorMax = new Vector2(1f, 0.5f);
        dotRect.pivot = new Vector2(0.5f, 0.5f);
        dotRect.sizeDelta = new Vector2(14f, 14f);
        _laserDot = dotObj.GetComponent<Image>();
        _laserDot.sprite = knobSprite != null ? knobSprite : uiSprite;
        _laserDot.color = laserDotColor;
        _laserDot.raycastTarget = false;

        _laserRoot.SetActive(false);

        // 2. Fire Line Flash
        _fireRoot = new GameObject("Fire_Beam_Flash", typeof(RectTransform));
        _fireRoot.transform.SetParent(transform, false);
        _fireLineRect = _fireRoot.GetComponent<RectTransform>();
        _fireLineRect.pivot = new Vector2(0f, 0.5f);
        _fireLineRect.anchorMin = new Vector2(0.5f, 0.5f);
        _fireLineRect.anchorMax = new Vector2(0.5f, 0.5f);

        // Fire Outer Flame
        GameObject fOuter = new GameObject("Fire_Outer", typeof(RectTransform), typeof(Image));
        fOuter.transform.SetParent(_fireRoot.transform, false);
        RectTransform fOuterRect = fOuter.GetComponent<RectTransform>();
        fOuterRect.anchorMin = new Vector2(0f, 0.5f);
        fOuterRect.anchorMax = new Vector2(1f, 0.5f);
        fOuterRect.sizeDelta = new Vector2(0f, 26f);
        _fireOuterGlow = fOuter.GetComponent<Image>();
        _fireOuterGlow.sprite = uiSprite;
        _fireOuterGlow.color = new Color(1f, 0.28f, 0f, 0.85f);
        _fireOuterGlow.raycastTarget = false;

        // Fire Mid Flame
        GameObject fMid = new GameObject("Fire_Mid", typeof(RectTransform), typeof(Image));
        fMid.transform.SetParent(_fireRoot.transform, false);
        RectTransform fMidRect = fMid.GetComponent<RectTransform>();
        fMidRect.anchorMin = new Vector2(0f, 0.5f);
        fMidRect.anchorMax = new Vector2(1f, 0.5f);
        fMidRect.sizeDelta = new Vector2(0f, 14f);
        _fireMidGlow = fMid.GetComponent<Image>();
        _fireMidGlow.sprite = uiSprite;
        _fireMidGlow.color = new Color(1f, 0.75f, 0.05f, 0.95f);
        _fireMidGlow.raycastTarget = false;

        // Fire Core
        GameObject fCore = new GameObject("Fire_Core", typeof(RectTransform), typeof(Image));
        fCore.transform.SetParent(_fireRoot.transform, false);
        RectTransform fCoreRect = fCore.GetComponent<RectTransform>();
        fCoreRect.anchorMin = new Vector2(0f, 0.5f);
        fCoreRect.anchorMax = new Vector2(1f, 0.5f);
        fCoreRect.sizeDelta = new Vector2(0f, 5f);
        _fireCoreLine = fCore.GetComponent<Image>();
        _fireCoreLine.sprite = uiSprite;
        _fireCoreLine.color = new Color(1f, 1f, 0.9f, 1f);
        _fireCoreLine.raycastTarget = false;

        _fireRoot.SetActive(false);

        // 3. Fire Bullet Projectile
        _bulletObj = new GameObject("Fire_Bullet", typeof(RectTransform), typeof(Image));
        _bulletObj.transform.SetParent(transform, false);
        _bulletRect = _bulletObj.GetComponent<RectTransform>();
        _bulletRect.pivot = new Vector2(0.5f, 0.5f);
        _bulletRect.sizeDelta = new Vector2(28f, 18f);
        Image bImg = _bulletObj.GetComponent<Image>();
        bImg.sprite = knobSprite != null ? knobSprite : uiSprite;
        bImg.color = new Color(1f, 0.9f, 0.3f, 1f);
        bImg.raycastTarget = false;

        // Trailing tail inside bullet
        GameObject bTail = new GameObject("Bullet_Tail", typeof(RectTransform), typeof(Image));
        bTail.transform.SetParent(_bulletObj.transform, false);
        RectTransform btRect = bTail.GetComponent<RectTransform>();
        btRect.anchorMin = new Vector2(0f, 0.5f);
        btRect.anchorMax = new Vector2(0f, 0.5f);
        btRect.pivot = new Vector2(1f, 0.5f);
        btRect.anchoredPosition = Vector2.zero;
        btRect.sizeDelta = new Vector2(40f, 14f);
        Image btImg = bTail.GetComponent<Image>();
        btImg.sprite = uiSprite;
        btImg.color = new Color(1f, 0.4f, 0f, 0.85f);
        btImg.raycastTarget = false;

        _bulletObj.SetActive(false);

        // 4. Impact Fire Burst
        _impactRoot = new GameObject("Fire_Impact_Burst", typeof(RectTransform));
        _impactRoot.transform.SetParent(transform, false);
        _impactRect = _impactRoot.GetComponent<RectTransform>();
        _impactRect.pivot = new Vector2(0.5f, 0.5f);
        _impactRect.sizeDelta = new Vector2(80f, 80f);

        int puffCount = 5;
        _impactPuffs = new Image[puffCount];
        for (int i = 0; i < puffCount; i++)
        {
            GameObject puff = new GameObject("FlamePuff_" + i, typeof(RectTransform), typeof(Image));
            puff.transform.SetParent(_impactRoot.transform, false);
            RectTransform pRect = puff.GetComponent<RectTransform>();
            pRect.pivot = new Vector2(0.5f, 0.5f);
            pRect.anchoredPosition = Vector2.zero;
            pRect.sizeDelta = new Vector2(40f, 40f);
            Image pImg = puff.GetComponent<Image>();
            pImg.sprite = knobSprite != null ? knobSprite : uiSprite;
            pImg.color = new Color(1f, 0.5f, 0f, 0.9f);
            pImg.raycastTarget = false;
            _impactPuffs[i] = pImg;
        }

        _impactRoot.SetActive(false);
    }

    public void SetLaserVisible(bool visible)
    {
        if (_laserRoot != null && _laserRoot.activeSelf != visible)
        {
            _laserRoot.SetActive(visible);
        }
    }

    public void UpdateLaser(bool visible, float lockProgress, Vector3 startWorld, Vector3 endWorld)
    {
        if (!visible || CanvasRect == null)
        {
            SetLaserVisible(false);
            return;
        }

        SetLaserVisible(true);

        Vector2 localStart = CanvasRect.InverseTransformPoint(startWorld);
        Vector2 localEnd = CanvasRect.InverseTransformPoint(endWorld);
        Vector2 diff = localEnd - localStart;
        float distance = diff.magnitude;
        float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

        _laserLineRect.anchoredPosition = localStart;
        _laserLineRect.sizeDelta = new Vector2(distance, baseLaserThickness);
        _laserLineRect.localEulerAngles = new Vector3(0f, 0f, angle);

        // Dynamic pulsing and lock-on excitation
        float pulse = Mathf.PingPong(Time.time * 8f, 0.25f);
        float currentAlpha = Mathf.Lerp(0.35f, 0.85f, lockProgress) + pulse;
        if (_laserOuterGlow != null)
        {
            Color c = laserGlowColor;
            c.a = Mathf.Clamp01(currentAlpha);
            _laserOuterGlow.color = c;
        }

        if (_laserDot != null)
        {
            float dotScale = 1f + lockProgress * 0.6f + pulse * 0.3f;
            _laserDot.rectTransform.localScale = new Vector3(dotScale, dotScale, 1f);
        }
    }

    public void PlayFireShotAlongLine(Vector3 startWorld, Vector3 endWorld, System.Action onImpact)
    {
        StartCoroutine(FireShotRoutine(startWorld, endWorld, onImpact));
    }

    private IEnumerator FireShotRoutine(Vector3 startWorld, Vector3 endWorld, System.Action onImpact)
    {
        if (CanvasRect == null)
        {
            onImpact?.Invoke();
            yield break;
        }

        Vector2 localStart = CanvasRect.InverseTransformPoint(startWorld);
        Vector2 localEnd = CanvasRect.InverseTransformPoint(endWorld);
        Vector2 diff = localEnd - localStart;
        float distance = diff.magnitude;
        float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

        // Position and show fire beam
        _fireLineRect.anchoredPosition = localStart;
        _fireLineRect.sizeDelta = new Vector2(distance, 26f);
        _fireLineRect.localEulerAngles = new Vector3(0f, 0f, angle);
        _fireRoot.SetActive(true);

        // Position fire bullet
        _bulletRect.localEulerAngles = new Vector3(0f, 0f, angle);
        _bulletObj.SetActive(true);

        // Animate bullet traveling from start to end
        float travelDuration = Mathf.Clamp(distance / bulletSpeed, 0.06f, 0.12f);
        float elapsed = 0f;

        while (elapsed < travelDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / travelDuration);
            _bulletRect.anchoredPosition = Vector2.Lerp(localStart, localEnd, t);
            yield return null;
        }

        _bulletRect.anchoredPosition = localEnd;
        _bulletObj.SetActive(false);

        // IMPACT! Call onImpact right when bullet hits
        onImpact?.Invoke();

        // Spawn Impact Fire Burst
        StartCoroutine(ImpactBurstRoutine(localEnd));

        // Fire Beam dissipate routine
        float beamElapsed = 0f;
        while (beamElapsed < fireBeamDuration)
        {
            beamElapsed += Time.deltaTime;
            float t = beamElapsed / fireBeamDuration;

            // Flame flicker
            float jitter = Random.Range(-2f, 2f);
            float widthT = Mathf.Lerp(26f, 4f, t) + jitter;
            _fireLineRect.sizeDelta = new Vector2(distance, Mathf.Max(2f, widthT));

            // Fade alphas
            float alpha = Mathf.Clamp01(1f - t);
            if (_fireOuterGlow)
            {
                Color c = _fireOuterGlow.color;
                c.a = 0.85f * alpha;
                _fireOuterGlow.color = c;
            }
            if (_fireMidGlow)
            {
                Color c = _fireMidGlow.color;
                c.a = 0.95f * alpha;
                _fireMidGlow.color = c;
            }
            if (_fireCoreLine)
            {
                Color c = _fireCoreLine.color;
                c.a = alpha;
                _fireCoreLine.color = c;
            }

            yield return null;
        }

        _fireRoot.SetActive(false);
    }

    private IEnumerator ImpactBurstRoutine(Vector2 impactPos)
    {
        _impactRect.anchoredPosition = impactPos;
        _impactRoot.SetActive(true);

        Vector2[] dirs = new Vector2[]
        {
            new Vector2(-1f, 0.5f).normalized,
            new Vector2(1f, 0.6f).normalized,
            new Vector2(0f, 1f),
            new Vector2(-0.7f, -0.7f).normalized,
            new Vector2(0.8f, -0.5f).normalized
        };

        float duration = 0.45f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            for (int i = 0; i < _impactPuffs.Length; i++)
            {
                if (_impactPuffs[i] == null) continue;
                RectTransform pr = _impactPuffs[i].rectTransform;
                float dist = Mathf.Lerp(5f, 45f, Mathf.Sqrt(t));
                pr.anchoredPosition = dirs[i % dirs.Length] * dist;

                float scale = Mathf.Lerp(1.2f, 0.2f, t);
                pr.localScale = new Vector3(scale, scale, 1f);

                // Transition from fiery orange to smoke dark
                Color fireCol = Color.Lerp(new Color(1f, 0.7f, 0.1f, 1f), new Color(0.18f, 0.18f, 0.18f, 0.7f), t);
                fireCol.a *= (1f - t);
                _impactPuffs[i].color = fireCol;
            }

            yield return null;
        }

        _impactRoot.SetActive(false);
    }
}
