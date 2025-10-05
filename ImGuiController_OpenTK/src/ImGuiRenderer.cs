using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ErrorCode = OpenTK.Graphics.OpenGL4.ErrorCode;


namespace ImGuiController_OpenTK;

public interface IImGuiRenderer : IDisposable
{
    /// <summary>
    /// Makes all GL calls necessary to render ImGui into a window context
    /// </summary>
    public void Render();
}

public sealed class ImGuiRenderer : IImGuiRenderer
{
    public readonly int GLVersion;
    public readonly bool CompatibilityProfile;

    public ImGuiRenderer(ImGuiViewportPtr viewport, NativeWindow mainWindow)
    {
        int major = GL.GetInteger(GetPName.MajorVersion);
        int minor = GL.GetInteger(GetPName.MinorVersion);
        GLVersion = major * 100 + minor * 10;
        CompatibilityProfile = (GL.GetInteger((GetPName)All.ContextProfileMask) & (int)All.ContextCompatibilityProfileBit) != 0;

        mMain = true;
        mViewport = viewport;
        mWindow = mainWindow;

        CreateDeviceNotSharedResources(mainWindow.Context);
        CreateDeviceSharedResources(mainWindow.Context);
    }
    public ImGuiRenderer(ImGuiRenderer mainRenderer, ImGuiViewportPtr viewport, NativeWindow secondaryWindow)
    {
        int major = GL.GetInteger(GetPName.MajorVersion);
        int minor = GL.GetInteger(GetPName.MinorVersion);
        GLVersion = major * 100 + minor * 10;
        CompatibilityProfile = (GL.GetInteger((GetPName)All.ContextProfileMask) & (int)All.ContextCompatibilityProfileBit) != 0;

        mViewport = viewport;
        mWindow = secondaryWindow;
        mFontTexture = mainRenderer.mFontTexture;
        mShader = mainRenderer.mShader;
        mShaderFontTextureLocation = mainRenderer.mShaderFontTextureLocation;
        mShaderProjectionMatrixLocation = mainRenderer.mShaderProjectionMatrixLocation;

        CreateDeviceNotSharedResources(secondaryWindow.Context);
    }

    public void Render()
    {
        using (new SaveGLState(GLVersion, CompatibilityProfile))
        {
            RenderImDrawData(mViewport.DrawData, mWindow);
        }
    }

    #region Disposing
    private bool disposedValue;
    public void Dispose()
    {
        if (disposedValue) return;
        disposedValue = true;

        GL.DeleteVertexArray(mVertexArray);
        GL.DeleteBuffer(mVertexBuffer);
        GL.DeleteBuffer(mIndexBuffer);

        if (!mMain) return;
        GL.DeleteTexture(mFontTexture);
        GL.DeleteProgram(mShader);

        GC.SuppressFinalize(this);
    }
    #endregion

    #region Private fields
    private readonly ImGuiViewportPtr mViewport;
    private readonly NativeWindow mWindow;

    private readonly bool mMain = false;
    private int mVertexArray;
    private int mVertexBuffer;
    private int mVertexBufferSize;
    private int mIndexBuffer;
    private int mIndexBufferSize;

    private int mFontTexture;
    private int mShader;
    private int mShaderFontTextureLocation;
    private int mShaderProjectionMatrixLocation;
    #endregion

