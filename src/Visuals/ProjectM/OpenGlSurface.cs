using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pulsive;

// WGL pbuffers render offscreen without adding a visible child HWND to WPF.
internal sealed class OpenGlSurface : IDisposable
{
    IntPtr window,windowDc,context,pbuffer,bufferDc;
    CreatePbuffer createBuffer=null!;
    GetPbufferDc getBufferDc=null!;
    ReleasePbufferDc releaseBufferDc=null!;
    DestroyPbuffer destroyBuffer=null!;
    BindFramebuffer bindFramebuffer=null!;
    int pixelFormat;
    internal int Width { get; private set; }
    internal int Height { get; private set; }
    internal string Version { get; private set; }="";
    internal OpenGlSurface(int width,int height)
    {
        try
        {
            window=CreateWindowEx(0,"STATIC","pulsive offscreen OpenGL",0x80000000,0,0,1,1,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
            if(window==IntPtr.Zero) throw new Win32Exception();
            windowDc=GetDC(window);
            var format=new PixelFormat { Size=(ushort)Marshal.SizeOf<PixelFormat>(),Version=1,Flags=0x25,ColourBits=32,AlphaBits=8 };
            int basic=ChoosePixelFormat(windowDc,ref format);
            if(basic==0||!SetPixelFormat(windowDc,basic,ref format)) throw new Win32Exception();
            context=wglCreateContext(windowDc);
            if(context==IntPtr.Zero||!wglMakeCurrent(windowDc,context)) throw new InvalidOperationException("OpenGL could not be initialised.");
            var choose=Function<ChooseFormat>("wglChoosePixelFormatARB");
            var createContext=Function<CreateContext>("wglCreateContextAttribsARB");
            createBuffer=Function<CreatePbuffer>("wglCreatePbufferARB");
            getBufferDc=Function<GetPbufferDc>("wglGetPbufferDCARB");
            releaseBufferDc=Function<ReleasePbufferDc>("wglReleasePbufferDCARB");
            destroyBuffer=Function<DestroyPbuffer>("wglDestroyPbufferARB");
            if(!choose(windowDc,[0x202D,1,0x2010,1,0x2013,0x202B,0x2014,32,0x201B,8,0x2011,0,0],null,1,out pixelFormat,out uint count)||count==0)
                throw new InvalidOperationException("Offscreen OpenGL is not supported by this graphics driver.");
            pbuffer=createBuffer(windowDc,pixelFormat,width,height,[0]);
            if(pbuffer==IntPtr.Zero) throw new InvalidOperationException("Could not allocate the OpenGL surface.");
            bufferDc=getBufferDc(pbuffer);
            IntPtr modern=createContext(bufferDc,IntPtr.Zero,[0x2091,3,0x2092,3,0x9126,1,0]);
            if(modern==IntPtr.Zero) throw new InvalidOperationException("projectM requires an OpenGL 3.3 graphics driver.");
            wglMakeCurrent(IntPtr.Zero,IntPtr.Zero); wglDeleteContext(context); context=modern;
            MakeCurrent(); Width=width; Height=height;
            bindFramebuffer=Function<BindFramebuffer>("glBindFramebuffer");
            Version=Marshal.PtrToStringAnsi(glGetString(0x1F02))??"Unknown";
        }
        catch { Dispose(); throw; }
    }
    static T Function<T>(string name) where T:Delegate
    {
        var pointer=wglGetProcAddress(name);
        if(pointer==IntPtr.Zero||pointer.ToInt64() is -1 or 1 or 2 or 3) throw new InvalidOperationException("projectM requires OpenGL 3.3 with WGL pbuffer support.");
        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }
    internal void MakeCurrent()
    {
        if(!TryMakeCurrent()) throw new InvalidOperationException("The OpenGL context is unavailable.");
    }
    internal bool TryMakeCurrent()=>wglMakeCurrent(bufferDc,context);
    internal void Resize(int width,int height)
    {
        if(width==Width&&height==Height) return;
        IntPtr next=createBuffer(windowDc,pixelFormat,width,height,[0]);
        if(next==IntPtr.Zero) throw new InvalidOperationException("Could not resize the OpenGL surface.");
        wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);
        releaseBufferDc(pbuffer,bufferDc); destroyBuffer(pbuffer);
        pbuffer=next; bufferDc=getBufferDc(pbuffer); Width=width; Height=height;
        MakeCurrent();
    }
    internal void ReadPixels(byte[] pixels)
    {
        bindFramebuffer(0x8CA8,0); // Read the final display framebuffer, including composite shaders.
        glReadBuffer(0x0404); // GL_FRONT: the pbuffer is single buffered.
        glReadPixels(0,0,Width,Height,0x80E1,0x1401,pixels);
    }
    public void Dispose()
    {
        if(context!=IntPtr.Zero) { wglMakeCurrent(IntPtr.Zero,IntPtr.Zero); wglDeleteContext(context); context=IntPtr.Zero; }
        if(pbuffer!=IntPtr.Zero) { releaseBufferDc(pbuffer,bufferDc); destroyBuffer(pbuffer); pbuffer=IntPtr.Zero; bufferDc=IntPtr.Zero; }
        if(windowDc!=IntPtr.Zero) { ReleaseDC(window,windowDc); windowDc=IntPtr.Zero; }
        if(window!=IntPtr.Zero) { DestroyWindow(window); window=IntPtr.Zero; }
    }
    [StructLayout(LayoutKind.Sequential)] struct PixelFormat
    {
        public ushort Size,Version; public uint Flags;
        public byte PixelType,ColourBits,RedBits,RedShift,GreenBits,GreenShift,BlueBits,BlueShift,AlphaBits,AlphaShift,AccumBits,AccumRedBits,AccumGreenBits,AccumBlueBits,AccumAlphaBits,DepthBits,StencilBits,AuxBuffers,LayerType,Reserved;
        public uint LayerMask,VisibleMask,DamageMask;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate bool ChooseFormat(IntPtr dc,int[] integers,float[]? floats,uint maximum,out int format,out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr CreateContext(IntPtr dc,IntPtr share,int[] attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr CreatePbuffer(IntPtr dc,int format,int width,int height,int[] attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr GetPbufferDc(IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int ReleasePbufferDc(IntPtr buffer,IntPtr dc);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate bool DestroyPbuffer(IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BindFramebuffer(uint target,uint framebuffer);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint extended,string className,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")] static extern int ChoosePixelFormat(IntPtr dc,ref PixelFormat descriptor);
    [DllImport("gdi32.dll")] static extern bool SetPixelFormat(IntPtr dc,int format,ref PixelFormat descriptor);
    [DllImport("opengl32.dll")] static extern IntPtr wglCreateContext(IntPtr dc);
    [DllImport("opengl32.dll")] static extern bool wglDeleteContext(IntPtr context);
    [DllImport("opengl32.dll")] static extern bool wglMakeCurrent(IntPtr dc,IntPtr context);
    [DllImport("opengl32.dll",CharSet=CharSet.Ansi)] static extern IntPtr wglGetProcAddress(string name);
    [DllImport("opengl32.dll")] static extern IntPtr glGetString(uint name);
    [DllImport("opengl32.dll")] static extern void glReadBuffer(uint mode);
    [DllImport("opengl32.dll")] static extern void glReadPixels(int x,int y,int width,int height,uint format,uint type,[Out] byte[] pixels);
}
