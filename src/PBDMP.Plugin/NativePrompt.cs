using Hyperstrange.PBD.UI.Components;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PBDMP.Plugin;

/// <summary>Confirmation, information and progress dialog shaped like the game's own prompts, inside PBDMP's canvas.</summary>
internal sealed class NativePrompt
{
    private readonly TMP_Text _text;
    private readonly RectTransform _spinner, _spiral;
    private System.Action? _confirm, _cancel;
    internal GameObject Root { get; }
    internal MainHyperButton ConfirmButton { get; }
    internal MainHyperButton CancelButton { get; }
    internal bool Open => Root.activeSelf;
    internal bool Busy => _spinner.gameObject.activeSelf;
    internal string Message => _text.text;

    internal NativePrompt(NativeUiKit kit, Transform parent)
    {
        Root = NativeUiKit.Rect("Multiplayer prompt", parent, Vector2.zero, Vector2.zero);
        NativeUiKit.Stretch(Root.GetComponent<RectTransform>());
        kit.DimAt(Root.transform, "Prompt dim", 0.7f);
        var box = kit.BoxAt(Root.transform, "Prompt panel", new Vector2(820, 360), out _spiral);
        _text = kit.TextAt(box.transform, "Text", "", new Vector2(410, 222), new Vector2(700, 150), 27);
        _spinner = kit.ArtAt(box.transform, "Busy spiral", kit.SpinnerArt, new Vector2(410, 132), new Vector2(84, 84)).rectTransform;
        ConfirmButton = kit.ActionAt(box.transform, "Confirm", new Vector2(270, 74), new Vector2(240, 60), () => Resolve(true));
        CancelButton = kit.ActionAt(box.transform, "Cancel", new Vector2(550, 74), new Vector2(240, 60), () => Resolve(false));
        Root.SetActive(false);
    }

    internal Selectable[] Controls => new Selectable[] { ConfirmButton, CancelButton }.Where(b => b.gameObject.activeSelf).ToArray();

    /// <summary>A question with two answers, or an <paramref name="cancel"/>-less notice with one button.</summary>
    internal void Ask(string text, string confirm, System.Action? onConfirm, string? cancel = null, System.Action? onCancel = null)
    {
        Show(text, false);
        _confirm = onConfirm; _cancel = onCancel;
        NativeUiKit.SetLabel(ConfirmButton, confirm);
        ConfirmButton.gameObject.SetActive(true);
        CancelButton.gameObject.SetActive(cancel != null);
        if (cancel != null) NativeUiKit.SetLabel(CancelButton, cancel);
        Center(ConfirmButton, cancel == null ? 410 : 270);
    }

    /// <summary>Progress with a native loading spiral and a single way out.</summary>
    internal void Wait(string text, string cancel, System.Action onCancel)
    {
        Show(text, true);
        _confirm = null; _cancel = onCancel;
        ConfirmButton.gameObject.SetActive(false);
        CancelButton.gameObject.SetActive(true); NativeUiKit.SetLabel(CancelButton, cancel);
        Center(CancelButton, 410);
    }

    internal void Update(string text) { if (_text.text != text) _text.text = text; }

    internal void Hide() { _confirm = _cancel = null; Root.SetActive(false); }

    /// <summary>Back/Escape answers the prompt the way its cancel button would.</summary>
    internal void Cancel() => Resolve(!CancelButton.gameObject.activeSelf);

    internal void Tick()
    {
        _spiral.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * 6f);
        if (Busy) _spinner.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * 240f);
    }

    private void Show(string text, bool busy)
    {
        _text.text = text;
        _text.rectTransform.anchoredPosition = new Vector2(410, busy ? 252 : 222);
        _spinner.gameObject.SetActive(busy);
        Root.SetActive(true);
        Root.transform.SetAsLastSibling();
    }

    private static void Center(MainHyperButton button, float x)
    {
        var rect = button.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
    }

    private void Resolve(bool confirmed)
    {
        var action = confirmed ? _confirm : _cancel;
        Hide();
        action?.Invoke();
    }
}
