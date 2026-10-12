using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A shield that pops up at a random spot in the field.
/// Only the imposter sheep can take it, and doing so cuts suspicion.
/// </summary>
public class ShieldPickup : MonoBehaviour
{
    [Header("References")]
    public RectTransform flockBounds;
    public SheepStatus imposter;
    public HUDController hudController;

    [Header("Pickup")]
    [Tooltip("Suspicion points removed when the imposter sheep takes the shield. This subtracts from the current bar and does not clear it.")]
    public float suspicionReduction = 25f;

    [Tooltip("How close the imposter must be, in field units, to take the shield.")]
    public float pickupRadius = 70f;

    [Header("Random Popup")]
    [Tooltip("Shortest wait, in seconds, before the shield pops up again.")]
    public float minRespawnDelay = 4f;

    [Tooltip("Longest wait, in seconds, before the shield pops up again.")]
    public float maxRespawnDelay = 9f;

    enum Phase { Waiting, PopIn, Shown, PopOut }

    RectTransform _root;
    Image _glow;
    Image _icon;
    Vector2 _basePos;
    Phase _phase = Phase.Waiting;
    float _timer;
    float _bob;
    bool _built;

    void Awake()
    {
        if (imposter == null) imposter = GetComponent<SheepStatus>();
        var wolf = GetComponent<WolfController>();
        if (wolf != null)
        {
            if (flockBounds == null) flockBounds = wolf.flockBounds;
            if (hudController == null) hudController = wolf.hudController;
        }
    }

    void Start()
    {
        EnsureVisual();
        HideImmediate();
        _phase = Phase.Waiting;
        _timer = NextDelay();
    }

    void Update()
    {
        if (MatchSession.Playing && !MatchSession.Instance.IsAuthority)
            return;

        EnsureVisual();
        if (_root == null) return;

        bool playing = GameManager.Instance == null || GameManager.Instance.IsPlaying();
        if (!playing || imposter == null || !imposter.alive)
        {
            if (_phase != Phase.Waiting)
            {
                HideImmediate();
                _phase = Phase.Waiting;
                _timer = NextDelay();
            }
            return;
        }

        float dt = Time.deltaTime;
        switch (_phase)
        {
            case Phase.Waiting:
                _timer -= dt;
                if (_timer <= 0f)
                    BeginPopup();
                break;

            case Phase.PopIn:
                _timer += dt;
                float t = Mathf.Clamp01(_timer / 0.28f);
                float s = EaseOutBack(t);
                _root.localScale = new Vector3(s, s, 1f);
                if (t >= 1f)
                {
                    _phase = Phase.Shown;
                    _bob = 0f;
                }
                TryPickup();
                break;

            case Phase.Shown:
                _bob += dt;
                float y = Mathf.Sin(_bob * 3.1f) * 12f;
                _root.anchoredPosition = _basePos + new Vector2(0f, y);
                _root.localEulerAngles = new Vector3(0f, 0f, Mathf.Sin(_bob * 2.2f) * 8f);
                float pulse = 1f + Mathf.Sin(_bob * 4.2f) * 0.07f;
                _root.localScale = new Vector3(pulse, pulse, 1f);
                if (_glow != null)
                {
                    float a = 0.32f + Mathf.Sin(_bob * 4.2f) * 0.14f;
                    _glow.color = new Color(0.45f, 0.9f, 1f, a);
                }
                TryPickup();
                break;

            case Phase.PopOut:
                _timer += dt;
                float u = Mathf.Clamp01(_timer / 0.2f);
                float sc = Mathf.Lerp(1.1f, 1.6f, u);
                _root.localScale = new Vector3(sc, sc, 1f);
                SetAlpha(1f - u);
                if (u >= 1f)
                {
                    HideImmediate();
                    _phase = Phase.Waiting;
                    _timer = NextDelay();
                }
                break;
        }
    }

    void BeginPopup()
    {
        _basePos = PickPoint();
        _root.anchoredPosition = _basePos;
        _root.localEulerAngles = Vector3.zero;
        _root.localScale = Vector3.zero;
        SetAlpha(1f);
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        _phase = Phase.PopIn;
        _timer = 0f;
    }

