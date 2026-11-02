using System.Text.RegularExpressions;

namespace Pulsive;

internal sealed record MilkdropTextureAnalysis(string[] ExternalNames,string[] RandomRequests,bool Incomplete)
{
    internal bool RequiresTextures=>ExternalNames.Length>0||RandomRequests.Length>0;
    static readonly HashSet<string> builtIns=new(StringComparer.OrdinalIgnoreCase)
    { "main","blur1","blur2","blur3","noise_lq_lite","noise_lq","noise_mq","noise_hq","noisevol_lq","noisevol_hq","idlem","idleheadphones" };
    static readonly Regex shaderLines=new(@"^\s*(?:warp|comp)_\d+\s*=\s*`?(.*)$",RegexOptions.Multiline|RegexOptions.IgnoreCase|RegexOptions.Compiled);
    static readonly Regex comments=new(@"/\*[\s\S]*?\*/|//[^\r\n]*",RegexOptions.Compiled);
    static readonly Regex samplers=new(@"\b(?:sampler|texsize)_([A-Za-z_][A-Za-z_0-9]*)",RegexOptions.Compiled);
    static readonly Regex images=new(@"^\s*shapecode_(\d+)_image\s*=\s*([^\r\n]*)",RegexOptions.Multiline|RegexOptions.IgnoreCase|RegexOptions.Compiled);
    internal static MilkdropTextureAnalysis Analyse(string text)
    {
        bool incomplete=false;
        int Value(string key,int fallback)
        {
            var match=Regex.Match(text,@"^\s*"+key+@"\s*=\s*(-?\d+)",RegexOptions.Multiline|RegexOptions.IgnoreCase);
            return match.Success&&int.TryParse(match.Groups[1].Value,out int value)?value:fallback;
        }
        int version=Value("MILKDROP_PRESET_VERSION",100);
        var external=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var random=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string name)
        {
            name=name.Trim().Trim('"','\'');
            // projectM strips a two-letter wrap/filter qualifier before lookup.
            if(name.Length>3&&name[2]=='_') name=name[3..];
            if(name.Length==0||name.Equals("state",StringComparison.OrdinalIgnoreCase)||builtIns.Contains(name)) return;
            if(Regex.IsMatch(name,@"^rand(?:0[0-9]|1[0-5])(?:_|$)",RegexOptions.IgnoreCase)) random.Add(name.ToLowerInvariant());
            else external.Add(name.ToLowerInvariant());
        }
        foreach(string stage in new[] { "warp","comp" })
        {
            int shaderVersion=Value(version==200?"PSVERSION":"PSVERSION_"+stage.ToUpperInvariant(),2);
            if(version<200||shaderVersion==0) continue;
            string shader=comments.Replace(string.Join("\n",shaderLines.Matches(text).Where(match=>match.Value.TrimStart().StartsWith(stage+"_",StringComparison.OrdinalIgnoreCase)).Select(match=>match.Groups[1].Value)),"");
            // Unknown preprocessor branches cannot prove a dependency. Keep those
            // as an incomplete check, rather than marking a collection as requiring it.
            int conditional=0; var unconditional=new List<string>();
            foreach(string line in shader.Split('\n'))
            {
                if(Regex.IsMatch(line,@"^\s*#\s*(?:if|ifdef|ifndef)\b")) { conditional++; incomplete=true; continue; }
                if(Regex.IsMatch(line,@"^\s*#\s*endif\b")) { conditional=Math.Max(0,conditional-1); continue; }
                if(conditional==0) unconditional.Add(line);
            }
            shader=string.Join("\n",unconditional);
            var aliases=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(Match alias in Regex.Matches(shader,@"^\s*#\s*define\s+(\w+)\s+(\w+)\s*$",RegexOptions.Multiline)) aliases[alias.Groups[1].Value]=alias.Groups[2].Value;
            incomplete|=shader.Contains("##",StringComparison.Ordinal)||Regex.IsMatch(shader,@"^\s*#\s*include\b",RegexOptions.Multiline);
            string body=Regex.Replace(shader,@"^\s*#.*$","",RegexOptions.Multiline);
            // A declaration alone does not mean the image affects the preset.
            body=Regex.Replace(body,@"\bsampler(?:1D|2D|3D|CUBE)?\s+sampler_\w+\s*;","",RegexOptions.IgnoreCase);
            foreach(Match match in samplers.Matches(body))
            {
                string token=match.Value; var visited=new HashSet<string>(StringComparer.Ordinal);
                while(aliases.TryGetValue(token,out string? replacement)&&visited.Add(token)) token=replacement;
                if(aliases.ContainsKey(token)) { incomplete=true; continue; }
                if(token.StartsWith("sampler_",StringComparison.Ordinal)||token.StartsWith("texsize_",StringComparison.Ordinal)) Add(token[8..]);
                else incomplete=true;
            }
            incomplete|=Regex.IsMatch(body,@"\btex(?:2D|3D)\w*\s*\(\s*(?!sampler_)[A-Za-z_]\w*\s*[,)]",RegexOptions.IgnoreCase);
        }
        foreach(Match match in images.Matches(text))
        {
            string i=match.Groups[1].Value;
            // Disabled shapes and ordinary feedback-textured shapes need no external image.
            bool enabled=Regex.IsMatch(text,@"^\s*shapecode_"+i+@"_enabled\s*=\s*(?:1|true)\b",RegexOptions.Multiline|RegexOptions.IgnoreCase);
            bool textured=Regex.IsMatch(text,@"^\s*shapecode_"+i+@"_textured\s*=\s*(?:1|true)\b",RegexOptions.Multiline|RegexOptions.IgnoreCase)
                ||Regex.IsMatch(text,@"^\s*shape_"+i+@"_per_frame\d*\s*=.*\btextured\s*=",RegexOptions.Multiline|RegexOptions.IgnoreCase);
            if(enabled&&textured) Add(comments.Replace(match.Groups[2].Value,""));
        }
        return new(external.Order().ToArray(),random.Order().ToArray(),incomplete);
    }
    internal string[] MissingFrom(IReadOnlySet<string> available)
    {
        var missing=ExternalNames.Where(name=>!available.Contains(name)).ToList();
        foreach(string request in RandomRequests)
        {
            string prefix=request.Length>7&&request[6]=='_'?request[7..]:"";
            if(!available.Any(name=>name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))) missing.Add(prefix.Length==0?"any external texture":"texture starting with "+prefix);
        }
        return missing.ToArray();
    }
}
