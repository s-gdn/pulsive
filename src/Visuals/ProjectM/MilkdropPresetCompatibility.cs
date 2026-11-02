using System.Globalization;
using System.Text.RegularExpressions;

namespace Pulsive;

internal static class MilkdropPresetCompatibility
{
    static readonly Regex equationLine=new(@"^(?<prefix>per_frame_init_|per_frame_|per_pixel_|wave_\d+_(?:init|per_frame|per_point)|shape_\d+_(?:init|per_frame))(?<number>[1-9]\d*)=(?<code>.*)$",RegexOptions.Compiled);
    static readonly Regex splitIdentifier=new(@"\b[A-Za-z_][A-Za-z_0-9]*_$",RegexOptions.Compiled);
    static readonly Regex simpleAssignment=new(@"^\s*[A-Za-z_][A-Za-z_0-9]*\s*=\s*(?:[A-Za-z_][A-Za-z_0-9]*|[+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*$",RegexOptions.Compiled);
    static readonly Regex startsAssignment=new(@"^\s*[A-Za-z_][A-Za-z_0-9]*\s*=(?!=)",RegexOptions.Compiled);
    sealed class EquationState
    {
        internal int Number,Depth;
        internal bool BlockComment,Uncertain;
        internal void Read(string code)
        {
            for(int i=0;i<code.Length;i++)
            {
                char c=code[i]; char next=i+1<code.Length?code[i+1]:'\0';
                if(BlockComment) { if(c=='*'&&next=='/') { BlockComment=false; i++; } continue; }
                if(c=='/'&&next=='/') break;
                if(c=='/'&&next=='*') { BlockComment=true; i++; continue; }
                if(c is '\'' or '"' or '?' or ':') { Uncertain=true; break; }
                if(c is '(' or '[') Depth++;
                else if(c is ')' or ']') { if(Depth==0) Uncertain=true; else Depth--; }
            }
        }
    }
    internal static string RepairEquations(string source,out int repairs)
    {
        repairs=0;
        if(source.Contains('\0')) return source;
        string newline=source.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
        var lines=source.Replace("\r\n","\n").Split('\n');
        for(int i=0;i<lines.Length-1;i++)
        {
            var left=equationLine.Match(lines[i]); var right=equationLine.Match(lines[i+1]);
            if(!left.Success||!right.Success||left.Groups["prefix"].Value!=right.Groups["prefix"].Value) continue;
            if(!int.TryParse(left.Groups["number"].Value,NumberStyles.None,CultureInfo.InvariantCulture,out int number)||number>=99999||right.Groups["number"].Value!=(number+1).ToString(CultureInfo.InvariantCulture)) continue;
            string before=left.Groups["code"].Value.TrimEnd(),after=right.Groups["code"].Value.TrimStart();
            // Repair only a split underscore-ended token in plain equation code.
            // Do not combine shaders, comments, quoted text or complete statements.
            if(!splitIdentifier.IsMatch(before)||after.Length==0||!((after[0]>='a'&&after[0]<='z')||(after[0]>='A'&&after[0]<='Z')||(after[0]>='0'&&after[0]<='9')||after[0]=='_')) continue;
            if(before.Contains("//",StringComparison.Ordinal)||before.Contains("/*",StringComparison.Ordinal)||before.Contains("*/",StringComparison.Ordinal)||before.Contains('"')||before.Contains('\'')) continue;
            lines[i]=left.Groups["prefix"].Value+left.Groups["number"].Value+"="+before+after;
            // Keep every numbered key so projectM still reads all following lines.
            lines[i+1]=right.Groups["prefix"].Value+right.Groups["number"].Value+"=";
            repairs++;
        }
        var states=new Dictionary<string,EquationState>(StringComparer.Ordinal);
        for(int i=0;i<lines.Length;i++)
        {
            var line=equationLine.Match(lines[i]);
            if(!line.Success||!int.TryParse(line.Groups["number"].Value,out int number)||number>99999) continue;
            string prefix=line.Groups["prefix"].Value,code=line.Groups["code"].Value;
            if(!states.TryGetValue(prefix,out var state)||number==1) states[prefix]=state=new EquationState();
            if(number!=state.Number+1) state.Uncertain=true;
            state.Number=number;
            // Add a separator only between two unambiguous top-level assignments.
            // Function arguments, loops and conditional expressions stay untouched.
            if(!state.Uncertain&&!state.BlockComment&&state.Depth==0&&simpleAssignment.IsMatch(code)&&i+1<lines.Length)
            {
                var next=equationLine.Match(lines[i+1]);
                if(next.Success&&next.Groups["prefix"].Value==prefix&&next.Groups["number"].Value==(number+1).ToString(CultureInfo.InvariantCulture)&&startsAssignment.IsMatch(next.Groups["code"].Value))
                { lines[i]+=";"; repairs++; }
            }
            state.Read(code);
        }
        return repairs==0?source:string.Join(newline,lines);
    }
}
