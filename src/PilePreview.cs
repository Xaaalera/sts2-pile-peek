using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.addons.mega_text;

namespace PilePeek;

internal sealed class PilePreview : IDisposable
{
    private sealed record Line(CardModel Card, string Title, string Energy, string Stars);
    private readonly NCombatCardPile _button;
    private readonly CardPile _pile;
    private NHoverTipSet? _set;
    private Control? _tip;
    private MegaRichTextLabel? _description;
    private VBoxContainer? _body;
    private ScrollContainer? _scroll;
    private VBoxContainer? _rows;
    private MegaLabel? _hint;
    private MegaLabel? _shortcutHint;
    private IReadOnlyList<Line> _lines = [];
    private string? _signature;
    private bool _focused;
    private bool _disposed;
    private float _contentWidth;
    private static Texture2D? _starTexture;

    internal PileType Type => _pile.Type;
    internal bool KeyboardFocused { get; set; }
    internal bool IsAvailable => !_disposed && GodotObject.IsInstanceValid(_button) && _button.IsVisibleInTree() && _button.IsEnabled;
    internal Key ShortcutKey => NInputManager.Instance?.GetShortcutKey(Type switch
    {
        PileType.Draw => MegaInput.viewDrawPile,
        PileType.Discard => MegaInput.viewDiscardPile,
        _ => MegaInput.viewExhaustPileAndTabRight
    }) ?? Key.None;

    internal PilePreview(NCombatCardPile button, CardPile pile)
    {
        _button = button;
        _pile = pile;
        button.Focused += OnFocused;
        button.Unfocused += OnUnfocused;
        button.Released += OnReleased;
        button.GuiInput += OnInput;
        pile.ContentsChanged += Invalidate;
    }

    private void OnFocused(NClickableControl _) { _focused = true; Entry.Focus(this); }
    private void OnUnfocused(NClickableControl _) { _focused = false; Entry.Unfocus(this); }
    private void OnReleased(NClickableControl _) => Hide();
    internal void Invalidate() => _signature = null;

    internal void Refresh()
    {
        if (_disposed || !(_focused || KeyboardFocused) || !GodotObject.IsInstanceValid(_button) || !_button.IsVisibleInTree() ||
            !_button.IsEnabled || NCapstoneContainer.Instance?.InUse == true || NHoverTipSet.shouldBlockHoverTips)
        { Hide(); return; }
        var cards = PileOrder.ForDisplay(_pile.Cards, Type == PileType.Draw, (first, second) =>
            first.Rarity != second.Rarity ? first.Rarity.CompareTo(second.Rarity) : string.Compare(first.Id.Entry, second.Id.Entry, StringComparison.Ordinal));
        var lines = cards.Select(card => new Line(card, card.Title,
            card.EnergyCost.CostsX ? "X" : Cost(card.EnergyCost.GetWithModifiers(CostModifiers.All)),
            card.HasStarCostX ? "X" : Cost(card.GetStarCostWithModifiers()))).ToArray();
        var header = $"{Header().GetFormattedText()} ({_pile.Cards.Count})";
        var signature = header + "|" + Entry.Text("ScrollHint") + "|" + Entry.Text("ShortcutHint") + "|" + ShortcutKey + "|" + string.Join(';', lines.Select(line =>
            $"{line.Card.Id.Entry}:{line.Title}:{line.Energy}:{line.Stars}:{line.Card.IsUpgraded}"));
        if (!GodotObject.IsInstanceValid(_set) || _set!.IsQueuedForDeletion())
        {
            Create(header);
            _signature = null;
        }
        if (_set is null || _signature == signature) { return; }
        _lines = lines;
        _signature = signature;
        _tip!.GetNode<MegaLabel>("%Title").SetTextAutoSize(header);
        BuildRows();
    }

    private static string Cost(int value) => value < 0 ? "" : value.ToString(CultureInfo.InvariantCulture);

    private LocString Header()
    {
        var title = new LocString("static_hover_tips", Type.ToString().ToUpperInvariant() + "_PILE.title");
        var hotkey = Type switch { PileType.Draw => MegaInput.viewDrawPile, PileType.Discard => MegaInput.viewDiscardPile, _ => MegaInput.viewExhaustPileAndTabRight };
        var input = MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager.Instance;
        title.Add("Hotkey", input is null ? "None" : input.GetShortcutKey(hotkey).ToString());
        return title;
    }

