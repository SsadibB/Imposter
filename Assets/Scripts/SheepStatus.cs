using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class SheepStatus : MonoBehaviour
{
    public bool alive = true;
    public bool isWolf = false;
    public bool isBad = false;
    public bool isBurned = false;
    public bool diesForGood = false;
    public float suspicion = 0f; // 0..100
    public Vector2 velocity = Vector2.zero;
    public RectTransform rect;
    public float badRate = 20f;
    public float goodDecay = 4f;

    [Header("Burn Recovery (normal sheep only)")]
    [Tooltip("Base seconds a normal sheep stays charred before recovering. Actual time is randomised ±0.5 s around this value, clamped 3–5 s.")]
    [Range(1f, 10f)]
    public float recoverDuration = 4f;

    // Cached visual parts so burns don't search the hierarchy or allocate
    private Image[] _images;
    private Color[] _origColors;
    private Color[] _charredColors;
    private int[] _part;
    private Transform _visualTransform;
    private Vector3 _origVisualLocalPos;
    private Quaternion _origVisualLocalRot;
    private bool _cachedInitial = false;
    private Coroutine _burnRecoverRoutine;
    private Coroutine _smokeRoutine;
    private SmokePuff[] _smoke;

    const int PartWool = 0;
    const int PartShadow = 1;
    const int PartHead = 2;
    const int PartEye = 3;

    struct SmokePuff
    {
        public GameObject go;
        public RectTransform rect;
        public Image image;
    }

    void Awake()
    {
        if (rect == null)
            rect = GetComponent<RectTransform>();

        CacheOriginalVisuals();
        FlockDepthSort.Register(rect);
    }

    void OnDestroy()
    {
        FlockDepthSort.Unregister(rect);
    }

    private void CacheOriginalVisuals()
    {
        if (_cachedInitial) return;
        _visualTransform = transform.Find("Visual");
        if (_visualTransform != null)
        {
            _origVisualLocalPos = _visualTransform.localPosition;
            _origVisualLocalRot = _visualTransform.localRotation;
            _images = _visualTransform.GetComponentsInChildren<Image>(true);
            int n = _images.Length;
            _origColors = new Color[n];
            _charredColors = new Color[n];
            _part = new int[n];
            for (int i = 0; i < n; i++)
            {
                Image img = _images[i];
                if (img == null) continue;
                _origColors[i] = img.color;
                string name = img.gameObject.name;
                if (name.IndexOf("Shadow") >= 0) _part[i] = PartShadow;
                else if (name.IndexOf("Head") >= 0) _part[i] = PartHead;
                else if (name.IndexOf("Eye") >= 0) _part[i] = PartEye;
                else _part[i] = PartWool;
            }
        }
        _cachedInitial = true;
    }

    public void Tick(float dt)
    {
        if (!alive) return;
        suspicion = Mathf.Clamp(suspicion + (isBad ? badRate : -goodDecay) * dt, 0f, 100f);
    }

    public void ApplyBurnedEffect()
    {
        // Cancel any in-progress recovery first
        if (_burnRecoverRoutine != null)
        {
            StopCoroutine(_burnRecoverRoutine);
            _burnRecoverRoutine = null;
        }

        isBurned = true;
        isBad = false;
        velocity = Vector2.zero;

        // Sheep_Burn plays when the shot lands
        if (SoundLibrary.Instance != null)
            SoundLibrary.Instance.PlaySFX("Sheep_Burn");

        // Pause AI/suspicion ticking while burned
        alive = false;

        CacheOriginalVisuals();

        // 1. Turn all visual parts to charred black
        if (_images != null)
        {
            for (int i = 0; i < _images.Length; i++)
            {
                Image img = _images[i];
                if (img == null) continue;

                switch (_part[i])
                {
                    case PartShadow:
                        img.color = new Color(0f, 0f, 0f, 0.45f);
                        break;
                    case PartHead:
                        img.color = new Color(0.06f, 0.06f, 0.06f, 1f);
                        break;
                    case PartEye:
                        img.color = new Color(0.35f, 0.35f, 0.35f, 1f);
                        break;
                    default:
                        img.color = new Color(0.12f, 0.12f, 0.12f, 1f);
                        break;
                }
            }

            // 2. Knockout tilt / collapse posture
            _visualTransform.localEulerAngles = new Vector3(0f, 0f, 18f);
            _visualTransform.localPosition = new Vector3(_origVisualLocalPos.x, _origVisualLocalPos.y - 8f, 0f);
        }

        // 3. Hide non-burned badges and effects
        var badMark = transform.Find("BadMark");
        if (badMark) badMark.gameObject.SetActive(false);

        var bleatBubble = transform.Find("BleatBubble");
        if (bleatBubble) bleatBubble.gameObject.SetActive(false);

        if (_visualTransform != null)
        {
            var graze = _visualTransform.Find("GrazeFX");
            if (graze) graze.gameObject.SetActive(false);
        }

        // 4. Pause AI command handling
        var ai = GetComponent<SheepAI>();
        if (ai != null) ai.EndCommand();

        var wolf = GetComponent<WolfController>();
        if (wolf != null) wolf.EndCommand();

        // 5. Rising smoke wisps
        if (_smokeRoutine != null)
            StopCoroutine(_smokeRoutine);
        _smokeRoutine = StartCoroutine(SmokeWispsRoutine());

        // 6. Recovery: wolf death is permanent; normal sheep recover after 3–5 s
        if (!isWolf && !diesForGood)
        {
            float burnTime = Mathf.Clamp(Random.Range(recoverDuration - 0.5f, recoverDuration + 0.5f), 3f, 5f);
            _burnRecoverRoutine = StartCoroutine(BurnRecoverRoutine(burnTime));
        }
    }

    /// <summary>Waits burnSeconds then fades the sheep back to its original appearance.</summary>
    private IEnumerator BurnRecoverRoutine(float burnSeconds)
    {
        yield return new WaitForSeconds(burnSeconds);

        // Snapshot charred colors for lerp
        if (_images != null)
        {
            for (int i = 0; i < _images.Length; i++)
            {
                if (_images[i] != null)
                    _charredColors[i] = _images[i].color;
            }
        }

        Vector3 charredEuler = new Vector3(0f, 0f, 18f);
        Vector3 charredPos = new Vector3(_origVisualLocalPos.x, _origVisualLocalPos.y - 8f, 0f);

        // Fade back to normal over 0.5 s
        float fadeDuration = 0.5f;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);

            if (_images != null)
            {
                for (int i = 0; i < _images.Length; i++)
                {
                    Image img = _images[i];
                    if (img == null) continue;
                    img.color = Color.Lerp(_charredColors[i], _origColors[i], t);
                }
            }

            if (_visualTransform != null)
            {
                _visualTransform.localEulerAngles = Vector3.Lerp(charredEuler, Vector3.zero, t);
                _visualTransform.localPosition = Vector3.Lerp(charredPos, _origVisualLocalPos, t);
            }

            yield return null;
        }

        // Full state reset — sheep rejoins the flock
        ResetStatus();
        _burnRecoverRoutine = null;
    }


    private IEnumerator SmokeWispsRoutine()
    {
        EnsureSmoke();
        for (int i = 0; i < _smoke.Length; i++)
        {
            SmokePuff puff = _smoke[i];
            puff.go.SetActive(true);
            puff.rect.anchoredPosition = new Vector2(Random.Range(-20f, 20f), Random.Range(5f, 25f));
            puff.rect.localScale = Vector3.one;
            puff.image.color = new Color(0.2f, 0.2f, 0.2f, 0.7f);
        }

        float duration = 1.8f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float alpha = Mathf.Lerp(0.7f, 0f, t);
            float scale = Mathf.Lerp(0.8f, 2.2f, t);

            for (int i = 0; i < _smoke.Length; i++)
            {
                SmokePuff puff = _smoke[i];
                if (puff.go == null) continue;
                puff.rect.anchoredPosition += new Vector2(Random.Range(-4f, 4f) * Time.deltaTime, 35f * Time.deltaTime);
                puff.rect.localScale = new Vector3(scale, scale, 1f);
                puff.image.color = new Color(0.18f, 0.18f, 0.18f, alpha);
            }

            yield return null;
        }

        for (int i = 0; i < _smoke.Length; i++)
        {
            if (_smoke[i].go != null)
                _smoke[i].go.SetActive(false);
        }
        _smokeRoutine = null;
    }

    private void EnsureSmoke()
    {
        if (_smoke != null) return;
        _smoke = new SmokePuff[3];
        Sprite sprite = RuntimeSprites.Circle;
        for (int i = 0; i < _smoke.Length; i++)
        {
            GameObject puff = new GameObject("BurnSmoke", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            puff.transform.SetParent(transform, false);
            RectTransform pr = puff.GetComponent<RectTransform>();
            pr.pivot = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(24f, 24f);
            Image img = puff.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            puff.SetActive(false);
            _smoke[i] = new SmokePuff { go = puff, rect = pr, image = img };
        }
    }

    public void ResetStatus()
    {
        alive = true;
        isBad = false;
        isBurned = false;
        diesForGood = false;
        suspicion = 0f;
        velocity = Vector2.zero;

        if (_visualTransform != null)
        {
            _visualTransform.localPosition = _origVisualLocalPos;
            _visualTransform.localRotation = _origVisualLocalRot;

            if (_images != null)
            {
                for (int i = 0; i < _images.Length; i++)
                {
                    if (_images[i] != null)
                        _images[i].color = _origColors[i];
                }
            }
        }
    }
}