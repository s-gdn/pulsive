namespace Pulsive;

// A shuffled bag visits every included entry before refilling. Stable IDs keep
// saved selections valid when MilkDrop entries or user presets are reordered.
internal sealed class ShuffleCycle
{
    readonly List<string> remaining=[];
    HashSet<string> previous=[];
    internal void Reset() { remaining.Clear(); previous.Clear(); }
    internal string? Next(IEnumerable<string> candidates,string? current)
    {
        var available=candidates.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if(!previous.SetEquals(available)) { remaining.Clear(); previous=available; }
        if(available.Count==0) return null;
        if(remaining.Count==0)
        {
            remaining.AddRange(available);
            Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(remaining));
        }
        int index=remaining.FindIndex(id=>id!=current);
        if(index<0)
        {
            remaining.Clear();
            if(available.Count==1) return null;
            return Next(available,current);
        }
        string next=remaining[index]; remaining.RemoveAt(index); return next;
    }
}