    public bool NetVisible => _root != null && _root.gameObject.activeSelf && (_phase == Phase.Shown || _phase == Phase.PopIn);
    public Vector2 NetPos => _root != null ? _root.anchoredPosition : Vector2.zero;

    public void ApplyRemoteShield(bool shown, Vector2 pos)
    {
        EnsureVisual();
        if (_root == null) return;
        bool was = _root.gameObject.activeSelf;
        if (!shown)
        {
            if (was)
                _root.gameObject.SetActive(false);
            if (was && SoundLibrary.Instance != null)
                SoundLibrary.Instance.PlaySFX("Sheild_Collect");
            return;
        }
        _root.anchoredPosition = pos;
        _root.localScale = Vector3.one;
        _root.gameObject.SetActive(true);
    }

    void TryPickup()
    {
        if (_root == null) return;
        if (MatchSession.Playing && MatchSession.Instance.Mode == MatchKind.Friends)
        {
            SheepStatus who = MatchSession.Instance.SheepNear(_root.anchoredPosition, pickupRadius);
            if (who == null) return;
            who.suspicion = Mathf.Clamp(who.suspicion - suspicionReduction, 0f, 100f);
            if (hudController != null && who == MatchSession.Instance.LocalSheep)
                hudController.UpdateSuspicion(who.suspicion);
            if (SoundLibrary.Instance != null)
                SoundLibrary.Instance.PlaySFX("Sheild_Collect");
            _phase = Phase.PopOut;
            _timer = 0f;
            return;
        }

        if (imposter == null || imposter.rect == null) return;
        float dist = Vector2.Distance(imposter.rect.anchoredPosition, _root.anchoredPosition);
        if (dist > pickupRadius) return;

        imposter.suspicion = Mathf.Clamp(imposter.suspicion - suspicionReduction, 0f, 100f);
        if (hudController != null)
            hudController.UpdateSuspicion(imposter.suspicion);

        if (SoundLibrary.Instance != null)
            SoundLibrary.Instance.PlaySFX("Sheild_Collect");

        _phase = Phase.PopOut;
        _timer = 0f;
    }

    Vector2 PickPoint()
    {
        Vector2 center = flockBounds != null ? flockBounds.anchoredPosition : Vector2.zero;
        Vector2 half = flockBounds != null ? flockBounds.sizeDelta * 0.5f : new Vector2(450f, 200f);
        Vector2 player = (imposter != null && imposter.rect != null)
            ? imposter.rect.anchoredPosition
            : center;

        for (int i = 0; i < 8; i++)
        {
            float x = Random.Range(center.x - half.x + 90f, center.x + half.x - 90f);
            float y = Random.Range(center.y - half.y + 70f, center.y + half.y - 70f);
            var p = new Vector2(x, y);
            if ((p - player).sqrMagnitude >= 220f * 220f)
                return p;
        }

        return new Vector2(
            Random.Range(center.x - half.x + 90f, center.x + half.x - 90f),
            Random.Range(center.y - half.y + 70f, center.y + half.y - 70f));
    }

    float NextDelay()
    {
        float a = Mathf.Max(0.4f, minRespawnDelay);
        float b = Mathf.Max(a, maxRespawnDelay);
        return Random.Range(a, b);
    }

    void HideImmediate()
    {
        if (_root != null)
            _root.gameObject.SetActive(false);
    }

    void SetAlpha(float a)
    {
        if (_icon != null)
        {
            Color c = _icon.color;
            c.a = a;
            _icon.color = c;
        }
        if (_glow != null)
        {
            Color c = _glow.color;
            c.a = 0.4f * a;
            _glow.color = c;
        }
    }

    void EnsureVisual()
    {
        if (_built) return;
        if (imposter == null) return;

        RectTransform layer = FieldLayer.Get(imposter.transform);
        if (layer == null) return;

        var go = new GameObject("ShieldPopup", typeof(RectTransform));
        go.transform.SetParent(layer, false);
        _root = go.GetComponent<RectTransform>();
        _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
        _root.pivot = new Vector2(0.5f, 0.5f);
        _root.sizeDelta = new Vector2(56f, 64f);

        _glow = CreateImage("Glow", _root, RuntimeSprites.Circle, new Color(0.45f, 0.9f, 1f, 0.4f), new Vector2(80f, 80f));
        _icon = CreateImage("Icon", _root, RuntimeSprites.Shield, Color.white, new Vector2(48f, 56f));

        go.SetActive(false);
        _built = true;
    }

