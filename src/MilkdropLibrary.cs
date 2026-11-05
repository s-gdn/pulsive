using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pulsive;

internal sealed class MilkdropEntry : INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public required MilkdropCollection Collection { get; set; }
    internal MilkdropTextureAnalysis Analysis { get; init; }=new([],[],false);
    public bool Bundled { get; init; }
    public string Origin=>Bundled?"BUNDLED":"IMPORTED";
    public string TextureStatus
    {
        get
        {
            var missing=Analysis.MissingFrom(Collection.TextureNames);
            return missing.Length>0?"Missing textures: "+string.Join(", ",missing):Analysis.RequiresTextures?"External texture filenames found":Analysis.Incomplete?"Texture check incomplete":"";
        }
    }
    bool enabled=true;
    public bool IsEnabled { get=>enabled; set { if(enabled==value) return; enabled=value; PropertyChanged?.Invoke(this,new(nameof(IsEnabled))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Refresh()=>PropertyChanged?.Invoke(this,new(null));
}

internal sealed class MilkdropLibrary
{
    sealed class Record
    {
        public string Id { get; set; }="";
        public string Name { get; set; }="";
        public string CollectionId { get; set; }="imported";
        public string? ContentHash { get; set; }
    }
    sealed class CollectionRecord
    {
        public string Id { get; set; }="";
        public string Name { get; set; }="";
        public string? TextureRevision { get; set; }
        public bool IsExpanded { get; set; }=true;
    }
    sealed class Document
    {
        public int Version { get; set; }=2;
        public List<Record> Presets { get; set; }=[];
        public List<CollectionRecord> Collections { get; set; }=[];
    }
    internal sealed record ImportResult(List<MilkdropEntry> Added,int Duplicates,List<string> Errors);
    internal sealed record PackImportResult(MilkdropCollection Collection,ImportResult Presets,int Textures,List<string> Errors);
    internal static readonly HashSet<string> TextureExtensions=new(StringComparer.OrdinalIgnoreCase) { ".jpg",".jpeg",".dds",".png",".tga",".bmp",".dib" };
    readonly string directory,indexPath;
    readonly List<Record> records=[];
    readonly List<MilkdropEntry> entries=[];
    readonly List<MilkdropCollection> collections=[];
    internal IReadOnlyList<MilkdropEntry> Entries=>entries;
    internal IReadOnlyList<MilkdropCollection> Collections=>collections;
    internal string? LoadError { get; }
    static bool Hash(string? id)=>id is { Length:64 }&&id.All(Uri.IsHexDigit);
    static bool CollectionId(string? id)=>id is "bundled" or "imported"||Guid.TryParseExact(id,"N",out _);
    static readonly EnumerationOptions recursive=new() { RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false };

    internal MilkdropLibrary(string directory,IEnumerable<string>? disabled=null)
    {
        this.directory=directory; indexPath=Path.Combine(directory,"library.json");
        collections.Add(new() { Id="bundled",Name="Bundled" });
        collections.Add(new() { Id="imported",Name="Imported" });
        var hidden=new HashSet<string>(disabled??[],StringComparer.Ordinal);
        try
        {
            if(File.Exists(indexPath))
            {
                using var json=JsonDocument.Parse(File.ReadAllText(indexPath));
                var saved=json.RootElement.ValueKind==JsonValueKind.Array
                    ?new Document { Presets=json.RootElement.Deserialize<List<Record>>()??throw new JsonException() }
                    :json.RootElement.Deserialize<Document>()??throw new JsonException();
                if(saved.Version!=2||saved.Presets==null||saved.Collections==null) throw new JsonException();
                if(saved.Collections.Any(c=>c==null||!CollectionId(c.Id)||string.IsNullOrWhiteSpace(c.Name)||(c.TextureRevision!=null&&!Guid.TryParseExact(c.TextureRevision,"N",out _)))||saved.Collections.Select(c=>c.Id).Distinct().Count()!=saved.Collections.Count) throw new JsonException();
                var staged=new List<MilkdropCollection>(collections);
                foreach(var c in saved.Collections)
                {
                    staged.RemoveAll(existing=>existing.Id==c.Id);
                    staged.Add(new() { Id=c.Id,Name=c.Name,TextureRevision=c.TextureRevision,IsExpanded=c.IsExpanded });
                }
                if(saved.Presets.Any(r=>r==null||!Hash(r.Id)||string.IsNullOrWhiteSpace(r.Name)||staged.All(c=>c.Id!=r.CollectionId)||(r.ContentHash!=null&&!Hash(r.ContentHash)))||saved.Presets.Select(r=>r.Id).Distinct().Count()!=saved.Presets.Count) throw new JsonException();
                collections.Clear(); collections.AddRange(staged); records.AddRange(saved.Presets);
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException)
        { LoadError="The MilkDrop library couldn't be read. Its saved index has been kept; changes are disabled until it is repaired."; }
        foreach(var collection in collections) LoadTextureNames(collection);
        foreach(string name in ProjectMRenderer.Presets)
        {
            string file=Path.Combine(AppContext.BaseDirectory,"projectm","presets",name+".milk");
            entries.Add(new MilkdropEntry { Id="bundled:"+name,Name=name,FilePath=file,Bundled=true,Collection=collections.Single(c=>c.Id=="bundled"),Analysis=AnalyseFile(file),IsEnabled=!hidden.Contains("bundled:"+name) });
        }
        foreach(var record in records)
            if(File.Exists(FilePath(record.Id))) entries.Add(Entry(record,hidden));
        RefreshCollections();
    }
    static MilkdropTextureAnalysis AnalyseFile(string file)
    {
        try { return MilkdropTextureAnalysis.Analyse(File.ReadAllText(file)); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { return new([],[],true); }
    }
    string FilePath(string id)=>Path.Combine(directory,"presets",id+".milk");
    string TexturePath(string collection,string revision)=>Path.Combine(directory,"textures",collection,revision);
    MilkdropEntry Entry(Record record,ISet<string>? hidden=null,MilkdropTextureAnalysis? analysis=null)=>new() { Id=record.Id,Name=record.Name,FilePath=FilePath(record.Id),Collection=collections.Single(c=>c.Id==record.CollectionId),Analysis=analysis??AnalyseFile(FilePath(record.Id)),IsEnabled=hidden?.Contains(record.Id)!=true };
    void EnsureWritable() { if(LoadError!=null) throw new IOException(LoadError); }
    void Write(List<Record> updated,IEnumerable<MilkdropCollection>? groups=null)
    {
        EnsureWritable();
        var document=new Document { Presets=updated,Collections=(groups??collections).Select(c=>new CollectionRecord { Id=c.Id,Name=c.Name,TextureRevision=c.TextureRevision,IsExpanded=c.IsExpanded }).ToList() };
        Directory.CreateDirectory(directory);
        File.WriteAllText(indexPath+".tmp",JsonSerializer.Serialize(document,new JsonSerializerOptions { WriteIndented=true }));
        File.Move(indexPath+".tmp",indexPath,true);
    }
    internal MilkdropCollection CreateCollection(string name)
    {
        EnsureWritable(); name=name.Trim();
        if(name.Length is 0 or >40||name.Any(char.IsControl)) throw new ArgumentException("Enter a collection name between 1 and 40 characters.");
        if(collections.Any(c=>c.Name.Equals(name,StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("That collection name is already in use.");
        var collection=new MilkdropCollection { Id=Guid.NewGuid().ToString("N"),Name=name };
        Write(records,collections.Append(collection)); collections.Add(collection); return collection;
    }
    internal void Move(MilkdropEntry entry,MilkdropCollection target)
    {
        EnsureWritable();
        if(!entries.Contains(entry)||!collections.Contains(target)) throw new ArgumentException("Select a preset and collection from this library.");
        if(entry.Bundled) throw new ArgumentException("Bundled presets stay in the Bundled collection.");
        var record=records.Single(r=>r.Id==entry.Id);
        if(record.CollectionId==target.Id) return;
        if(records.Any(r=>r.CollectionId==target.Id&&(r.ContentHash??r.Id)==(record.ContentHash??record.Id))) throw new ArgumentException("This preset is already in that collection.");
        var updated=records.Select(r=>r.Id==entry.Id?new Record { Id=r.Id,Name=r.Name,CollectionId=target.Id,ContentHash=r.ContentHash??r.Id }:r).ToList();
        Write(updated); records.Clear(); records.AddRange(updated); entry.Collection=target; RefreshCollections();
    }
    internal void SetExpanded(MilkdropCollection collection,bool expanded)
    {
        EnsureWritable();
        if(!collections.Contains(collection)) throw new ArgumentException("Select a collection from this library.");
        if(collection.IsExpanded==expanded) return;
        var replacement=new MilkdropCollection { Id=collection.Id,Name=collection.Name,TextureRevision=collection.TextureRevision,IsExpanded=expanded };
        Write(records,collections.Select(c=>c==collection?replacement:c));
        collection.IsExpanded=expanded; collection.Refresh();
    }
    internal static bool ContainsMilkFiles(string folder)=>Directory.EnumerateFiles(folder,"*",recursive).Any(file=>Path.GetExtension(file).Equals(".milk",StringComparison.OrdinalIgnoreCase));
    internal PackImportResult ImportFolder(string folder,IProgress<MilkdropImportProgress>? progress=null)
    {
        EnsureWritable();
        var reporter=new MilkdropImportReporter(progress);
        reporter.Report("Scanning folder",0,0,folder);
        var found=new List<string>();
        foreach(string file in Directory.EnumerateFiles(folder,"*",recursive)) { found.Add(file); reporter.Report("Scanning folder",found.Count,0,file); }
        var files=found.ToArray();
        var presets=files.Where(file=>Path.GetExtension(file).Equals(".milk",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(presets.Length==0) throw new ArgumentException("This folder contains no .milk presets. Drop a texture-only folder onto a collection's Textures entry.");
        if(!presets.Any(file=>new FileInfo(file).Length<=8*1024*1024&&File.ReadAllText(file).Contains("[preset00]",StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("This folder contains no valid MilkDrop presets.");
        string name=new string(new DirectoryInfo(Path.GetFullPath(folder)).Name.Where(c=>!char.IsControl(c)).Take(40).ToArray()).Trim();
        if(string.IsNullOrWhiteSpace(name)) name="Collection";
        string unique=name; int suffix=2;
        while(collections.Any(c=>c.Name.Equals(unique,StringComparison.OrdinalIgnoreCase)))
        {
            string ending=$" ({suffix++})"; unique=name[..Math.Min(name.Length,40-ending.Length)]+ending;
        }
        var collection=CreateCollection(unique);
        var imported=Import(presets,collection.Id,progress); var errors=new List<string>(imported.Errors);
        var textures=files.Where(file=>TextureExtensions.Contains(Path.GetExtension(file))&&InTextureFolder(folder,file)).ToArray();
        int count=0;
        if(textures.Length>0)
        {
            try { count=ImportTextureFiles(collection,folder,textures,progress); }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) { errors.Add("Textures: "+error.Message); }
        }
        return new(collection,imported,count,errors);
    }
    static bool InTextureFolder(string root,string file)
    {
        string rootName=new DirectoryInfo(root).Name;
        return rootName.Equals("texture",StringComparison.OrdinalIgnoreCase)||rootName.Equals("textures",StringComparison.OrdinalIgnoreCase)
            ||(Path.GetDirectoryName(Path.GetRelativePath(root,file))??"").Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Any(part=>part.Equals("texture",StringComparison.OrdinalIgnoreCase)||part.Equals("textures",StringComparison.OrdinalIgnoreCase));
    }
    internal ImportResult Import(IEnumerable<string> files,string collectionId="imported",IProgress<MilkdropImportProgress>? progress=null)
    {
        var added=new List<MilkdropEntry>(); var errors=new List<string>(); int duplicates=0;
        if(LoadError!=null) return new(added,0,[LoadError]);
        if(collections.All(c=>c.Id!=collectionId)) return new(added,0,["Select a collection before importing."]);
        var updated=new List<Record>(records);
        var lookup=updated.GroupBy(r=>(r.CollectionId,r.ContentHash??r.Id)).ToDictionary(g=>g.Key,g=>g.First());
        var usedIds=updated.Select(r=>r.Id).ToHashSet(StringComparer.Ordinal);
        var names=updated.Select(r=>r.Name).Concat(entries.Select(e=>e.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var paths=files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var reporter=new MilkdropImportReporter(progress); int completed=0;
        reporter.Report("Importing presets",0,paths.Length);
        foreach(string file in paths)
        {
            try
            {
                if(!Path.GetExtension(file).Equals(".milk",StringComparison.OrdinalIgnoreCase)) { errors.Add(Path.GetFileName(file)+": not a .milk file."); continue; }
                if(new FileInfo(file).Length>8*1024*1024) { errors.Add(Path.GetFileName(file)+": preset exceeds 8 MB."); continue; }
                byte[] data=File.ReadAllBytes(file);
                string text=Encoding.UTF8.GetString(data);
                if(text.Length>0&&text[0]=='\uFEFF') text=text[1..];
                if(!text.Contains("[preset00]",StringComparison.OrdinalIgnoreCase)) { errors.Add(Path.GetFileName(file)+": not a MilkDrop preset."); continue; }
                string hash=Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                lookup.TryGetValue((collectionId,hash),out var record);
                if(record!=null&&File.Exists(FilePath(record.Id))) { duplicates++; continue; }
                string id=record?.Id??(collectionId=="imported"?hash:Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(collectionId+":"+hash))).ToLowerInvariant());
                if(record==null&&usedIds.Contains(id)) id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(collectionId+":"+hash))).ToLowerInvariant();
                Directory.CreateDirectory(Path.Combine(directory,"presets")); File.WriteAllBytes(FilePath(id),data);
                if(record==null)
                {
                    string name=new(Path.GetFileNameWithoutExtension(file).Where(c=>!char.IsControl(c)).ToArray());
                    if(string.IsNullOrWhiteSpace(name)) name="MilkDrop preset";
                    string unique=name; int suffix=2;
                    while(!names.Add(unique)) unique=$"{name} ({suffix++})";
                    record=new Record { Id=id,Name=unique,CollectionId=collectionId,ContentHash=hash }; updated.Add(record);
                    lookup[(collectionId,hash)]=record; usedIds.Add(id);
                }
                added.Add(Entry(record,analysis:MilkdropTextureAnalysis.Analyse(text)));
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { errors.Add(Path.GetFileName(file)+": couldn't import this file."); }
            finally { reporter.Report("Importing presets",++completed,paths.Length,file); }
        }
        if(added.Count>0)
        {
            try { reporter.Report("Saving library",0,1); Write(updated); records.Clear(); records.AddRange(updated); entries.AddRange(added); RefreshCollections(); reporter.Report("Saving library",1,1); }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException) { errors.Add("Couldn't save the MilkDrop library. Check folder access and try importing again."); added.Clear(); }
        }
        return new(added,duplicates,errors);
    }
    internal int ImportTextures(MilkdropCollection collection,string source,IProgress<MilkdropImportProgress>? progress=null)
        =>ImportTextureFiles(collection,source,Directory.EnumerateFiles(source,"*",recursive).Where(file=>TextureExtensions.Contains(Path.GetExtension(file))).ToArray(),progress);
    int ImportTextureFiles(MilkdropCollection collection,string source,string[] files,IProgress<MilkdropImportProgress>? progress=null)
    {
        EnsureWritable();
        if(!collections.Contains(collection)) throw new ArgumentException("Select a collection from this library.");
        string revision=Guid.NewGuid().ToString("N"),destination=TexturePath(collection.Id,revision);
        if(files.Length==0) throw new ArgumentException("This folder contains no supported textures (PNG, JPG, DDS, TGA, BMP or DIB).");
        var reporter=new MilkdropImportReporter(progress); int completed=0;
        reporter.Report("Importing textures",0,files.Length);
        foreach(string file in files)
        {
            string relative=Path.GetRelativePath(source,file),target=Path.GetFullPath(Path.Combine(destination,relative));
            if(!target.StartsWith(Path.GetFullPath(destination)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Texture path is outside the collection.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file,target,true);
            reporter.Report("Importing textures",++completed,files.Length,file);
        }
        var replacement=new MilkdropCollection { Id=collection.Id,Name=collection.Name,TextureRevision=revision,IsExpanded=collection.IsExpanded };
        Write(records,collections.Select(c=>c==collection?replacement:c));
        collection.TextureRevision=revision; LoadTextureNames(collection); RefreshCollections();
        return files.Length;
    }
    void LoadTextureNames(MilkdropCollection collection)
    {
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        collection.TextureNames=names; collection.TextureDirectory=null;
        if(collection.TextureRevision==null) return;
        string path=TexturePath(collection.Id,collection.TextureRevision);
        try
        {
            if(!Directory.Exists(path)) return;
            var found=Directory.EnumerateFiles(path,"*",recursive).Where(file=>TextureExtensions.Contains(Path.GetExtension(file))).Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            collection.TextureNames=found;
            if(collection.HasTextures) collection.TextureDirectory=path;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { collection.TextureNames=names; }
    }
    void RefreshCollections()
    {
        var grouped=entries.ToLookup(entry=>entry.Collection);
        foreach(var collection in collections)
        {
            var presets=grouped[collection].ToArray();
            collection.RequiresTextures=presets.Any(entry=>entry.Analysis.RequiresTextures);
            collection.CheckIncomplete=presets.Any(entry=>entry.Analysis.Incomplete);
            collection.MissingTextures=presets.SelectMany(entry=>entry.Analysis.MissingFrom(collection.TextureNames)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
            collection.Refresh(); foreach(var preset in presets) preset.Refresh();
        }
    }
}
