using System.ComponentModel;

namespace Pulsive;

internal sealed class MilkdropCollection : INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsExpanded { get; internal set; }=true;
    public string? TextureDirectory { get; internal set; }
    internal string? TextureRevision { get; set; }
    internal HashSet<string> TextureNames { get; set; }=new(StringComparer.OrdinalIgnoreCase);
    public bool HasTextures=>TextureNames.Count>0;
    public bool RequiresTextures { get; internal set; }
    public bool CheckIncomplete { get; internal set; }
    public string[] MissingTextures { get; internal set; }=[];
    public string TextureLabel=>"Textures"+(HasTextures?$" · {TextureNames.Count} available":" · no folder imported");
    public string TextureDetails=>MissingTextures.Length>0?"Missing: "+string.Join(", ",MissingTextures):RequiresTextures?"Detected texture references are available.":CheckIncomplete?"Some shader references could not be checked.":"No external textures detected.";
    public string TextureBadge=>RequiresTextures?"REQUIRES TEXTURES":CheckIncomplete?"CHECK INCOMPLETE":"";
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Refresh()=>PropertyChanged?.Invoke(this,new(null));
}