    #region Creating device resources
    private void CreateDeviceNotSharedResources(IGLFWGraphicsContext context)
    {
        context.MakeCurrent();

        mVertexBufferSize = 10000;
        mIndexBufferSize = 2000;

        int prevVAO = GL.GetInteger(GetPName.VertexArrayBinding);
        int prevArrayBuffer = GL.GetInteger(GetPName.ArrayBufferBinding);

        mVertexArray = GL.GenVertexArray();
        GL.BindVertexArray(mVertexArray);

        mVertexBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, mVertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, mVertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        mIndexBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, mIndexBuffer);
        GL.BufferData(BufferTarget.ElementArrayBuffer, mIndexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        int stride = Unsafe.SizeOf<ImDrawVert>();
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 8);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, 16);

        GL.EnableVertexAttribArray(0);
        GL.EnableVertexAttribArray(1);
        GL.EnableVertexAttribArray(2);

        GL.BindVertexArray(prevVAO);
        GL.BindBuffer(BufferTarget.ArrayBuffer, prevArrayBuffer);
    }

    private void CreateDeviceSharedResources(IGLFWGraphicsContext context)
    {
        context.MakeCurrent();

        RecreateFontDeviceTexture();

        (mShader, mShaderProjectionMatrixLocation, mShaderFontTextureLocation) = ImGuiShader.Create();

        CheckGLError("End of ImGui setup");
    }

    private void RecreateFontDeviceTexture()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

        int mips = (int)Math.Floor(Math.Log(Math.Max(width, height), 2));

        int prevActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
        GL.ActiveTexture(TextureUnit.Texture0);
        int prevTexture2D = GL.GetInteger(GetPName.TextureBinding2D);

        mFontTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, mFontTexture);
        GL.TexStorage2D(TextureTarget2d.Texture2D, mips, SizedInternalFormat.Rgba8, width, height);

        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, mips - 1);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);

        // Restore state
        GL.BindTexture(TextureTarget.Texture2D, prevTexture2D);
        GL.ActiveTexture((TextureUnit)prevActiveTexture);

        io.Fonts.SetTexID(mFontTexture);

        io.Fonts.ClearTexData();
    }
    #endregion

    #region Rendering
    private void RenderImDrawData(ImDrawDataPtr drawData, NativeWindow window)
    {
        if (drawData.CmdListsCount == 0) return;

        ResizeBuffers(drawData);
        SetupOrthographicProjection(window, drawData);

        ImGuiIOPtr io = ImGui.GetIO();
        drawData.ScaleClipRects(io.DisplayFramebufferScale);

        GL.Enable(EnableCap.Blend);
        GL.Enable(EnableCap.ScissorTest);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);

        for (int commandListIndex = 0; commandListIndex < drawData.CmdListsCount; commandListIndex++)
        {
            RenderCommandList(drawData, window, commandListIndex, io);
        }

        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.ScissorTest);
    }

    private void ResizeBuffers(ImDrawDataPtr drawData)
    {
        GL.BindVertexArray(mVertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, mVertexBuffer);

        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr cmd_list = drawData.CmdLists[i];

            int vertexSize = cmd_list.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>();
            if (vertexSize > mVertexBufferSize)
            {
                int newSize = (int)Math.Max(mVertexBufferSize * 1.5f, vertexSize);

                GL.BufferData(BufferTarget.ArrayBuffer, newSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
                mVertexBufferSize = newSize;
            }

            int indexSize = cmd_list.IdxBuffer.Size * sizeof(ushort);
            if (indexSize > mIndexBufferSize)
            {
                int newSize = (int)Math.Max(mIndexBufferSize * 1.5f, indexSize);
                GL.BufferData(BufferTarget.ElementArrayBuffer, newSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
                mIndexBufferSize = newSize;
            }
        }
    }
    private void RenderCommandList(ImDrawDataPtr drawData, NativeWindow window, int index, ImGuiIOPtr io)
    {
        ImDrawListPtr commandList = drawData.CmdLists[index];

        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, commandList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>(), commandList.VtxBuffer.Data);
        CheckGLError($"Data Vert {index}");

        GL.BufferSubData(BufferTarget.ElementArrayBuffer, IntPtr.Zero, commandList.IdxBuffer.Size * sizeof(ushort), commandList.IdxBuffer.Data);
        CheckGLError($"Data Idx {index}");

        for (int cmd_i = 0; cmd_i < commandList.CmdBuffer.Size; cmd_i++)
        {
            ImDrawCmdPtr command = commandList.CmdBuffer[cmd_i];
            if (command.UserCallback != IntPtr.Zero)
            {
                throw new NotImplementedException();
            }
            else
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, (int)command.TextureId);
                CheckGLError("Texture");

                System.Numerics.Vector4 clip = command.ClipRect;
                SetupScissor(window, clip);

                if ((io.BackendFlags & ImGuiBackendFlags.RendererHasVtxOffset) != 0)
                {
                    GL.DrawElementsBaseVertex(PrimitiveType.Triangles, (int)command.ElemCount, DrawElementsType.UnsignedShort, (IntPtr)(command.IdxOffset * sizeof(ushort)), unchecked((int)command.VtxOffset));
                }
                else
                {
                    GL.DrawElements(BeginMode.Triangles, (int)command.ElemCount, DrawElementsType.UnsignedShort, (int)command.IdxOffset * sizeof(ushort));
                }
                CheckGLError("Draw");
            }
        }
    }
    private static void SetupScissor(NativeWindow window, System.Numerics.Vector4 clip)
    {
        int posX = window.ClientLocation.X;
        int posY = window.ClientLocation.Y + window.ClientSize.Y;

        int x = (int)clip.X - posX;
        int y = posY - (int)clip.W;
        int width = (int)(clip.Z - clip.X);
        int height = (int)(clip.W - clip.Y);

        GL.Scissor(x, y, width, height);
        CheckGLError("Scissor");
    }
    private void SetupOrthographicProjection(NativeWindow window, ImDrawDataPtr drawData)
    {
        Matrix4 mvp = Matrix4.CreateOrthographicOffCenter(
            window.ClientLocation.X,
            window.ClientLocation.X + drawData.DisplaySize.X,
            window.ClientLocation.Y + drawData.DisplaySize.Y,
            window.ClientLocation.Y,
            -1.0f,
            1.0f);

        GL.UseProgram(mShader);
        GL.UniformMatrix4(mShaderProjectionMatrixLocation, false, ref mvp);
        GL.Uniform1(mShaderFontTextureLocation, 0);
        CheckGLError("Projection");
    }
    #endregion

    private static void CheckGLError(string title)
    {
        ErrorCode error;
        int i = 1;
        while ((error = GL.GetError()) != ErrorCode.NoError)
        {
            Debug.Print($"{title} ({i++}): {error}");
        }
    }
}

