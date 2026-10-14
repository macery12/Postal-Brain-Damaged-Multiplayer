using Hyperstrange.PBD;
using Hyperstrange.PBD.UI.Components;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PBDMP.Plugin;

/// <summary>Clones native controls and borrows native art from the live menu canvas. No generated artwork.</summary>
internal sealed class NativeUiKit
{
    private readonly GameObject _staging, _actionTemplate, _inputTemplate;
    private readonly TMP_Text _bodyTemplate;
    private readonly MainHyperButton _mainTemplate;
    private readonly Image _panelArt, _dimArt, _spiralArt;
    internal Image CardArt { get; }
    internal Image SpinnerArt { get; }
    internal Image BadgeArt { get; }

    internal NativeUiKit(Canvas nativeCanvas, Transform root)
    {
        var group = Require(nativeCanvas.transform, "MainCanvasGroup");
        var prompt = Require(group, "PROMPT_Input/OverlayImage/Background");
        _actionTemplate = Require(prompt, "BUTTON_Confirm").gameObject;
        _inputTemplate = Require(prompt, "INPUT_Field/Image").gameObject;
        _bodyTemplate = Require(prompt, "Text").GetComponent<TMP_Text>();
        _panelArt = prompt.GetComponent<Image>();
        _dimArt = Require(group, "PROMPT_Input/OverlayImage").GetComponent<Image>();
        _spiralArt = Require(prompt, "SpiralImage").GetComponent<Image>();
        CardArt = Require(group, "MENU_Pause/Stats/Title").GetComponent<Image>();
        SpinnerArt = Require(group, "MENU_Loading/LoadingIcon").GetComponent<Image>();
        BadgeArt = Require(group, "MenuLegendComponent/ElementsLayout").GetComponent<Image>();
        _mainTemplate = Require(group, "MENU_Main/ButtonsLayout/BUTTON_Start").GetComponent<MainHyperButton>();
        _staging = new GameObject("PBDMP inactive UI staging");
        _staging.transform.SetParent(root, false); _staging.SetActive(false);
    }

    internal TMP_Text TextAt(Transform parent, string name, string value, Vector2 center, Vector2 size, int fontSize)
    {
        var text = Rect(name, parent, center, size).AddComponent<TextMeshProUGUI>();
        text.font = _bodyTemplate.font; text.fontSharedMaterial = _bodyTemplate.fontSharedMaterial;
        text.color = Extension_UI.INTERACTABLE_HIGHLIGHTED_COLOR; text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center; text.enableWordWrapping = true;
        text.raycastTarget = false; text.text = value;
        return text;
    }

