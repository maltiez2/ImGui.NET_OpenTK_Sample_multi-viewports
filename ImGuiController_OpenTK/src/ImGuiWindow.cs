using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;


namespace ImGuiController_OpenTK;

public interface IWindow : IDisposable
{
    /// <summary>
    /// Window that holds context where ImGui widgets will be rendered
    /// </summary>
    NativeWindow Native { get; }
    /// <summary>
    /// All window rendering that should be done after making its context current but before any imgui GL calls, always called once after <see cref="OnDraw"/>
    /// </summary>
    /// <param name="deltaSeconds"></param>
    void OnRender(float deltaSeconds);
    /// <summary>
    /// All ImGui widgets draw calls should be in here, always called once after <see cref="OnUpdate"/> and before <see cref="OnRender"/>
    /// </summary>
    /// <param name="deltaSeconds"></param>
    void OnDraw(float deltaSeconds);
    /// <summary>
    /// All inputs update in window should be done in this method or before it is called, always called once before <see cref="OnDraw"/>
    /// </summary>
    /// <param name="deltaSeconds"></param>
    void OnUpdate(float deltaSeconds);
    /// <summary>
    /// Called directly after making all InGui calls and before switching to next context, always called once after <see cref="OnRender"/>
    /// </summary>
    void SwapBuffers();
    /// <summary>
    /// Sets current context to one owned by this window
    /// </summary>
    void ContextMakeCurrent();
}
public interface IImGuiWindow : IWindow
{
    /// <summary>
    /// Object that makes all GL calls necessary to render ImGui
    /// </summary>
    IImGuiRenderer ImGuiRenderer { get; }
    /// <summary>
    /// Associated with this window viewport
    /// </summary>
    ImGuiViewportPtr Viewport { get; }
}

public class ImGuiWindow : GameWindow, IImGuiWindow
{
    public NativeWindow Native => this;
    public IImGuiRenderer ImGuiRenderer => mImGuiRenderer;
    public ImGuiViewportPtr Viewport => mViewport;
    public void ContextMakeCurrent() => Context.MakeCurrent();

    public ImGuiWindow(ImGuiViewportPtr viewport, NativeWindow mainWindow, ImGuiRenderer renderer, ImGuiController controller) : base(GameWindowSettings.Default, new NativeWindowSettings()
    {
        SharedContext = mainWindow.Context,
        WindowBorder = GetBorderSettings(viewport),
        StartVisible = false,
        Location = new Vector2i((int)viewport.Pos.X, (int)viewport.Pos.Y),
        Size = new Vector2i((int)viewport.Size.X, (int)viewport.Size.Y),
        APIVersion = new Version(3, 3)
    })
    {
        mViewport = viewport;
        GCHandle gcHandle = GCHandle.Alloc(this);
        mViewport.PlatformUserData = (IntPtr)gcHandle;
        mImGuiRenderer = new(renderer, viewport, this);

        Resize += _ => mViewport.PlatformRequestResize = true;
        Move += _ => mViewport.PlatformRequestMove = true;
        Closing += _ => mViewport.PlatformRequestClose = true;

        MouseWheel += controller.OnMouseScroll;
        TextInput += controller.OnTextInput;

        //SetMouseButton(MouseButton.Left, true);
    }
    public void OnRender(float deltaSeconds)
    {
        base.OnRenderFrame(new FrameEventArgs(deltaSeconds));

        GL.ClearColor(new Color4(0, 32, 48, 255));
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
    }
    public void OnUpdate(float deltaSeconds)
    {
        NewInputFrame();
        ProcessWindowEvents(IsEventDriven);
        UpdateTime = deltaSeconds;
    }
    public void OnDraw(float deltaSeconds)
    {

    }

    public void SetMouseButton(MouseButton button, bool value)
    {
        MouseState state = MouseState;
        FieldInfo? fieldInfo = typeof(MouseState).GetField("_buttons", BindingFlags.NonPublic | BindingFlags.Instance);
        BitArray? buttons = (BitArray?)fieldInfo?.GetValue(state);
        if (buttons == null) return;
        buttons[(int)button] = value;
    }
    public void ReleaseLeftMouseButton()
    {
        SetMouseButton(MouseButton.Left, false);
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);

        unsafe
        {
            Window* context = GLFW.GetCurrentContext();
            Native.Context.MakeCurrent();
            GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
            GLFW.MakeContextCurrent(context);
        }

    }

    protected readonly ImGuiRenderer mImGuiRenderer;
    protected readonly ImGuiViewportPtr mViewport;
    protected static WindowBorder GetBorderSettings(ImGuiViewportPtr viewport)
    {
        if ((viewport.Flags & ImGuiViewportFlags.NoDecoration) != 0)
        {
            return WindowBorder.Hidden;
        }

        return WindowBorder.Resizable;
    }
}