    private void Create(string header)
    {
        NHoverTipSet.Remove(_button);
        _set = NHoverTipSet.CreateAndShow(_button, new HoverTip(Header(), ""));
        if (_set is null) { return; }
        _set.Name = "PilePeek_" + Type;
        _set.Modulate = new(1, 1, 1, 0);
        _tip = _set.GetNode<Control>("textHoverTipContainer").GetChild<Control>(0);
        _description = _tip.GetNode<MegaRichTextLabel>("%Description");
        _description.Text = "";
        _contentWidth = Math.Clamp(_button.GetViewportRect().Size.X * .18f, 220, 280);
        var title = _tip.GetNode<MegaLabel>("%Title");
        _shortcutHint = Label(Entry.Text("ShortcutHint").Replace("{Hotkey}", ShortcutKey.ToString()), 14);
        _shortcutHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _shortcutHint.CustomMinimumSize = new(_contentWidth, 0);
        _shortcutHint.Size = new(_contentWidth, 0);
        var headingRow = title.GetParent();
        var titleIndex = title.GetIndex();
        var heading = new VBoxContainer { Name = "PilePeekHeading", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        heading.AddThemeConstantOverride("separation", 2);
        headingRow.AddChild(heading);
        headingRow.MoveChild(heading, titleIndex);
        title.Reparent(heading);
        heading.AddChild(_shortcutHint);
        _body = new VBoxContainer { Name = "PilePeekBody", MouseFilter = Control.MouseFilterEnum.Ignore };
        _body.AddThemeConstantOverride("separation", 4);
        _description.AddChild(_body);
        _scroll = new ScrollContainer { Name = "PilePeekScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new(_contentWidth, 30) };
        _body.AddChild(_scroll);
        _rows = new VBoxContainer { Name = "PilePeekRows", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        _rows.AddThemeConstantOverride("separation", 3);
        _scroll.AddChild(_rows);
        _hint = Label(Entry.Text("ScrollHint"), 14);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hint.CustomMinimumSize = new(_contentWidth, 0);
        _hint.Size = new(_contentWidth, 0);
        _hint.Modulate = new(1, 1, 1, .75f);
        _body.AddChild(_hint);
    }

    private void BuildRows()
    {
        foreach (var child in _rows!.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        var showStars = _lines.Any(line => line.Stars.Length > 0);
        if (_lines.Count == 0)
        {
            var empty = new MegaRichTextLabel { Name = "PilePeekEmpty", MouseFilter = Control.MouseFilterEnum.Ignore,
                BbcodeEnabled = true, FitContent = true, AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new(_contentWidth - 16, 30), Size = new(_contentWidth - 16, 30) };
            empty.AddThemeFontOverride("normal_font", PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_bold_glyph_space_two.tres"));
            empty.AddThemeFontSizeOverride("normal_font_size", 18);
            empty.Text = new LocString("combat_messages", "OPEN_EMPTY_" + Type.ToString().ToUpperInvariant()).GetFormattedText();
            _rows.AddChild(empty);
        }
        foreach (var line in _lines)
        {
            var row = new HBoxContainer { Name = "PilePeekCard", MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 6);
            _rows.AddChild(row);
            row.AddChild(CostIcon(line.Energy, line.Card.EnergyIcon, line.Card.Pool.EnergyOutlineColor));
            if (showStars) { row.AddChild(CostIcon(line.Stars, StarTexture(), StsColors.defaultStarCostOutline)); }
            var name = Label(line.Title, 18);
            var width = _contentWidth - (showStars ? 72 : 36) - 16;
            name.CustomMinimumSize = new(width, 30);
            name.Size = name.CustomMinimumSize;
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            name.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            if (line.Card.IsUpgraded) { name.AddThemeColorOverride("font_color", StsColors.green); }
            row.AddChild(name);
        }
        _hint!.SetTextAutoSize(Entry.Text("ScrollHint"));
        _shortcutHint!.SetTextAutoSize(Entry.Text("ShortcutHint").Replace("{Hotkey}", ShortcutKey.ToString()));
        IgnoreMouse(_tip!);
    }

    private static MegaLabel Label(string text, int size)
    {
        var label = new MegaLabel { MouseFilter = Control.MouseFilterEnum.Ignore, AutoSizeEnabled = false, MinFontSize = size, MaxFontSize = size,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontOverride("font", PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_bold_glyph_space_two.tres"));
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", StsColors.cream);
        label.SetTextAutoSize(text);
        return label;
    }

    private static Control CostIcon(string text, Texture2D? texture, Color outline)
    {
        var slot = new Control { CustomMinimumSize = new(30, 30), MouseFilter = Control.MouseFilterEnum.Ignore };
        var icon = new TextureRect { Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Visible = text.Length > 0,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        slot.AddChild(icon);
        icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var value = Label(text, 20);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.AddThemeColorOverride("font_outline_color", outline);
        value.AddThemeConstantOverride("outline_size", 4);
        icon.AddChild(value);
        value.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return slot;
    }

    private static Texture2D? StarTexture()
    {
        if (_starTexture is not null) { return _starTexture; }
        var card = PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<Control>();
        try { return _starTexture = card.GetNode<TextureRect>("%StarIcon").Texture; }
        finally { card.Free(); }
    }

    private static void IgnoreMouse(Control root)
    {
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var child in root.FindChildren("*", "Control", true, false).OfType<Control>()) { child.MouseFilter = Control.MouseFilterEnum.Ignore; }
    }

    internal void Present()
    {
        if (!GodotObject.IsInstanceValid(_set) || _set!.IsQueuedForDeletion() || _scroll is null || _rows is null) { return; }
        if (NCapstoneContainer.Instance?.InUse == true || !(_focused || KeyboardFocused) || !_button.IsVisibleInTree()) { Hide(); return; }
        var viewport = _button.GetViewportRect().Size;
        var safeTop = Math.Max(16, NRun.Instance?.GlobalUi?.TopBar?.GetNodeOrNull<Control>("BgImage")?.GetGlobalRect().End.Y + 12 ?? 16);
        var fullHeight = _rows.GetCombinedMinimumSize().Y;
        var overhead = Math.Max(64, _tip!.GetCombinedMinimumSize().Y - _description!.CustomMinimumSize.Y);
        var bodyHeight = Math.Clamp(fullHeight, 30, Math.Max(30, Math.Min(560, viewport.Y - safeTop - overhead - 48)));
        _hint!.Visible = fullHeight > bodyHeight + 1;
        _scroll.CustomMinimumSize = new(_contentWidth, bodyHeight);
        var bodySize = new Vector2(_contentWidth, bodyHeight + (_hint.Visible ? _hint.GetCombinedMinimumSize().Y + 4 : 0));
        _body!.Size = bodySize;
        _description.CustomMinimumSize = bodySize;
        _description.Size = bodySize;
        _tip.ResetSize();
        var size = _tip.Size;
        var anchor = _button.GetGlobalRect();
        var horizontal = Type == PileType.Draw ? anchor.End.X + 12 : anchor.Position.X - size.X - 12;
        horizontal = Math.Clamp(horizontal, 16, Math.Max(16, viewport.X - size.X - 16));
        var vertical = Math.Clamp(anchor.Position.Y - size.Y - 12, safeTop, Math.Max(safeTop, viewport.Y - size.Y - 16));
        _set.GetNode<Control>("textHoverTipContainer").Position = Vector2.Zero;
        _set.GlobalPosition = new(horizontal, vertical);
        _set.SetAlignment(_button, HoverTipAlignment.None);
        _set.Modulate = size.Y > 40 && size.Y <= viewport.Y - safeTop ? Colors.White : new(1, 1, 1, 0);
    }

    private void OnInput(InputEvent input)
    {
        if (!_focused || _scroll is null || !GodotObject.IsInstanceValid(_set) || input is not InputEventMouseButton { Pressed: true } mouse) { return; }
        var direction = mouse.ButtonIndex switch { MouseButton.WheelDown => 1, MouseButton.WheelUp => -1, _ => 0 };
        if (direction == 0 || _scroll.GetVScrollBar().MaxValue <= _scroll.Size.Y) { return; }
        _scroll.ScrollVertical += direction * 90;
        _button.AcceptEvent();
    }

    internal void Hide()
    {
        if (GodotObject.IsInstanceValid(_set) && !_set!.IsQueuedForDeletion()) { NHoverTipSet.Remove(_button); }
        _set = null; _tip = null; _description = null; _body = null; _scroll = null; _rows = null; _hint = null; _shortcutHint = null;
        _signature = null;
    }

    internal object Inspect()
    {
        var screen = _button.GetScreenTransform() * (_button.Size / 2);
        var client = _button.GetViewportTransform() * _button.GetGlobalTransform() * (_button.Size / 2);
        return new { pile = Type.ToString(), count = _pile.Cards.Count, focused = _focused, shortcutKey = ShortcutKey.ToString(),
            shown = GodotObject.IsInstanceValid(_set) && _set!.Modulate.A > .9f, button = _button.GetGlobalRect().ToString(),
            viewportCenter = new[] { screen.X, screen.Y }, clientCenter = new[] { client.X, client.Y }, viewport = _button.GetViewportRect().Size.ToString(),
            viewerOpen = NCapstoneContainer.Instance?.InUse == true,
            bounds = GodotObject.IsInstanceValid(_tip) ? _tip!.GetGlobalRect().ToString() : null,
            scroll = _scroll?.ScrollVertical, renderedCards = _lines.Select(line => new { id = line.Card.Id.Entry, line.Title, line.Energy, line.Stars }).ToArray() };
    }

    internal void DebugHover() { if (!_disposed) { _button.EmitSignal(Control.SignalName.MouseEntered); } }
    internal void DebugUnhover() { if (!_disposed) { _button.EmitSignal(Control.SignalName.MouseExited); } }
    internal void EndDebugHover()
    {
        if (!_disposed && GodotObject.IsInstanceValid(_button) && !_button.GetGlobalRect().HasPoint(_button.GetGlobalMousePosition())) { DebugUnhover(); }
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        Hide();
        _pile.ContentsChanged -= Invalidate;
        if (GodotObject.IsInstanceValid(_button))
        {
            _button.Focused -= OnFocused; _button.Unfocused -= OnUnfocused; _button.Released -= OnReleased; _button.GuiInput -= OnInput;
        }
    }
}
