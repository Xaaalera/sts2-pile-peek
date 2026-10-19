using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS2RitsuLib.Utils;

namespace PilePeek;

[ModInitializer(nameof(Initialize))]
public partial class Entry : Node
{
    private static Node? _root;
    private static Godot.Timer? _timer;
    private static NCombatUi? _ui;
    private static readonly List<PilePreview> Previews = [];
    private static PilePreview? _active;
    private static I18N? _localization;
    private static IDisposable? _adapter;
    private static string? _lastFailure;
    private static PilePreview? _keyboardPreview;
    private static readonly HashSet<Key> HeldShortcuts = [];
    private static int _keyEvents;

    public static void Initialize() => Callable.From(Start).CallDeferred();

    private static void Start()
    {
        if (GodotObject.IsInstanceValid(_root)) { return; }
        _localization = new I18N("PilePeek", resourceFolders: ["PilePeek.Localization"], resourceAssembly: typeof(Entry).Assembly);
        _localization.Changed += Invalidate;
        _root = new Entry { Name = "PilePeekController" };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(_root);
        _root.TreeExiting += Stop;
        _timer = new Godot.Timer { WaitTime = .1, Autostart = true, ProcessMode = Node.ProcessModeEnum.Always };
        _root.AddChild(_timer);
        _timer.Timeout += Tick;
        RenderingServer.FramePreDraw += Present;
        var host = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == "Sts2.Devkit.GameHost")?
            .GetType("Sts2.Devkit.GameHost.GameSession");
        _adapter = host?.GetMethod("RegisterAdapter")?.Invoke(null, ["pile-peek", (Func<string, string, string>)HandleCommand]) as IDisposable;
        Tick();
        GD.Print("pile-peek.started");
    }

    internal static string Text(string key) => _localization?.Get(key, key) ?? key;

    internal static void Focus(PilePreview preview)
    {
        if (_keyboardPreview is not null) { return; }
        if (!ReferenceEquals(_active, preview)) { _active?.Hide(); }
        _active = preview;
        // Native Focused is emitted before NCombatCardPile.OnFocus creates its own set.
        Callable.From(Tick).CallDeferred();
    }

    internal static void Unfocus(PilePreview preview)
    {
        if (ReferenceEquals(_keyboardPreview, preview)) { return; }
        preview.Hide();
        if (ReferenceEquals(_active, preview)) { _active = null; }
    }

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey key) { return; }
        _keyEvents++;
        if (HeldShortcuts.Contains(key.Keycode))
        {
            GetViewport().SetInputAsHandled();
            if (!key.Pressed)
            {
                HeldShortcuts.Remove(key.Keycode);
                if (_keyboardPreview?.ShortcutKey == key.Keycode) { EndKeyboardPreview(); }
            }
            return;
        }
        if (key.Keycode == Key.Alt && !key.Pressed) { EndKeyboardPreview(); return; }
        if (!key.Pressed || key.Echo || !key.AltPressed || key.CtrlPressed || key.MetaPressed || key.ShiftPressed || !CanPreviewFromKeyboard()) { return; }
        var target = Previews.FirstOrDefault(preview => preview.ShortcutKey == key.Keycode);
        if (target is null || !target.IsAvailable) { return; }
        EndKeyboardPreview();
        HeldShortcuts.Add(key.Keycode);
        _keyboardPreview = target;
        _active?.Hide();
        _active = target;
        target.KeyboardFocused = true;
        GetViewport().SetInputAsHandled();
        Tick();
    }

    private static bool CanPreviewFromKeyboard() => _ui is not null && _ui.IsVisibleInTree() &&
        NGame.IsGameFocusedWindow() && NGame.Instance?.Transition.InTransition == false &&
        NDevConsole.Instance?.Visible != true && NCapstoneContainer.Instance?.InUse != true &&
        _root?.GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit);

    private static void EndKeyboardPreview()
    {
        if (_keyboardPreview is null) { return; }
        var previous = _keyboardPreview;
        _keyboardPreview = null;
        previous.KeyboardFocused = false;
        previous.Hide();
        if (ReferenceEquals(_active, previous)) { _active = null; }
    }

    private static void Tick()
    {
        try
        {
            var ui = CombatManager.Instance.IsInProgress ? NCombatRoom.Instance?.Ui : null;
            if (!GodotObject.IsInstanceValid(ui) || !ui!.IsNodeReady() || !ui.IsVisibleInTree()) { ui = null; }
            if (!ReferenceEquals(ui, _ui))
            {
                Clear();
                _ui = ui;
                var state = CombatManager.Instance.DebugOnlyGetState();
                var player = state is null ? null : LocalContext.GetMe(state);
                if (ui is not null && player?.PlayerCombatState is not null)
                {
                    Previews.Add(new(ui.DrawPile, PileType.Draw.GetPile(player)));
                    Previews.Add(new(ui.DiscardPile, PileType.Discard.GetPile(player)));
                    Previews.Add(new(ui.ExhaustPile, PileType.Exhaust.GetPile(player)));
                }
            }
            if (_keyboardPreview is not null && !CanPreviewFromKeyboard()) { EndKeyboardPreview(); }
            if (!NGame.IsGameFocusedWindow()) { HeldShortcuts.Clear(); }
            _active?.Refresh();
            _lastFailure = null;
        }
        catch (Exception exception) { ReportFailure(exception); }
    }

    private static void Present()
    {
        try { _active?.Present(); }
        catch (Exception exception) { ReportFailure(exception); }
    }

    private static void ReportFailure(Exception exception)
    {
        _active?.Hide();
        var diagnostic = exception.ToString();
        if (_lastFailure == diagnostic) { return; }
        _lastFailure = diagnostic;
        GD.PrintErr(JsonSerializer.Serialize(new { error = new { code = 500, status = "INTERNAL", message = Text("Unavailable"),
            details = new[] { new { reason = "NATIVE_PILE_PREVIEW_FAILURE", domain = "pile-peek.player" } } }, diagnostic }));
    }

    private static void Invalidate() { foreach (var preview in Previews) { preview.Invalidate(); } }

    private static void Clear()
    {
        EndKeyboardPreview();
        HeldShortcuts.Clear();
        _active = null;
        foreach (var preview in Previews) { preview.Dispose(); }
        Previews.Clear();
        _ui = null;
    }

    public static void Stop()
    {
        RenderingServer.FramePreDraw -= Present;
        Clear();
        _adapter?.Dispose(); _adapter = null;
        if (_localization is not null) { _localization.Changed -= Invalidate; _localization.Dispose(); _localization = null; }
        if (GodotObject.IsInstanceValid(_timer)) { _timer!.Stop(); _timer.Timeout -= Tick; }
        _timer = null;
        if (GodotObject.IsInstanceValid(_root)) { _root!.TreeExiting -= Stop; _root.QueueFree(); }
        _root = null;
    }

    private static string HandleCommand(string command, string arguments)
    {
        if (command == "inspect")
        {
            Tick();
            return JsonSerializer.Serialize(new { error = _lastFailure, keyEvents = _keyEvents, inputProcessing = _root?.IsProcessingInput(),
                keyboardAvailable = CanPreviewFromKeyboard(), keyboardPile = _keyboardPreview?.Type.ToString(),
                piles = Previews.Select(preview => preview.Inspect()).ToArray() });
        }
        if (command == "preview-pile")
        {
            using var document = JsonDocument.Parse(arguments);
            if (!Enum.TryParse<PileType>(document.RootElement.GetProperty("pile").GetString(), true, out var pile)) { throw new ArgumentException("Unknown pile"); }
            foreach (var preview in Previews) { preview.DebugUnhover(); }
            var target = Previews.Single(preview => preview.Type == pile);
            target.DebugHover();
            ((SceneTree)Engine.GetMainLoop()).CreateTimer(3).Timeout += target.EndDebugHover;
            return "{\"posed\":true,\"physicalInput\":false}";
        }
        throw new ArgumentException("Unknown command");
    }
}