internal static class ImGuiShader
{
    public static (int shader, int projectionMatrix, int fontTexture) Create()
    {
        int shader = CreateProgram("ImGui", cVertexShaderSource, cFragmentShaderSource);
        int projectionMatrix = GL.GetUniformLocation(shader, "projection_matrix");
        int fontTexture = GL.GetUniformLocation(shader, "in_fontTexture");

        return (shader, projectionMatrix, fontTexture);
    }

    #region Shaders code

    private const string cVertexShaderSource = @"#version 330 core

uniform mat4 projection_matrix;

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_texCoord;
layout(location = 2) in vec4 in_color;

out vec4 color;
out vec2 texCoord;

void main()
{
    gl_Position = projection_matrix * vec4(in_position, 0, 1);
    color = in_color;
    texCoord = in_texCoord;
}";
    private const string cFragmentShaderSource = @"#version 330 core

uniform sampler2D in_fontTexture;

in vec4 color;
in vec2 texCoord;

out vec4 outputColor;

void main()
{
    outputColor = color * texture(in_fontTexture, texCoord);
}";

    #endregion

    private static int CreateProgram(string name, string vertexSource, string fragmentSource)
    {
        int program = GL.CreateProgram();

        int vertex = CompileShader(name, ShaderType.VertexShader, vertexSource);
        int fragment = CompileShader(name, ShaderType.FragmentShader, fragmentSource);

        GL.AttachShader(program, vertex);
        GL.AttachShader(program, fragment);

        GL.LinkProgram(program);

        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int success);
        if (success == 0)
        {
            string info = GL.GetProgramInfoLog(program);
            Debug.WriteLine($"GL.LinkProgram had info log [{name}]:\n{info}");
        }

        GL.DetachShader(program, vertex);
        GL.DetachShader(program, fragment);

        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);

        return program;
    }

    private static int CompileShader(string name, ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);

        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);

        GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
        if (success == 0)
        {
            string info = GL.GetShaderInfoLog(shader);
            Debug.WriteLine($"GL.CompileShader for shader '{name}' [{type}] had info log:\n{info}");
        }

        return shader;
    }
}

internal struct SaveGLState : IDisposable
{
    private bool disposedValue = false;

