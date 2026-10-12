using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One depth sort for the whole flock. Each sheep used to reorder itself, which rebuilt the canvas over and over.
/// </summary>
public static class FlockDepthSort
{
    const float Interval = 0.12f;

    static readonly List<RectTransform> Sheep = new List<RectTransform>(12);
    static float _timer = 0.05f;
    static int _lastFrame = -1;

    public static void Register(RectTransform rect)
    {
        if (rect != null && !Sheep.Contains(rect))
            Sheep.Add(rect);
    }

    public static void Unregister(RectTransform rect)
    {
        if (rect != null)
            Sheep.Remove(rect);
    }

    public static void RequestTick(float dt)
    {
        if (_lastFrame == Time.frameCount) return;
        _lastFrame = Time.frameCount;

        _timer -= dt;
        if (_timer > 0f) return;
        _timer = Interval;
        Sort();
    }

    static void Sort()
    {
        for (int i = Sheep.Count - 1; i >= 0; i--)
        {
            if (Sheep[i] == null)
                Sheep.RemoveAt(i);
        }

        int n = Sheep.Count;
        if (n < 2) return;

        for (int i = 1; i < n; i++)
        {
            RectTransform key = Sheep[i];
            float keyY = key.anchoredPosition.y;
            int j = i - 1;
            while (j >= 0 && Sheep[j].anchoredPosition.y < keyY)
            {
                Sheep[j + 1] = Sheep[j];
                j--;
            }
            Sheep[j + 1] = key;
        }

        int prev = -1;
        for (int i = 0; i < n; i++)
        {
            int idx = Sheep[i].GetSiblingIndex();
            if (idx <= prev)
            {
                for (int k = 0; k < n; k++)
                    Sheep[k].SetAsLastSibling();
                return;
            }
            prev = idx;
        }
    }
}
