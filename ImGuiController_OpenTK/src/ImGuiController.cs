using ImGuiNET;
using ImGuizmoNET;
using ImPlotNET;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ImGuiController_OpenTK;

public class ImGuiController : IDisposable
{
    public ImGuiController(IWindow mainWindow, bool multiViewport = true)
    {
        SetImGuiContext();
        SetImGuiParameters(multiViewport);

        ImGuiPlatformIOPtr platformIO = ImGui.GetPlatformIO();
        mMainViewport = platformIO.Viewports[0];
        mMainImGuiRenderer = new(mMainViewport, mainWindow.Native);
        mMainWindow = new MainWindowWrapper(mainWindow, mMainImGuiRenderer);
        mWindowsManager = new(CreateWindow, mMainViewport, mMainWindow);

        mMainWindow.Native.MouseWheel += OnMouseScroll;
        mMainWindow.Native.TextInput += OnTextInput;

        SetPerFrameImGuiData(1f / 60f, mMainWindow.Native);
        UpdateMonitors();
    }

    public virtual void Render(float deltaSeconds, bool mainWindowIncluded = true)
    {
        ImGui.UpdatePlatformWindows();

        List<IImGuiWindow> windows = GetWindows(mainWindowIncluded);

        foreach (IImGuiWindow window in windows)
        {
            MonitorInfo monitor = Monitors.GetMonitorFromWindow(window.Native);
            window.Viewport.Pos = new System.Numerics.Vector2(monitor.ClientArea.Min.X, monitor.ClientArea.Min.Y);
            window.Viewport.Size = new System.Numerics.Vector2(monitor.ClientArea.Size.X, monitor.ClientArea.Size.Y);
            window.OnUpdate(deltaSeconds);
        }

        UpdateImGuiInput(mainWindowIncluded);
        SetPerFrameImGuiData(deltaSeconds, mMainWindow.Native);

        UpdateMonitors();
        ImGui.NewFrame();

        foreach (IImGuiWindow window in windows)
        {
            window.OnDraw(deltaSeconds);
        }

        ImGui.Render();

        foreach (IImGuiWindow window in windows)
        {
            window.ContextMakeCurrent();
            window.OnRender(deltaSeconds);
            window.ImGuiRenderer.Render();
            window.SwapBuffers();
        }
    }

    public bool CanUpdateInputs { get; set; } = true;

    protected readonly ImGuiWindowsManager mWindowsManager;
    protected readonly IImGuiWindow mMainWindow;
    protected readonly ImGuiViewportPtr mMainViewport;
    protected readonly ImGuiRenderer mMainImGuiRenderer;

    protected List<IImGuiWindow> GetWindows(bool includeMain = true)
    {
        List<IImGuiWindow> windows = new();

        ImVector<ImGuiViewportPtr> viewports = ImGui.GetPlatformIO().Viewports;
        for (int index = 0; index < viewports.Size; index++)
        {
            ImGuiViewportPtr viewport = viewports[index];
            IImGuiWindow? window = mWindowsManager.GetWindow(viewport);
            unsafe
            {
                if (window == null || (!includeMain && viewport.NativePtr == mMainViewport.NativePtr)) continue;
            }

            windows.Add(window);
        }

        return windows;
    }