    private readonly int prevVAO = GL.GetInteger(GetPName.VertexArrayBinding);
    private readonly int prevArrayBuffer = GL.GetInteger(GetPName.ArrayBufferBinding);
    private readonly int prevProgram = GL.GetInteger(GetPName.CurrentProgram);
    private readonly bool prevBlendEnabled = GL.GetBoolean(GetPName.Blend);
    private readonly bool prevScissorTestEnabled = GL.GetBoolean(GetPName.ScissorTest);
    private readonly int prevBlendEquationRgb = GL.GetInteger(GetPName.BlendEquationRgb);
    private readonly int prevBlendEquationAlpha = GL.GetInteger(GetPName.BlendEquationAlpha);
    private readonly int prevBlendFuncSrcRgb = GL.GetInteger(GetPName.BlendSrcRgb);
    private readonly int prevBlendFuncSrcAlpha = GL.GetInteger(GetPName.BlendSrcAlpha);
    private readonly int prevBlendFuncDstRgb = GL.GetInteger(GetPName.BlendDstRgb);
    private readonly int prevBlendFuncDstAlpha = GL.GetInteger(GetPName.BlendDstAlpha);
    private readonly bool prevCullFaceEnabled = GL.GetBoolean(GetPName.CullFace);
    private readonly bool prevDepthTestEnabled = GL.GetBoolean(GetPName.DepthTest);
    private readonly int prevActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
    private readonly int prevTexture2D = GL.GetInteger(GetPName.TextureBinding2D);
    private readonly int[] prevScissorBox = GetScissorBox();
    private readonly int[] prevPolygonMode = GetPolygonMode();

    private readonly int GLVersion;
    private readonly bool CompatibilityProfile;

    public SaveGLState(int GLVersion, bool CompatibilityProfile)
    {
        this.GLVersion = GLVersion;
        this.CompatibilityProfile = CompatibilityProfile;

        GL.ActiveTexture(TextureUnit.Texture0);

        if (GLVersion <= 310 || CompatibilityProfile)
        {
            GL.PolygonMode(MaterialFace.Front, PolygonMode.Fill);
            GL.PolygonMode(MaterialFace.Back, PolygonMode.Fill);
        }
        else
        {
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
        }
    }

    private static int[] GetScissorBox()
    {
        Span<int> prevScissorBoxPtr = stackalloc int[4];
        unsafe
        {
            fixed (int* iptr = &prevScissorBoxPtr[0])
            {
                GL.GetInteger(GetPName.ScissorBox, iptr);
            }
        }
        return prevScissorBoxPtr.ToArray();
    }
    private static int[] GetPolygonMode()
    {
        Span<int> prevPolygonModePtr = stackalloc int[2];
        unsafe
        {
            fixed (int* iptr = &prevPolygonModePtr[0])
            {
                GL.GetInteger(GetPName.PolygonMode, iptr);
            }
        }
        return prevPolygonModePtr.ToArray();
    }

    public void Dispose()
    {
        if (disposedValue) return;
        disposedValue = true;
        GC.SuppressFinalize(this);

        GL.BindTexture(TextureTarget.Texture2D, prevTexture2D);
        GL.ActiveTexture((TextureUnit)prevActiveTexture);
        GL.UseProgram(prevProgram);
        GL.BindVertexArray(prevVAO);
        GL.Scissor(prevScissorBox[0], prevScissorBox[1], prevScissorBox[2], prevScissorBox[3]);
        GL.BindBuffer(BufferTarget.ArrayBuffer, prevArrayBuffer);
        GL.BlendEquationSeparate((BlendEquationMode)prevBlendEquationRgb, (BlendEquationMode)prevBlendEquationAlpha);
        GL.BlendFuncSeparate(
            (BlendingFactorSrc)prevBlendFuncSrcRgb,
            (BlendingFactorDest)prevBlendFuncDstRgb,
            (BlendingFactorSrc)prevBlendFuncSrcAlpha,
            (BlendingFactorDest)prevBlendFuncDstAlpha);
        if (prevBlendEnabled) GL.Enable(EnableCap.Blend); else GL.Disable(EnableCap.Blend);
        if (prevDepthTestEnabled) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
        if (prevCullFaceEnabled) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
        if (prevScissorTestEnabled) GL.Enable(EnableCap.ScissorTest); else GL.Disable(EnableCap.ScissorTest);

        if (GLVersion <= 310 || CompatibilityProfile)
        {
            GL.PolygonMode(MaterialFace.Front, (PolygonMode)prevPolygonMode[0]);
            GL.PolygonMode(MaterialFace.Back, (PolygonMode)prevPolygonMode[1]);
        }
        else
        {
            GL.PolygonMode(MaterialFace.FrontAndBack, (PolygonMode)prevPolygonMode[0]);
        }
    }
}