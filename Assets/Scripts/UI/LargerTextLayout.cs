using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Larger Text (Accessibility) for the neon menu. The menu canvas is zoomed 1.5x or 2x, so a screen
/// laid out for a phone held sideways no longer fits; this puts the screen in a scroll view instead
/// of letting it clip. Card screens restack their side-by-side cards into one column that scrolls
/// up and down; the rest keep their layout at full size and pan in both directions.
/// Only added while Larger Text is on, so the normal menu is untouched.
/// </summary>
public class LargerTextLayout : MonoBehaviour
{
    private RectTransform viewport, content;
    private ScrollRect scroll;
    private Vector2 designSize;          // pan mode: the smallest size the screen is laid out at
    private float headerHeight;          // stack mode: room left at the top for the header
    private readonly List<RectTransform> cards = new List<RectTransform>();
    private Vector2 lastViewport = new Vector2(-1, -1);
    private float lastHeight = -1f;

    /// <summary>Lay the screen out at least this big, panning to see the rest</summary>
    public static LargerTextLayout Pan(GameObject screen, Vector2 designSize)
    {
        LargerTextLayout fit = Wrap(screen);
        fit.designSize = designSize;
        return fit;
    }

    /// <summary>Stack these cards (normally side by side) under the header and scroll up and down</summary>
    public static LargerTextLayout Stack(GameObject screen, float headerHeight, params RectTransform[] cards)
    {
        LargerTextLayout fit = Wrap(screen);
        fit.headerHeight = headerHeight;
        fit.cards.AddRange(cards);
        foreach (RectTransform card in cards)
        {
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.offsetMin = new Vector2(16f, card.offsetMin.y);
            card.offsetMax = new Vector2(-24f, card.offsetMax.y);
        }
        return fit;
    }

    private static LargerTextLayout Wrap(GameObject screen)
    {
        var content = (RectTransform)screen.transform;
        var viewport = new GameObject(screen.name + " Scroll", typeof(RectTransform)).GetComponent<RectTransform>();
        viewport.SetParent(content.parent, false);
        viewport.SetSiblingIndex(content.GetSiblingIndex());
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = viewport.offsetMax = Vector2.zero;
        viewport.gameObject.AddComponent<RectMask2D>();

        content.SetParent(viewport, false);
        content.anchorMin = content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = Vector2.zero;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 20f;

        var fit = viewport.gameObject.AddComponent<LargerTextLayout>();
        fit.viewport = viewport;
        fit.content = content;
        fit.scroll = scroll;
        return fit;
    }

    private void LateUpdate()
    {
        Vector2 view = viewport.rect.size;
        if (view.x <= 0f || view.y <= 0f) return;

        if (cards.Count == 0)
        {
            if (view == lastViewport) return;
            lastViewport = view;
            Vector2 size = new Vector2(Mathf.Max(view.x, designSize.x), Mathf.Max(view.y, designSize.y));
            content.sizeDelta = size;
            scroll.horizontal = size.x > view.x + 1f;
            scroll.vertical = size.y > view.y + 1f;
            return;
        }

        // Stack: each card as tall as its contents want, one under another
        float y = headerHeight;
        foreach (RectTransform card in cards)
        {
            if (!card.gameObject.activeSelf) continue;
            float h = Mathf.Max(LayoutUtility.GetPreferredHeight(card), 60f);
            card.sizeDelta = new Vector2(card.sizeDelta.x, h);
            card.anchoredPosition = new Vector2(card.anchoredPosition.x, -y);
            y += h + 12f;
        }
        float height = Mathf.Max(view.y, y + 2f);
        if (view == lastViewport && Mathf.Approximately(height, lastHeight)) return;
        lastViewport = view;
        lastHeight = height;
        content.sizeDelta = new Vector2(view.x, height);
        scroll.horizontal = false;
        scroll.vertical = height > view.y + 1f;
    }
}

/// <summary>
/// Larger Text: a pop-up that's only a picture to look at (tap anywhere closes it) is drawn
/// bigger, but never past the edges of the screen
/// </summary>
public class ShrinkToFit : MonoBehaviour
{
    public Vector2 size;

    private void LateUpdate()
    {
        var parent = transform.parent as RectTransform;
        if (parent == null) return;
        Vector2 room = parent.rect.size - new Vector2(16f, 16f);
        float k = Mathf.Min(1f, room.x / size.x, room.y / size.y);
        transform.localScale = new Vector3(k, k, 1f);
    }
}
