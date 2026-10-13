using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace PrismPlay;
public interface IOverlayInteraction : IDisposable
{
    bool GameMode { get; }
    void SetGameMode(bool enabled);
    bool SetHotkey(char key);
    void Resize(double width, double height, bool animate = false);
    void BeginDrag();
    void UpdateDrag();
    void EndDrag();
    void Show();
}
public sealed class NativeWindow : IOverlayInteraction
{
    // The window frame has a fixed size (it changes only with scale or DPI) and is per-pixel transparent; the CSS card is the visible
    // shape. 480 = 156 card + 44 hover row + 280 Settings drawer, the tallest visible height (FRAME in frontend main.jsx).
    public const double FrameHeight = 480;
    private readonly nint hwnd;
    private readonly Window window;
    private readonly Preferences preferences;
    private readonly SubclassProc callback;
    private readonly bool preview;
    private NOTIFYICONDATA tray;
    private bool hotkeyRegistered, activatable;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private double logicalWidth = 300, logicalHeight = 100;
    private SizeInt32 frame;
    private int regionHeight = -1;
    // Visible-height spring, in physical pixels, matching the CSS --spring; it moves the window near the bottom edge. A retarget
    // restarts it from the sampled height and velocity.
    private double springFrom, springVelocity, springTarget, springHeight, holdHeight;
    private long springStart, holdUntil;
    private bool springing;
    private bool moving;
    private bool pointerDragging;
    private POINT dragPointerOrigin;
    private PointInt32 dragWindowOrigin;
    private double dragX, dragY, targetX, targetY;
    private long lastDragFrame;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer dragTimer;
    public bool GameMode { get; private set; }
    public event Action? ToggleRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;
    public event Action? PositionChanged;
    public event Action<bool>? MoveStateChanged;
    public NativeWindow(Window window, Preferences preferences, bool preview = false)
    {
        this.window = window; this.preferences = preferences; this.preview = preview;
        dragTimer = window.DispatcherQueue.CreateTimer();
        dragTimer.Interval = TimeSpan.FromMilliseconds(8);
        dragTimer.Tick += (_, _) => DragFrame();
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false); presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false;
        window.AppWindow.SetPresenter(presenter);
        var style = GetWindowLongPtr(hwnd, -20).ToInt64();
        if (!preview) SetWindowLongPtr(hwnd, -20, (nint)((style | 0x80L | 0x08000000L) & ~0x40000L));
        var frame = GetWindowLongPtr(hwnd, -16).ToInt64();
        SetWindowLongPtr(hwnd, -16, (nint)(frame & ~(0x00C00000L | 0x00040000L | 0x00800000L | 0x00400000L)));
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x1 | 0x2 | 0x4 | 0x10 | 0x20);
        var round = 1; DwmSetWindowAttribute(hwnd, 33, ref round, 4);
        var noBorder = unchecked((int)0xFFFFFFFE); DwmSetWindowAttribute(hwnd, 34, ref noBorder, 4);
        // Per-pixel alpha for the transparent backdrop (as WinUIEx TransparentTintBackdrop): blur-behind with an off-window region,
        // so DWM honours the content's alpha but blurs nothing. ponytail: not re-applied on WM_DWMCOMPOSITIONCHANGED (always on in Win11).
        var margins = new MARGINS(); DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var blur = new DWM_BLURBEHIND { dwFlags = 3, fEnable = true, hRgnBlur = CreateRectRgn(-2, -2, -1, -1) };
        DwmEnableBlurBehindWindow(hwnd, ref blur); DeleteObject(blur.hRgnBlur);
        window.AppWindow.Move(new PointInt32(preferences.X, preferences.Y));
        callback = WndProc;
        SetWindowSubclass(hwnd, callback, 1, 0);
        // ponytail: generic IDI_APPLICATION icon; the project has no .ico asset yet.
        tray = new NOTIFYICONDATA { cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = hwnd, uID = 1, uFlags = 1 | 2 | 4, uCallbackMessage = 0x8001, hIcon = LoadIcon(0, (nint)32512), szTip = TrayTip, szInfo = "", szInfoTitle = "" };
        Shell_NotifyIcon(0, ref tray);
        KeepOnTop();
    }
    // WinAppSDK 1.8: IsAlwaysOnTop set before the window is shown reads back true but leaves WS_EX_TOPMOST clear, and the AppWindow
    // drops raw SetWindowPos(HWND_TOPMOST). Re-apply through the presenter (false, then true forces it); this does not activate.
    public void KeepOnTop()
    {
        var before = (GetWindowLongPtr(hwnd, -20) & 0x8) != 0;
        if (!before && window.AppWindow.Presenter is OverlappedPresenter presenter) { presenter.IsAlwaysOnTop = false; presenter.IsAlwaysOnTop = true; }
        DiagnosticLog.Write($"WS_EX_TOPMOST {before} -> {(GetWindowLongPtr(hwnd, -20) & 0x8) != 0}");
    }
    private const string TrayTip = "PrismPlay · right-click for controls";
    public bool HotkeyRegistered => hotkeyRegistered;
    public bool SetHotkey(char key)
    {
        key = char.ToUpperInvariant(key);
        if (hotkeyRegistered && registeredKey == key) return true;
        // Register a new ID first so a failed change keeps the working shortcut.
        var candidate = activeHotkey == 1 ? 2 : 1;
        var registered = RegisterHotKey(hwnd, candidate, 0x4000 | 0x2 | 0x1, key);
        if (registered)
        {
            if (hotkeyRegistered) UnregisterHotKey(hwnd, activeHotkey);
            activeHotkey = candidate; registeredKey = key; hotkeyRegistered = true;
        }
        // The tooltip reports a conflict only while no shortcut works; NIM_MODIFY, and TaskbarCreated re-adds with this text.
        tray.szTip = hotkeyRegistered ? TrayTip : $"PrismPlay · Ctrl+Alt+{key} is in use · right-click for Settings";
        Shell_NotifyIcon(1, ref tray);
        return registered;
    }
    private int activeHotkey;
    private char registeredKey;
    public void SetGameMode(bool enabled)
    {
        if (moving) { pointerDragging = false; FinishDrag(); }
        GameMode = enabled;
        var style = GetWindowLongPtr(hwnd, -20).ToInt64();
        // WS_EX_LAYERED + WS_EX_TRANSPARENT passes mouse input across processes.
        style = enabled ? style | 0x80000L | 0x20L : style & ~(0x80000L | 0x20L);
        SetWindowLongPtr(hwnd, -20, (nint)style);
        if (enabled) SetLayeredWindowAttributes(hwnd, 0, 255, 2);
        SetWindowPos(hwnd, (nint)(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x20); KeepOnTop();
    }
    // `height` is the visible card height. With `animate` the spring runs there (retargeting mid-flight keeps its velocity);
    // otherwise, or when the frame itself changes size (scale, DPI), it snaps.
    public void Resize(double width, double height, bool animate = false)
    {
        logicalWidth = width; logicalHeight = height;
        var dpi = GetDpiForWindow(hwnd) / 96d;
        var size = new SizeInt32((int)Math.Ceiling(width * dpi), (int)Math.Ceiling(FrameHeight * preferences.Scale * dpi));
        var target = Math.Min(Math.Ceiling(height * dpi), size.Height);
        var reframe = size.Width != frame.Width || size.Height != frame.Height;
        // Mid-drag the frame keeps its size; FinishDrag calls Resize again.
        if (reframe && !moving) { frame = size; window.AppWindow.Resize(size); }
        if (reframe || !animate && target != springTarget) { StopSpring(); springHeight = springTarget = target; holdUntil = 0; }
        else if (target != springTarget)
        {
            // The hit-test region must cover the CSS card on every frame: it grows at once, and on a shrink keeps its height until the
            // 520 ms CSS transition has finished (560 ms, two frames of margin).
            if (target < regionHeight) { holdHeight = regionHeight; holdUntil = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency * 56 / 100; }
            (springFrom, springVelocity) = springing ? DragMath.Spring(springFrom, springVelocity, springTarget, SpringTime) : (springHeight, 0d);
            springTarget = target; springStart = System.Diagnostics.Stopwatch.GetTimestamp();
            // Rendering is vsync-aligned; subscribed only while the spring runs.
            if (!springing) { springing = true; CompositionTarget.Rendering += SpringFrame; }
        }
        ApplyRegion();
    }
    private double SpringTime => System.Diagnostics.Stopwatch.GetElapsedTime(springStart).TotalSeconds;
    private void SpringFrame(object? sender, object e)
    {
        var (x, v) = DragMath.Spring(springFrom, springVelocity, springTarget, SpringTime);
        if (Math.Abs(x - springTarget) < .25 && Math.Abs(v) < 5) x = springTarget;
        springHeight = x; ApplyRegion();
        if (x == springTarget && regionHeight == (int)springTarget) StopSpring(); // settled, and any shrink hold has been applied
    }
    private void StopSpring() { if (springing) CompositionTarget.Rendering -= SpringFrame; springing = false; }
    // The region is hit-testing only: a plain rectangle, so it never clips the CSS card's antialiased edge, and SetWindowRgn only when
    // its height changes. Then the window follows the saved position, clamped for the visible height, so near the bottom edge the card
    // rises and returns in step with the spring.
    private void ApplyRegion()
    {
        var h = (int)(System.Diagnostics.Stopwatch.GetTimestamp() < holdUntil ? Math.Max(springTarget, holdHeight) : springTarget);
        if (h != regionHeight)
        {
            regionHeight = h;
            // ponytail: the transparent pixels outside the card's rounded corners (and below it during a 560 ms shrink) still catch
            // clicks; a rounded or per-pixel hit test would need WM_NCHITTEST, which cannot pass clicks to other processes.
            var region = CreateRectRgn(0, 0, frame.Width, h);
            if (SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region);
        }
        if (moving) return;
        // preferences.X/Y are the saved card position; only FinishDrag writes them.
        var pos = ClampVisible(preferences.X, preferences.Y); var at = window.AppWindow.Position;
        if (pos.X != at.X || pos.Y != at.Y) SetWindowPos(hwnd, 0, pos.X, pos.Y, 0, 0, 0x1 | 0x4 | 0x10 | 0x200);
    }
    public void Clamp()
    {
        var pos = window.AppWindow.Position; var clamped = ClampVisible(pos.X, pos.Y);
        if (clamped.X != pos.X || clamped.Y != pos.Y) window.AppWindow.Move(clamped);
    }
    // Keeps the visible card (not the taller frame, which may hang below the screen) on the work area of the display it is on.
    private PointInt32 ClampVisible(int x, int y)
    {
        var size = new SizeInt32(frame.Width, Math.Max(1, (int)Math.Round(springHeight)));
        var area = DisplayArea.GetFromRect(new RectInt32(x, y, size.Width, size.Height), DisplayAreaFallback.Nearest).WorkArea;
        return new(Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - size.Width)), Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - size.Height)));
    }
    // Settings takes keyboard input: while it is open a click may activate the overlay. Otherwise it never takes the game's focus.
    public void SetActivatable(bool value)
    {
        activatable = value;
        if (preview) return; // --demo/--inspect never set WS_EX_NOACTIVATE.
        var style = GetWindowLongPtr(hwnd, -20).ToInt64();
        SetWindowLongPtr(hwnd, -20, (nint)(value ? style & ~0x08000000L : style | 0x08000000L));
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x1 | 0x2 | 0x4 | 0x10 | 0x20);
    }
    public void Activate() => SetForegroundWindow(hwnd);
    public void BeginDrag()
    {
        if (GameMode) return;
        GetCursorPos(out dragPointerOrigin);
        dragWindowOrigin = window.AppWindow.Position;
        dragX = targetX = dragWindowOrigin.X; dragY = targetY = dragWindowOrigin.Y;
        pointerDragging = moving = true;
        lastDragFrame = System.Diagnostics.Stopwatch.GetTimestamp();
        MoveStateChanged?.Invoke(true); dragTimer.Start();
    }
    public void UpdateDrag()
    {
        if (!pointerDragging) return;
        GetCursorPos(out var cursor);
        targetX = dragWindowOrigin.X + cursor.X - dragPointerOrigin.X;
        targetY = dragWindowOrigin.Y + cursor.Y - dragPointerOrigin.Y;
    }
    public void EndDrag() { if (!pointerDragging) return; UpdateDrag(); pointerDragging = false; }
    private void DragFrame()
    {
        // GetAsyncKeyState reads physical buttons; with SM_SWAPBUTTON the primary button is VK_RBUTTON (per the documented API).
        if (pointerDragging && (GetAsyncKeyState(GetSystemMetrics(23) != 0 ? 2 : 1) & 0x8000) == 0) EndDrag();
        // Sample once per frame, independent of the number of routed pointer events.
        UpdateDrag();
        var dt = System.Diagnostics.Stopwatch.GetElapsedTime(lastDragFrame).TotalSeconds;
        lastDragFrame = System.Diagnostics.Stopwatch.GetTimestamp();
        dragX = DragMath.Step(dragX, targetX, dt, preferences.ReducedMotion);
        dragY = DragMath.Step(dragY, targetY, dt, preferences.ReducedMotion);
        var x = (int)Math.Round(dragX); var y = (int)Math.Round(dragY);
        if (window.AppWindow.Position.X != x || window.AppWindow.Position.Y != y)
            SetWindowPos(hwnd, 0, x, y, 0, 0, 0x1 | 0x4 | 0x10 | 0x200);
        if (!pointerDragging && Math.Abs(dragX - targetX) < .4 && Math.Abs(dragY - targetY) < .4) FinishDrag();
    }
    private void FinishDrag()
    {
        dragTimer.Stop(); moving = false;
        // A header click without movement must not persist the display clamp (an expanded card near the bottom edge).
        var moved = !window.AppWindow.Position.Equals(dragWindowOrigin);
        Clamp();
        if (moved) { preferences.X = window.AppWindow.Position.X; preferences.Y = window.AppWindow.Position.Y; }
        Resize(logicalWidth, logicalHeight); if (moved) PositionChanged?.Invoke(); MoveStateChanged?.Invoke(false);
    }
    public void Show() { ShowWindow(hwnd, 4); SetWindowPos(hwnd, (nint)(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x40); KeepOnTop(); }
    private nint WndProc(nint h, uint msg, nuint w, nint l, nuint id, nuint data)
    {
        if (msg == taskbarCreated && taskbarCreated != 0) { Shell_NotifyIcon(0, ref tray); return 0; }
        if (msg == 0x83) return 0; // WM_NCCALCSIZE: no residual non-client frame.
        // WM_ERASEBKGND: the top-level window's GDI surface lies under the XAML island; black is alpha 0 with blur-behind on.
        if (msg == 0x14) { GetClientRect(h, out var rect); FillRect((nint)w, ref rect, GetStockObject(4)); return 1; }
        if (msg == 0x21) return activatable ? 1 : 3; // MA_ACTIVATE only while Settings is open; MA_NOACTIVATE: don't take the game's focus.
        if (msg == 0x312) { ToggleRequested?.Invoke(); return 0; }
        if (msg == 0x8001)
        {
            if ((uint)l == 0x202) Show(); // Left click = "Show overlay"; idempotent, so a double-click still just opens Settings.
            if ((uint)l == 0x203) SettingsRequested?.Invoke();
            if ((uint)l == 0x205)
            {
                var menu = CreatePopupMenu();
                AppendMenu(menu, 0, 1, GameMode ? "Exit game mode" : "Enter game mode");
                AppendMenu(menu, 0, 2, "Show overlay"); AppendMenu(menu, 0, 3, "Settings"); AppendMenu(menu, 0, 4, "Quit PrismPlay");
                // Documented TrackPopupMenu pattern for notification icons: foreground first (user just clicked the tray), WM_NULL after,
                // so a click outside dismisses the menu and the next one does not vanish immediately.
                GetCursorPos(out var p); SetForegroundWindow(hwnd);
                var command = TrackPopupMenu(menu, 0x100 | 0x2, p.X, p.Y, 0, hwnd, 0);
                PostMessage(hwnd, 0, 0, 0);
                DestroyMenu(menu);
                if (command == 1) ToggleRequested?.Invoke(); if (command == 2) Show();
                if (command == 3) SettingsRequested?.Invoke(); if (command == 4) QuitRequested?.Invoke();
            }
            return 0;
        }
        // Mid-drag, Resize leaves the frame but `frame` stays reset, so FinishDrag applies the new DPI's frame and region.
        if (msg is 0x2E0 or 0x7E) window.DispatcherQueue.TryEnqueue(() => { frame = default; Resize(logicalWidth, logicalHeight); KeepOnTop(); });
        return DefSubclassProc(h, msg, w, l);
    }
    public void Dispose() { dragTimer.Stop(); StopSpring(); if (hotkeyRegistered) UnregisterHotKey(hwnd, activeHotkey); Shell_NotifyIcon(2, ref tray); RemoveWindowSubclass(hwnd, callback, 1); }
    private delegate nint SubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam, nuint id, nuint data);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint h, out RECT rect);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref RECT rect, nint brush);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NOTIFYICONDATA
    {
        public uint cbSize; public nint hWnd; public uint uID, uFlags, uCallbackMessage; public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public nint hBalloonIcon;
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint h, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint h, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint h, int id);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint h, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint h, nint rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(nint h, uint msg, nuint w, nint l);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern nint LoadIcon(nint instance, nint name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint command, ref NOTIFYICONDATA data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint h, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint h, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint h, uint msg, nuint w, nint l);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint h, uint attribute, ref int value, int size);
    [StructLayout(LayoutKind.Sequential)] private struct MARGINS { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct DWM_BLURBEHIND { public uint dwFlags; public bool fEnable; public nint hRgnBlur; public bool fTransitionOnMaximized; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint h, ref MARGINS margins);
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(nint h, ref DWM_BLURBEHIND blur);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint h, nint region, bool redraw);
}