    /// <summary>Text in the main menu's own lettering.</summary>
    internal TMP_Text HeadingAt(Transform parent, string name, string value, Vector2 center, Vector2 size, int fontSize)
    {
        var text = TextAt(parent, name, value, center, size, fontSize);
        text.font = _mainTemplate.LabelText.font; text.fontSharedMaterial = _mainTemplate.ClickableMaterial;
        text.color = Extension_UI.INTERACTABLE_BASE_COLOR; text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    internal Image ArtAt(Transform parent, string name, Image art, Vector2 center, Vector2 size)
    {
        var image = Rect(name, parent, center, size).AddComponent<Image>();
        CopyImage(art, image); image.raycastTarget = false;
        return image;
    }

    /// <summary>Full-screen native gradient that also swallows pointer input for whatever lies beneath.</summary>
    internal Image DimAt(Transform parent, string name, float alpha)
    {
        var dim = Rect(name, parent, Vector2.zero, Vector2.zero).AddComponent<Image>();
        CopyImage(_dimArt, dim); Stretch(dim.rectTransform);
        dim.color = new Color(0, 0, 0, alpha); dim.raycastTarget = true;
        return dim;
    }

    /// <summary>Centered native panel. The oversized spiral is clipped by the panel art, as in the game's prompts.</summary>
    internal GameObject BoxAt(Transform parent, string name, Vector2 size, out RectTransform spiral)
    {
        var box = Rect(name, parent, Vector2.zero, size);
        var rect = box.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.anchoredPosition = Vector2.zero;
        var backing = box.AddComponent<Image>(); CopyImage(_panelArt, backing);
        backing.type = Image.Type.Sliced; backing.pixelsPerUnitMultiplier = 2; backing.raycastTarget = true;
        box.AddComponent<Mask>().showMaskGraphic = true;
        var side = Mathf.Max(size.x, size.y) * 1.25f;
        var art = ArtAt(box.transform, "Native spiral", _spiralArt, size / 2, new Vector2(side, side));
        art.color = new Color(1, 1, 1, 0.16f);
        spiral = art.rectTransform;
        return box;
    }

    internal MainHyperButton ActionAt(Transform parent, string label, Vector2 center, Vector2 size, System.Action action)
    {
        var copy = InactiveCopy(_actionTemplate); copy.name = label;
        foreach (var fitter in copy.GetComponentsInChildren<ContentSizeFitter>(true)) Object.DestroyImmediate(fitter);
        foreach (var layout in copy.GetComponentsInChildren<LayoutGroup>(true)) Object.DestroyImmediate(layout);
        foreach (var resetter in copy.GetComponentsInChildren<LayoutResetter>(true)) Object.DestroyImmediate(resetter);
        var button = copy.GetComponent<MainHyperButton>();
        PrepareButton(button, label, action);
        Place(copy.GetComponent<RectTransform>(), parent, center, size);
        var image = copy.transform.Find("Image").GetComponent<Image>(); Stretch(image.rectTransform);
        button.LabelText.fontSize = 27; button.LabelText.enableAutoSizing = false;
        Stretch(button.LabelText.rectTransform); button.LabelText.margin = new Vector4(10, 0, 10, 0);
        button.LabelText.alignment = TextAlignmentOptions.Center;
        copy.SetActive(true); button.SetInteractable();
        return button;
    }

    internal TMP_InputField InputAt(Transform parent, string name, string value, Vector2 center, Vector2 size)
    {
        var copy = InactiveCopy(_inputTemplate); copy.name = name;
        Place(copy.GetComponent<RectTransform>(), parent, center, size);
        var field = copy.GetComponentInChildren<TMP_InputField>(true);
        field.onValueChanged = new TMP_InputField.OnChangeEvent();
        field.onEndEdit = new TMP_InputField.SubmitEvent(); field.onSubmit = new TMP_InputField.SubmitEvent();
        field.onSelect = new TMP_InputField.SelectionEvent(); field.onDeselect = new TMP_InputField.SelectionEvent();
        field.onTextSelection = new TMP_InputField.TextSelectionEvent(); field.onEndTextSelection = new TMP_InputField.TextSelectionEvent();
        field.textComponent.fontSize = 28; field.textComponent.enableAutoSizing = false;
        Stretch(field.GetComponent<RectTransform>());
        field.textViewport.offsetMin = new Vector2(14, 4); field.textViewport.offsetMax = new Vector2(-14, -4);
        field.text = value; field.lineType = TMP_InputField.LineType.SingleLine;
        copy.SetActive(true);
        return field;
    }

    /// <summary>Copies a native menu entry beside its template; the caller places and activates it.</summary>
    internal GameObject InactiveCopy(GameObject source)
    {
        var copy = Object.Instantiate(source, _staging.transform, false);
        copy.SetActive(false);
        foreach (var localized in copy.GetComponentsInChildren<LocalizedText>(true)) localized.LocalizedLabel = null!;
        return copy;
    }

    internal static void PrepareButton(MainHyperButton button, string label, System.Action action)
    {
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener((UnityEngine.Events.UnityAction)action);
        button.LabelText.LocalizedLabel = null!; button.LabelText.text = label;
        button._doubleClickAction = null!;
        var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
    }

    internal static void SetButtonState(MainHyperButton button, bool enabled)
    {
        if (button.interactable == enabled) return;
        if (enabled) button.SetInteractable(); else button.SetDisabled();
        button.interactable = enabled;
    }

    internal static void SetLabel(MainHyperButton button, string label)
    { if (button.LabelText.text != label) button.LabelText.text = label; }

    internal static void CopyImage(Image source, Image target)
    { target.sprite = source.sprite; target.material = source.material; target.color = source.color; target.type = source.type; }

    internal static Transform Require(Transform root, string path) => root.Find(path) ?? throw new InvalidOperationException("Native UI template missing: " + path);
    internal static GameObject Rect(string name, Transform parent, Vector2 center, Vector2 size)
    { var obj = new GameObject(name); Place(obj.AddComponent<RectTransform>(), parent, center, size); return obj; }
    internal static void Place(RectTransform rect, Transform parent, Vector2 center, Vector2 size)
    {
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f); rect.anchoredPosition = center; rect.sizeDelta = size;
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }
    internal static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
}