    protected virtual void LoadFonts()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.AddFontDefault();
    }

    private IImGuiWindow CreateWindow(ImGuiViewportPtr viewport)
    {
        return new ImGuiWindow(viewport, mMainWindow.Native, mMainImGuiRenderer, this);
    }
    private void SetImGuiParameters(bool multiViewport = true)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        LoadFonts();
        if (multiViewport)
        {
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
        }

        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
        io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;
        io.BackendFlags |= ImGuiBackendFlags.PlatformHasViewports;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasViewports;
    }
    private static void SetImGuiContext()
    {
        IntPtr context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);

        IntPtr plotContext = ImPlot.CreateContext();
        ImPlot.SetCurrentContext(plotContext);
        ImPlot.SetImGuiContext(context);

        ImGuizmo.SetImGuiContext(context);

        IntPtr nodesContext = imnodesNET.imnodes.CreateContext();
        imnodesNET.imnodes.SetCurrentContext(nodesContext);
        imnodesNET.imnodes.SetImGuiContext(context);
    }

    #region Updating
    public List<char> PressedCharacters { get; } = new();

    public void OnTextInput(TextInputEventArgs args)
    {
        PressedCharacters.Add((char)args.Unicode);
    }
    public void OnMouseScroll(MouseWheelEventArgs args)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.MouseWheel = args.Offset.Y;
        io.MouseWheelH = args.Offset.X;
    }

    protected static void SetPerFrameImGuiData(float deltaSeconds, NativeWindow window)
    {
        //MonitorInfo monitor = Monitors.GetMonitorFromWindow(window);
        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new System.Numerics.Vector2(
                window.ClientSize.X / 1,
                window.ClientSize.Y / 1);
        io.DisplayFramebufferScale = new System.Numerics.Vector2(1, 1);
        io.DeltaTime = deltaSeconds;
    }
    protected static void UpdateMonitors()
    {
        ImGuiPlatformIOPtr platformIO = ImGui.GetPlatformIO();
        List<MonitorInfo> monitors = Monitors.GetMonitors();
        IntPtr data = Marshal.AllocHGlobal(Unsafe.SizeOf<ImGuiPlatformMonitor>() * monitors.Count);

        unsafe
        {
            Marshal.FreeHGlobal(platformIO.NativePtr->Monitors.Data);
            platformIO.NativePtr->Monitors = new ImVector(monitors.Count, monitors.Count, data);
        }

        for (int i = 0; i < monitors.Count; i++)
        {
            Box2i clientArea = monitors[i].ClientArea;
            ImGuiPlatformMonitorPtr monitor = platformIO.Monitors[i];

            monitor.DpiScale = 1f;
            monitor.MainPos = new System.Numerics.Vector2(clientArea.Min.X, clientArea.Min.Y);
            monitor.MainSize = new System.Numerics.Vector2(clientArea.Size.X, clientArea.Size.Y);
            monitor.WorkPos = new System.Numerics.Vector2(clientArea.Min.X, clientArea.Min.Y);
            monitor.WorkSize = new System.Numerics.Vector2(clientArea.Size.X, clientArea.Size.Y);
        }
    }


    protected void CollectKeysInputs(bool mainWindowIncluded = true)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        bool mouseLeft = false;
        bool mouseRight = false;
        bool mouseMiddle = false;
        bool mouseButton4 = false;
        bool mouseButton5 = false;
        bool keyCtrl = false;
        bool keyAlt = false;
        bool keyShift = false;
        bool keySuper = false;

        foreach (NativeWindow window in GetWindows(mainWindowIncluded).Select(window => window.Native))
        {
            MouseState mouseState = window.MouseState;
            KeyboardState keyboardState = window.KeyboardState;

            mouseLeft |= mouseState[MouseButton.Left];
            mouseRight |= mouseState[MouseButton.Right];
            mouseMiddle |= mouseState[MouseButton.Middle];
            mouseButton4 |= mouseState[MouseButton.Button4];
            mouseButton5 |= mouseState[MouseButton.Button5];
            keyCtrl |= keyboardState.IsKeyDown(Keys.LeftControl) || keyboardState.IsKeyDown(Keys.RightControl);
            keyAlt |= keyboardState.IsKeyDown(Keys.LeftAlt) || keyboardState.IsKeyDown(Keys.RightAlt);
            keyShift |= keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift);
            keySuper |= keyboardState.IsKeyDown(Keys.LeftSuper) || keyboardState.IsKeyDown(Keys.RightSuper);

            foreach (Keys key in Enum.GetValues(typeof(Keys)))
            {
                if (key == Keys.Unknown)
                {
                    continue;
                }
                io.AddKeyEvent(TranslateKey(key), keyboardState.IsKeyDown(key));
            }
        }

        io.MouseDown[0] = mouseLeft;
        io.MouseDown[1] = mouseRight;
        io.MouseDown[2] = mouseMiddle;
        io.MouseDown[3] = mouseButton4;
        io.MouseDown[4] = mouseButton5;
        io.KeyCtrl = keyCtrl;
        io.KeyAlt = keyAlt;
        io.KeyShift = keyShift;
        io.KeySuper = keySuper;
    }

    protected System.Numerics.Vector2 CollectMousePosition(bool outsideOfMainWinodw = false)
    {
        NativeWindow mainWindow = mMainWindow.Native;
        Vector2i screenPoint = new((int)mainWindow.MouseState.X, (int)mainWindow.MouseState.Y);
        Vector2i point = mainWindow.ClientLocation + screenPoint;
        return new(point.X, point.Y);
    }

    protected virtual void UpdateImGuiInput(bool mainWindowIncluded = true)
    {
        if (!CanUpdateInputs)
        {
            PressedCharacters.Clear();
            return;
        }
        
        ImGuiIOPtr io = ImGui.GetIO();

        CollectKeysInputs(mainWindowIncluded);

        io.MousePos = CollectMousePosition(!mainWindowIncluded);

        foreach (char character in PressedCharacters)
        {
            io.AddInputCharacter(character);
        }
        PressedCharacters.Clear();
    }
    public static ImGuiKey TranslateKey(Keys key)
    {
        if (key >= Keys.D0 && key <= Keys.D9)
            return key - Keys.D0 + ImGuiKey._0;

        if (key >= Keys.A && key <= Keys.Z)
            return key - Keys.A + ImGuiKey.A;

        if (key >= Keys.KeyPad0 && key <= Keys.KeyPad9)
            return key - Keys.KeyPad0 + ImGuiKey.Keypad0;

        if (key >= Keys.F1 && key <= Keys.F12)
            return key - Keys.F1 + ImGuiKey.F12;

        switch (key)
        {
            case Keys.Tab: return ImGuiKey.Tab;
            case Keys.Left: return ImGuiKey.LeftArrow;
            case Keys.Right: return ImGuiKey.RightArrow;
            case Keys.Up: return ImGuiKey.UpArrow;
            case Keys.Down: return ImGuiKey.DownArrow;
            case Keys.PageUp: return ImGuiKey.PageUp;
            case Keys.PageDown: return ImGuiKey.PageDown;
            case Keys.Home: return ImGuiKey.Home;
            case Keys.End: return ImGuiKey.End;
            case Keys.Insert: return ImGuiKey.Insert;
            case Keys.Delete: return ImGuiKey.Delete;
            case Keys.Backspace: return ImGuiKey.Backspace;
            case Keys.Space: return ImGuiKey.Space;
            case Keys.Enter: return ImGuiKey.Enter;
            case Keys.Escape: return ImGuiKey.Escape;
            case Keys.Apostrophe: return ImGuiKey.Apostrophe;
            case Keys.Comma: return ImGuiKey.Comma;
            case Keys.Minus: return ImGuiKey.Minus;
            case Keys.Period: return ImGuiKey.Period;
            case Keys.Slash: return ImGuiKey.Slash;
            case Keys.Semicolon: return ImGuiKey.Semicolon;
            case Keys.Equal: return ImGuiKey.Equal;
            case Keys.LeftBracket: return ImGuiKey.LeftBracket;
            case Keys.Backslash: return ImGuiKey.Backslash;
            case Keys.RightBracket: return ImGuiKey.RightBracket;
            case Keys.GraveAccent: return ImGuiKey.GraveAccent;
            case Keys.CapsLock: return ImGuiKey.CapsLock;
            case Keys.ScrollLock: return ImGuiKey.ScrollLock;
            case Keys.NumLock: return ImGuiKey.NumLock;
            case Keys.PrintScreen: return ImGuiKey.PrintScreen;
            case Keys.Pause: return ImGuiKey.Pause;
            case Keys.KeyPadDecimal: return ImGuiKey.KeypadDecimal;
            case Keys.KeyPadDivide: return ImGuiKey.KeypadDivide;
            case Keys.KeyPadMultiply: return ImGuiKey.KeypadMultiply;
            case Keys.KeyPadSubtract: return ImGuiKey.KeypadSubtract;
            case Keys.KeyPadAdd: return ImGuiKey.KeypadAdd;
            case Keys.KeyPadEnter: return ImGuiKey.KeypadEnter;
            case Keys.KeyPadEqual: return ImGuiKey.KeypadEqual;
            case Keys.LeftShift: return ImGuiKey.LeftShift;
            case Keys.LeftControl: return ImGuiKey.LeftCtrl;
            case Keys.LeftAlt: return ImGuiKey.LeftAlt;
            case Keys.LeftSuper: return ImGuiKey.LeftSuper;
            case Keys.RightShift: return ImGuiKey.RightShift;
            case Keys.RightControl: return ImGuiKey.RightCtrl;
            case Keys.RightAlt: return ImGuiKey.RightAlt;
            case Keys.RightSuper: return ImGuiKey.RightSuper;
            case Keys.Menu: return ImGuiKey.Menu;
            default: return ImGuiKey.None;
        }
    }
    #endregion

    #region Disposing
    private bool mDisposed;
    protected virtual void Dispose(bool disposing)
    {
        if (mDisposed) return;
        if (disposing)
        {
            mWindowsManager.Dispose();
            mMainImGuiRenderer.Dispose();
        }

        mDisposed = true;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    #endregion
}

internal sealed class MainWindowWrapper : IImGuiWindow
{
    public NativeWindow Native => mMainWindow.Native;
    public IImGuiRenderer ImGuiRenderer { get; }
    public ImGuiViewportPtr Viewport { get; }

    public MainWindowWrapper(IWindow window, IImGuiRenderer renderer)
    {
        mMainWindow = window;
        ImGuiPlatformIOPtr platformIO = ImGui.GetPlatformIO();
        Viewport = platformIO.Viewports[0];
        ImGuiRenderer = renderer;
    }

    public void OnDraw(float deltaSeconds) => mMainWindow.OnDraw(deltaSeconds);
    public void OnRender(float deltaSeconds) => mMainWindow.OnRender(deltaSeconds);
    public void OnUpdate(float deltaSeconds) => mMainWindow.OnUpdate(deltaSeconds);
    public void ContextMakeCurrent() => mMainWindow.ContextMakeCurrent();
    public void SwapBuffers() => mMainWindow.SwapBuffers();

    private readonly IWindow mMainWindow;

    public void Dispose()
    {
        // nothing to dispose
    }
}
