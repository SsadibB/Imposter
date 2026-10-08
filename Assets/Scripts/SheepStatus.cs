using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SheepStatus : MonoBehaviour
{
    public bool alive = true;
    public bool isWolf = false;
    public bool isBad = false;
    public bool isBurned = false;
    public float suspicion = 0f; // 0..100
    public Vector2 velocity = Vector2.zero;
    public RectTransform rect;
    public float badRate = 20f;
    public float goodDecay = 4f;

    [Header("Burn Recovery (normal sheep only)")]
    [Tooltip("Base seconds a normal sheep stays charred before recovering. Actual time is randomised ±0.5 s around this value, clamped 3–5 s.")]
    [Range(1f, 10f)]
    public float recoverDuration = 4f;

    // Cache original colors for clean reset
    private Dictionary<Image, Color> _origColors = new Dictionary<Image, Color>();
    private Transform _visualTransform;
    private Vector3 _origVisualLocalPos;
    private Quaternion _origVisualLocalRot;
    private bool _cachedInitial = false;
    private Coroutine _burnRecoverRoutine;

    void Awake()
    {
        if (rect == null)
            rect = GetComponent<RectTransform>();

        CacheOriginalVisuals();
    }

    private void CacheOriginalVisuals()
    {
        if (_cachedInitial) return;
        _visualTransform = transform.Find("Visual");
        if (_visualTransform != null)
        {
            _origVisualLocalPos = _visualTransform.localPosition;
            _origVisualLocalRot = _visualTransform.localRotation;
            Image[] images = _visualTransform.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                if (img != null && !_origColors.ContainsKey(img))
                {
                    _origColors[img] = img.color;
                }
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

        // Pause AI/suspicion ticking while burned
        alive = false;

        CacheOriginalVisuals();

        // 1. Turn all visual parts to charred black
        if (_visualTransform != null)
        {
            Image[] images = _visualTransform.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                if (img == null) continue;

                string n = img.gameObject.name;
                if (n.Contains("Shadow"))
                {
                    img.color = new Color(0f, 0f, 0f, 0.45f);
                }
                else if (n.Contains("Head"))
                {
                    img.color = new Color(0.06f, 0.06f, 0.06f, 1f);
                }
                else if (n.Contains("Eye"))
                {
                    img.color = new Color(0.35f, 0.35f, 0.35f, 1f);
                }
                else
                {
                    img.color = new Color(0.12f, 0.12f, 0.12f, 1f);
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
        StartCoroutine(SmokeWispsRoutine());

        // 6. Recovery: wolf death is permanent; normal sheep recover after 3–5 s
        if (!isWolf)
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
        var charredColors = new Dictionary<Image, Color>();
        if (_visualTransform != null)
        {
            Image[] imgs = _visualTransform.GetComponentsInChildren<Image>(true);
            foreach (var img in imgs)
            {
                if (img != null) charredColors[img] = img.color;
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

            if (_visualTransform != null)
            {
                Image[] imgs = _visualTransform.GetComponentsInChildren<Image>(true);
                foreach (var img in imgs)
                {
                    if (img == null) continue;
                    Color from = charredColors.TryGetValue(img, out Color c) ? c : img.color;
                    Color to = _origColors.TryGetValue(img, out Color orig) ? orig : img.color;
                    img.color = Color.Lerp(from, to, t);
                }

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
        Sprite knobSprite = null;
        var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
        for (int s = 0; s < allSprites.Length; s++)
        {
            if (allSprites[s] != null && allSprites[s].name == "Knob")
            {
                knobSprite = allSprites[s];
                break;
            }
        }
        List<GameObject> smokePuffs = new List<GameObject>();

        for (int i = 0; i < 3; i++)
        {
            GameObject puff = new GameObject("BurnSmoke_" + i, typeof(RectTransform), typeof(Image));
            puff.transform.SetParent(transform, false);
            RectTransform pr = puff.GetComponent<RectTransform>();
            pr.pivot = new Vector2(0.5f, 0.5f);
            pr.anchoredPosition = new Vector2(Random.Range(-20f, 20f), Random.Range(5f, 25f));
            pr.sizeDelta = new Vector2(24f, 24f);

            Image img = puff.GetComponent<Image>();
            img.sprite = knobSprite;
            img.color = new Color(0.2f, 0.2f, 0.2f, 0.7f);
            img.raycastTarget = false;
            smokePuffs.Add(puff);
        }

        float duration = 1.8f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            for (int i = 0; i < smokePuffs.Count; i++)
            {
                if (smokePuffs[i] == null) continue;
                RectTransform pr = smokePuffs[i].GetComponent<RectTransform>();
                pr.anchoredPosition += new Vector2(Random.Range(-4f, 4f) * Time.deltaTime, 35f * Time.deltaTime);
                float scale = Mathf.Lerp(0.8f, 2.2f, t);
                pr.localScale = new Vector3(scale, scale, 1f);

                Image img = smokePuffs[i].GetComponent<Image>();
                img.color = new Color(0.18f, 0.18f, 0.18f, Mathf.Lerp(0.7f, 0f, t));
            }

            yield return null;
        }

        foreach (var p in smokePuffs)
        {
            if (p != null) Destroy(p);
        }
    }

    public void ResetStatus()
    {
        alive = true;
        isBad = false;
        isBurned = false;
        suspicion = 0f;
        velocity = Vector2.zero;

        if (_visualTransform != null)
        {
            _visualTransform.localPosition = _origVisualLocalPos;
            _visualTransform.localRotation = _origVisualLocalRot;

            foreach (var kvp in _origColors)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.color = kvp.Value;
                }
            }
        }
    }
}
