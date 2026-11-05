using Windows.Media.Control;

namespace Pulsive;

internal sealed record NowPlayingInfo(string Title,string Artist)
{
    internal static readonly NowPlayingInfo Unknown=new("Unknown","");
    internal string Format(int format)=>string.IsNullOrWhiteSpace(Title)?"Unknown":string.IsNullOrWhiteSpace(Artist)||format==1?Title:format==2?$"{Artist} — {Title}":$"{Title} · {Artist}";
}

// Only enabled while the text is visible. Windows supplies metadata from apps
// using system media controls; no screen capture, web requests or audio matching.
internal sealed class NowPlayingService
{
    GlobalSystemMediaTransportControlsSessionManager? manager;
    internal string? LastError { get; private set; }
    internal async Task<NowPlayingInfo> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            LastError=null;
            manager??=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken);
            var current=manager.GetCurrentSession();
            var sessions=manager.GetSessions();
            var session=current?.GetPlaybackInfo()?.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                ?current:sessions.FirstOrDefault(s=>s.GetPlaybackInfo()?.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            if(session==null) return NowPlayingInfo.Unknown;
            var media=await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
            return string.IsNullOrWhiteSpace(media?.Title)?NowPlayingInfo.Unknown:new(media.Title.Trim(),media.Artist?.Trim()??"");
        }
        catch(Exception error) { LastError=error.Message; manager=null; return NowPlayingInfo.Unknown; }
    }
}
