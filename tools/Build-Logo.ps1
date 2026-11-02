$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$assetPath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../assets'))
[xml]$svg=Get-Content -LiteralPath (Join-Path $assetPath 'pulsive-logo.svg') -Raw
$gradient=[Windows.Media.LinearGradientBrush]::new()
$gradient.MappingMode=[Windows.Media.BrushMappingMode]::Absolute
$gradient.StartPoint=[Windows.Point]::new([double]$svg.svg.defs.linearGradient.x1,[double]$svg.svg.defs.linearGradient.y1)
$gradient.EndPoint=[Windows.Point]::new([double]$svg.svg.defs.linearGradient.x2,[double]$svg.svg.defs.linearGradient.y2)
foreach($stop in $svg.svg.defs.linearGradient.stop) {
    $color=[Windows.Media.ColorConverter]::ConvertFromString($stop.'stop-color')
    $gradient.GradientStops.Add([Windows.Media.GradientStop]::new($color,[double]$stop.offset))
}
$gradient.Freeze()
function Render-Logo([int]$size) {
    $visual=[Windows.Media.DrawingVisual]::new()
    $drawing=$visual.RenderOpen()
    $viewBox=$svg.svg.viewBox.Split(' ')
    $drawing.PushTransform([Windows.Media.ScaleTransform]::new($size/[double]$viewBox[2],$size/[double]$viewBox[3]))
    $background=[Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($svg.svg.rect.fill))
    $edge=[Windows.Media.Pen]::new([Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($svg.svg.rect.stroke)),[double]$svg.svg.rect.'stroke-width')
    $rect=$svg.svg.rect
    $drawing.DrawRoundedRectangle($background,$edge,[Windows.Rect]::new([double]$rect.x,[double]$rect.y,[double]$rect.width,[double]$rect.height),[double]$rect.rx,[double]$rect.rx)
    $pen=[Windows.Media.Pen]::new($gradient,[double]$svg.svg.g.'stroke-width')
    $pen.StartLineCap=$pen.EndLineCap=[Windows.Media.PenLineCap]::Round
    $pen.LineJoin=[Windows.Media.PenLineJoin]::Round
    foreach($path in $svg.svg.g.path) { $drawing.DrawGeometry($null,$pen,[Windows.Media.Geometry]::Parse($path.d)) }
    $drawing.Pop(); $drawing.Close()
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream=[IO.MemoryStream]::new()
    $encoder.Save($stream)
    $bytes=$stream.ToArray(); $stream.Dispose()
    return ,$bytes
}
[IO.File]::WriteAllBytes((Join-Path $assetPath 'pulsive-logo.png'),(Render-Logo 512))
$sizes=@(16,24,32,48,64,128,256)
$images=@(); foreach($size in $sizes) { $images+=,(Render-Logo $size) }
$stream=[IO.File]::Create((Join-Path $assetPath 'pulsive.ico'))
$writer=[IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset=6+16*$sizes.Count
    for($i=0;$i -lt $sizes.Count;$i++) {
        $dimension=if($sizes[$i] -eq 256) {0} else {$sizes[$i]}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset+=$images[$i].Length
    }
    foreach($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose(); $stream.Dispose() }
Write-Output "Generated pulsive-logo.png and pulsive.ico (16-256 px) from pulsive-logo.svg."
