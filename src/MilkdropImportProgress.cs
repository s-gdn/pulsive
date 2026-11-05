namespace Pulsive;

internal sealed record MilkdropImportProgress(string Stage,int Completed,int Total,string? File=null);

// Limit dispatcher traffic while importing thousands of small files. Report
// stage boundaries and completion immediately, then at most every 60 ms.
internal sealed class MilkdropImportReporter(IProgress<MilkdropImportProgress>? progress)
{
    long lastReport;
    string? stage;
    internal void Report(string nextStage,int completed,int total,string? file=null)
    {
        if(progress==null) return;
        long now=Environment.TickCount64;
        if(stage==nextStage&&completed!=0&&completed!=total&&now-lastReport<60) return;
        stage=nextStage; lastReport=now;
        progress.Report(new(nextStage,completed,total,file));
    }
}
