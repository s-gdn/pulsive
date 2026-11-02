using System.IO;
using System.Runtime.InteropServices;

namespace Pulsive;

internal static class ProjectMNative
{
    const string Library="projectM-4";
    static ProjectMNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(ProjectMNative).Assembly,(name,_,_)=>
        {
            if(name!=Library&&name!="glew32") return IntPtr.Zero;
            string path=Path.Combine(AppContext.BaseDirectory,"projectm",name+".dll");
            var module=LoadLibraryEx(path,IntPtr.Zero,0x00001100);
            if(module==IntPtr.Zero) throw new DllNotFoundException("The projectM runtime or its Visual C++ x64 dependency could not be loaded.");
            return module;
        });
    }
    internal static void InitialiseOpenGl()
    {
        string path=Path.Combine(AppContext.BaseDirectory,"projectm","glew32.dll");
        IntPtr module=LoadLibraryEx(path,IntPtr.Zero,0x00001100);
        if(module==IntPtr.Zero) throw new DllNotFoundException("The projectM OpenGL loader is missing.");
        try
        {
            Marshal.WriteByte(NativeLibrary.GetExport(module,"glewExperimental"),1);
            if(glewInit()!=0) throw new InvalidOperationException("The OpenGL extension loader could not start.");
            while(glGetError()!=0) { }
        }
        finally { NativeLibrary.Free(module); }
    }
    [DllImport("glew32",CallingConvention=CallingConvention.Cdecl)] static extern uint glewInit();
    [DllImport("opengl32.dll")] static extern uint glGetError();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void LoadFailed([MarshalAs(UnmanagedType.LPUTF8Str)] string filename,[MarshalAs(UnmanagedType.LPUTF8Str)] string message,IntPtr context);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr projectm_create();
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void projectm_destroy(IntPtr instance);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void projectm_get_version_components(out int major,out int minor,out int patch);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void projectm_load_preset_file(IntPtr instance,[MarshalAs(UnmanagedType.LPUTF8Str)] string path,[MarshalAs(UnmanagedType.I1)] bool smooth);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void projectm_load_preset_data(IntPtr instance,[MarshalAs(UnmanagedType.LPUTF8Str)] string data,[MarshalAs(UnmanagedType.I1)] bool smooth);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void projectm_pcm_add_float(IntPtr instance,float[] samples,uint count,int channels);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern uint projectm_pcm_get_max_samples();
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void projectm_opengl_render_frame(IntPtr instance);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void projectm_set_window_size(IntPtr instance,nuint width,nuint height);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void projectm_set_mesh_size(IntPtr instance,nuint width,nuint height);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void projectm_set_fps(IntPtr instance,int fps);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void projectm_set_preset_locked(IntPtr instance,[MarshalAs(UnmanagedType.I1)] bool locked);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void projectm_set_soft_cut_duration(IntPtr instance,double seconds);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void projectm_set_texture_search_paths(IntPtr instance,IntPtr[] paths,nuint count);
    internal static void SetTextureDirectory(IntPtr instance,string? path)
    {
        IntPtr text=path==null?IntPtr.Zero:Marshal.StringToCoTaskMemUTF8(path);
        try { projectm_set_texture_search_paths(instance,path==null?[]:[text],path==null?0u:1u); }
        finally { if(text!=IntPtr.Zero) Marshal.FreeCoTaskMem(text); }
    }
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] internal static extern void projectm_set_preset_switch_failed_event_callback(IntPtr instance,LoadFailed callback,IntPtr context);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
}
