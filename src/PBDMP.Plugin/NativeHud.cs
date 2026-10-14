using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PBDMP.Plugin;

/// <summary>In-game session badge and short notifications on the native legend strip. Fades on unscaled time: the session may be paused.</summary>
internal sealed class NativeHud
{
    private const int MaxToasts = 3;
    private const float ToastSeconds = 5f, FadeSeconds = 0.6f;
    private readonly GameObject _badge;
    private readonly Image _badgeArt;
    private readonly (GameObject Root, Image Art, TMP_Text Text, CanvasGroup Group)[] _views = new (GameObject, Image, TMP_Text, CanvasGroup)[MaxToasts];
    private readonly List<(string Text, float Until)> _toasts = new();
    private string _shown = "";
    internal TMP_Text Text { get; }

    internal NativeHud(NativeUiKit kit, Transform root)
    {
        _badge = NativeUiKit.Rect("Session badge", root, new Vector2(24, 1040), new Vector2(400, 40));
        _badge.GetComponent<RectTransform>().pivot = new Vector2(0, 0.5f);
        _badgeArt = _badge.AddComponent<Image>(); NativeUiKit.CopyImage(kit.BadgeArt, _badgeArt); _badgeArt.raycastTarget = false;
        Text = kit.TextAt(_badge.transform, "Session status", "", Vector2.zero, Vector2.zero, 20);
        NativeUiKit.Stretch(Text.rectTransform); Text.enableWordWrapping = false; Text.overflowMode = TextOverflowModes.Ellipsis;
        Text.margin = new Vector4(26, 0, 26, 0);
        _badge.SetActive(false);
        for (var i = 0; i < MaxToasts; i++)
        {
            var toast = NativeUiKit.Rect("Notification " + i, root, new Vector2(24, 990 - i * 46), new Vector2(400, 38));
            toast.GetComponent<RectTransform>().pivot = new Vector2(0, 0.5f);
            var art = toast.AddComponent<Image>(); NativeUiKit.CopyImage(kit.BadgeArt, art); art.raycastTarget = false;
            var text = kit.TextAt(toast.transform, "Text", "", Vector2.zero, Vector2.zero, 19);
            NativeUiKit.Stretch(text.rectTransform); text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Ellipsis;
            text.margin = new Vector4(26, 0, 26, 0);
            _views[i] = (toast, art, text, toast.AddComponent<CanvasGroup>());
            toast.SetActive(false);
        }
    }

    /// <summary>Empty text hides the badge.</summary>
    internal void Set(string text)
    {
        if (text == _shown) return;
        _shown = text; Text.text = text;
        _badge.SetActive(text.Length > 0);
        if (text.Length > 0) Fit(_badge, Text, text);
    }

    internal void Toast(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || _toasts.Any(t => t.Text == text)) return;
        if (_toasts.Count == MaxToasts) _toasts.RemoveAt(0);
        _toasts.Add((text, Time.unscaledTime + ToastSeconds));
    }

    internal void Tick()
    {
        _toasts.RemoveAll(t => Time.unscaledTime >= t.Until);
        for (var i = 0; i < MaxToasts; i++)
        {
            var view = _views[i];
            if (i >= _toasts.Count) { if (view.Root.activeSelf) view.Root.SetActive(false); continue; }
            if (view.Text.text != _toasts[i].Text) { view.Text.text = _toasts[i].Text; Fit(view.Root, view.Text, _toasts[i].Text); }
            view.Group.alpha = Mathf.Clamp01((_toasts[i].Until - Time.unscaledTime) / FadeSeconds);
            if (!view.Root.activeSelf) view.Root.SetActive(true);
        }
    }

    private static void Fit(GameObject strip, TMP_Text text, string value)
    {
        var rect = strip.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(Mathf.Clamp(text.GetPreferredValues(value).x + 56, 160, 1100), rect.sizeDelta.y);
    }
}
