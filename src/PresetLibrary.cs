using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Pulsive;

internal sealed class SavedPreset
{
    public string Id { get; set; }=Guid.NewGuid().ToString("N");
    public string Name { get; set; }="";
    public AppearanceSettings Settings { get; set; }=new();
}

internal sealed class PresetLibrary
{
    internal const string DefaultId="default";
    internal static AppearanceSettings Default => new()
    {
        BackgroundType=2,          // Blur
        BackgroundBlur=true,
        BackgroundBlurAmount=48,   // 100%
        BorderType=2,              // Gradient
        Intensity=1                // 100% brightness
    };
    static readonly PropertyInfo[] properties=typeof(AppearanceSettings).GetProperties();
    readonly string path;
    List<SavedPreset> presets=[];
    internal IReadOnlyList<SavedPreset> Presets=>presets;
    internal string? LoadError { get; }
    internal PresetLibrary(string path)
    {
        this.path=path;
        try
        {
            if(File.Exists(path))
            {
                presets=JsonSerializer.Deserialize<List<SavedPreset>>(File.ReadAllText(path))??throw new JsonException();
                if(presets.Any(p=>p==null||string.IsNullOrWhiteSpace(p.Id)||p.Id==DefaultId||string.IsNullOrWhiteSpace(p.Name)||p.Settings==null)||presets.Select(p=>p.Id).Distinct().Count()!=presets.Count) throw new JsonException();
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException)
        {
            presets=[]; LoadError="Saved presets couldn't be loaded. The existing preset file has been kept.";
        }
    }
    internal static AppearanceSettings Snapshot(AppearanceSettings source)
    {
        var copy=new AppearanceSettings(); Copy(source,copy); return copy;
    }
    internal static void Copy(AppearanceSettings source,AppearanceSettings destination)
    {
        foreach(var property in properties) property.SetValue(destination,Clone(property.GetValue(source)));
    }
    static object? Clone(object? value)=>value is List<string> list?new List<string>(list):value;
    internal static void CopyShuffle(ShuffleSettings source,ShuffleSettings destination)
    {
        foreach(var property in typeof(ShuffleSettings).GetProperties()) property.SetValue(destination,Clone(property.GetValue(source)));
    }
    internal static ShuffleSettings ShuffleSnapshot(ShuffleSettings source) { var copy=new ShuffleSettings(); CopyShuffle(source,copy); return copy; }
    static bool Equal(object? a,object? b)=>a is IEnumerable<string> left&&b is IEnumerable<string> right?left.ToHashSet(StringComparer.Ordinal).SetEquals(right):Equals(a,b);
    static string? MilkdropId(AppearanceSettings settings)=>settings.Mode<11?null:settings.MilkdropPresetId??(settings.Mode<14?"bundled:"+ProjectMRenderer.Presets[settings.Mode-11]:null);
    internal static bool Matches(AppearanceSettings a,AppearanceSettings b)
    {
        bool sameMode=a.Mode>=11&&b.Mode>=11&&(MilkdropId(a)!=null||MilkdropId(b)!=null)?MilkdropId(a)==MilkdropId(b):a.Mode==b.Mode;
        return sameMode&&properties.Where(p=>p.Name!=nameof(AppearanceSettings.Mode)&&p.Name!=nameof(AppearanceSettings.MilkdropPresetId)).All(p=>Equal(p.GetValue(a),p.GetValue(b)));
    }
    internal SavedPreset Add(string name,AppearanceSettings settings)
    {
        name=name.Trim();
        if(name.Length==0||name.Length>40||name.Any(char.IsControl)) throw new ArgumentException("Enter a name between 1 and 40 characters.");
        if(name.Equals("Default",StringComparison.OrdinalIgnoreCase)||name.TrimEnd('…','.').TrimEnd().Equals("Save as new preset",StringComparison.OrdinalIgnoreCase)||presets.Any(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("That preset name is already in use. Choose another name.");
        var preset=new SavedPreset { Name=name,Settings=Snapshot(settings) };
        Write([..presets,preset]); return preset;
    }
    internal void Delete(string id)
    {
        if(id==DefaultId) throw new ArgumentException("The Default preset cannot be deleted.");
        if(presets.All(p=>p.Id!=id)) return;
        Write(presets.Where(p=>p.Id!=id).ToList());
    }
    internal void Update(string id,AppearanceSettings settings)
    {
        if(id==DefaultId) throw new ArgumentException("The Default preset cannot be overwritten.");
        var existing=presets.FirstOrDefault(p=>p.Id==id)??throw new ArgumentException("This preset no longer exists.");
        var replacement=new SavedPreset { Id=id,Name=existing.Name,Settings=Snapshot(settings) };
        Write(presets.Select(p=>p.Id==id?replacement:p).ToList());
    }
    void Write(List<SavedPreset> updated)
    {
        if(LoadError!=null) throw new IOException(LoadError);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(updated,new JsonSerializerOptions { WriteIndented=true }));
        File.Move(temporary,path,true); presets=updated;
    }
}