    static Image CreateImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}

/// <summary>Draws the shield and the dash airflow above the flock and under the owner's aim.</summary>
public static class FieldLayer
{
    public static RectTransform Get(Transform sheep)
    {
        if (sheep == null || sheep.parent == null) return null;
        Transform sheepRoot = sheep.parent;
        Transform canvas = sheepRoot.parent != null ? sheepRoot.parent : sheepRoot;

        Transform existing = canvas.Find("FieldPickups");
        if (existing is RectTransform found) return found;

        var go = new GameObject("FieldPickups", typeof(RectTransform));
        go.transform.SetParent(canvas, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        go.transform.SetSiblingIndex(sheepRoot.GetSiblingIndex() + 1);
        return rt;
    }
}

public static class RuntimeSprites
{
    static Sprite _circle;
    static Sprite _shield;

    public static Sprite Circle
    {
        get
        {
            if (_circle == null) _circle = MakeCircle();
            return _circle;
        }
    }

    public static Sprite Shield
    {
        get
        {
            if (_shield == null) _shield = MakeShield();
            return _shield;
        }
    }

    static Sprite MakeCircle()
    {
        const int n = 128;
        var tex = NewTexture(n);
        var px = new Color[n * n];
        float r = n * 0.5f - 1.5f;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float dx = x - n * 0.5f + 0.5f;
                float dy = y - n * 0.5f + 0.5f;
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 1.25f);
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
        }
        return Finish(tex, px, n);
    }

    static Sprite MakeShield()
    {
        const int n = 160;
        var tex = NewTexture(n);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        {
            float v = (y + 0.5f) / n;
            float half = ShieldHalf(v);
            for (int x = 0; x < n; x++)
            {
                float u = ((x + 0.5f) / n) * 2f - 1f;
                float field = half - Mathf.Abs(u);
                if (field <= 0f)
                {
                    px[y * n + x] = Color.clear;
                    continue;
                }

                float aa = Mathf.Clamp01(field * n);
                bool rim = field < 0.055f;
                bool innerRim = !rim && field < 0.11f;
                Color col;
                if (rim)
                    col = new Color(0.99f, 0.82f, 0.28f, 1f);
                else if (innerRim)
                    col = new Color(0.08f, 0.32f, 0.62f, 1f);
                else
                    col = new Color(0.20f, 0.62f, 0.96f, 1f);

                if (!rim && !innerRim && v > 0.58f)
                    col = Color.Lerp(col, new Color(0.62f, 0.9f, 1f, 1f), (v - 0.58f) * 1.1f);

                float ax = Mathf.Abs(u);
                if (!rim && ax < 0.055f && v > 0.3f && v < 0.78f)
                    col = Color.white;
                if (!rim && Mathf.Abs(v - 0.6f) < 0.04f && ax < 0.26f)
                    col = Color.white;

                float gx = u + 0.22f;
                float gy = v - 0.74f;
                if (!rim && gx * gx + gy * gy < 0.01f)
                    col = new Color(1f, 1f, 1f, 1f);

                col.a *= aa;
                px[y * n + x] = col;
            }
        }
        return Finish(tex, px, n);
    }

    static float ShieldHalf(float v)
    {
        if (v < 0.08f) return 0f;
        if (v < 0.52f)
        {
            float t = (v - 0.08f) / 0.44f;
            return Mathf.Lerp(0.02f, 0.74f, Mathf.Sin(t * Mathf.PI * 0.5f));
        }
        float u = (v - 0.52f) / 0.48f;
        return Mathf.Lerp(0.74f, 0.62f, u);
    }

    static Texture2D NewTexture(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }

    static Sprite Finish(Texture2D tex, Color[] px, int n)
    {
        tex.SetPixels(px);
        tex.Apply(false, true);
        var sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
