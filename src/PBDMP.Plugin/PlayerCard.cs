using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PBDMP.Plugin;

/// <summary>What one lobby slot shows. <paramref name="Glyph"/> stands in while there is no avatar.</summary>
internal readonly record struct CardModel(string Name, string Role, string State, Texture2D? Avatar, string Glyph, bool Occupied);

/// <summary>One player slot on native stat-panel art: avatar well, name in menu lettering, role and state lines.</summary>
internal sealed class PlayerCard
{
    private readonly RawImage _avatar;
    private readonly TMP_Text _glyph, _name, _role, _state;
    private CardModel _shown;
    internal GameObject Root { get; }
    internal Image Backing { get; }

    internal PlayerCard(NativeUiKit kit, Transform parent, string name, Vector2 center, Vector2 size)
    {
        Root = NativeUiKit.Rect(name, parent, center, size);
        Backing = kit.ArtAt(Root.transform, "Backing", kit.CardArt, size / 2, size);
        var side = size.y - 48;
        var well = NativeUiKit.Rect("Avatar well", Root.transform, new Vector2(side / 2 + 34, size.y / 2), new Vector2(side, side));
        var shade = well.AddComponent<Image>(); shade.color = new Color(0, 0, 0, 0.5f); shade.raycastTarget = false;
        _glyph = kit.HeadingAt(well.transform, "Role glyph", "", new Vector2(side / 2, side / 2), new Vector2(side, side), 46);
        _avatar = NativeUiKit.Rect("Avatar", well.transform, new Vector2(side / 2, side / 2), new Vector2(side - 8, side - 8)).AddComponent<RawImage>();
        _avatar.raycastTarget = false;
        var left = side + 62; var width = size.x - left - 30; var x = left + width / 2;
        _name = kit.HeadingAt(Root.transform, "Name", "", new Vector2(x, size.y / 2 + 34), new Vector2(width, 48), 31);
        _role = kit.TextAt(Root.transform, "Role", "", new Vector2(x, size.y / 2 - 8), new Vector2(width, 32), 22);
        _state = kit.TextAt(Root.transform, "State", "", new Vector2(x, size.y / 2 - 42), new Vector2(width, 32), 21);
        foreach (var text in new[] { _name, _role, _state })
        { text.alignment = TextAlignmentOptions.Left; text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Ellipsis; }
        Set(new("", "", "", null, "", false));
    }

    internal void Set(in CardModel model)
    {
        if (model == _shown && Root.activeSelf) return;
        _shown = model;
        _name.text = model.Name; _role.text = model.Role; _state.text = model.State;
        // Unity's null covers a texture destroyed during cleanup.
        var avatar = model.Avatar != null;
        _avatar.gameObject.SetActive(avatar);
        if (avatar) _avatar.texture = model.Avatar;
        _glyph.gameObject.SetActive(!avatar); _glyph.text = model.Glyph;
        var alpha = model.Occupied ? 1f : 0.55f;
        foreach (var text in new[] { _name, _role, _state, _glyph }) text.alpha = alpha;
    }
}
