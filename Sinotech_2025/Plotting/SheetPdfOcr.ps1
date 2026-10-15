# Windows PowerShell 5.1 WinRT bridge. Reads JSON requests from stdin, writes one JSON reply per line.
# Uses the installed Windows OCR languages and Windows PDF renderer; no network or model edits.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName System.Runtime.WindowsRuntime
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class SheetOcrBitmapFilter {
    public static void Enhance(Stream input, Stream output) {
        using (var image = new Bitmap(input))
        using (var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb)) {
            using (var graphics = Graphics.FromImage(bitmap)) {
                graphics.Clear(Color.White);
                graphics.DrawImageUnscaled(image, 0, 0);
            }
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try {
                int w = bitmap.Width, h = bitmap.Height, stride = data.Stride;
                byte[] pixels = new byte[stride * h];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                bool[] dark = new bool[w * h];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) {
                    int p = y * stride + x * 4;
                    dark[y * w + x] = (pixels[p] + pixels[p+1] + pixels[p+2]) < 600;
                }
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) {
                    bool ink = false;
                    for (int yy = Math.Max(0, y-1); yy <= Math.Min(h-1, y+1) && !ink; yy++)
                        for (int xx = Math.Max(0, x-1); xx <= Math.Min(w-1, x+1); xx++) if (dark[yy*w+xx]) { ink = true; break; }
                    int p = y * stride + x * 4;
                    pixels[p] = pixels[p+1] = pixels[p+2] = (byte)(ink ? 0 : 255); pixels[p+3] = 255;
                }
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            } finally { bitmap.UnlockBits(data); }
            bitmap.Save(output, ImageFormat.Png);
        }
    }
}
'@
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime]
$null = [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType=WindowsRuntime]
$null = [Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType=WindowsRuntime]
$null = [Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType=WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType=WindowsRuntime]
$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType=WindowsRuntime]
$null = [Windows.Globalization.Language, Windows.Globalization, ContentType=WindowsRuntime]
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
} | Select-Object -First 1
$asAction = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and -not $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction'
} | Select-Object -First 1
function Await($Operation, $Type) {
    $task = $asTask.MakeGenericMethod($Type).Invoke($null, @($Operation))
    $task.GetAwaiter().GetResult()
}
function AwaitAction($Operation) {
    $task = $asAction.Invoke($null, @($Operation))
    $null = $task.GetAwaiter().GetResult()
}
$document = $null
$openedPath = ''
while ($null -ne ($line = [Console]::ReadLine())) {
    try {
        $request = $line | ConvertFrom-Json
        if ($openedPath -ne $request.Path) {
            $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($request.Path)) ([Windows.Storage.StorageFile])
            $document = Await ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
            $openedPath = $request.Path
        }
        $language = [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages | Where-Object { $_.LanguageTag -like 'zh-Hant*' } | Select-Object -First 1
        if ($null -eq $language) { throw 'Windows 尚未安裝繁體中文 OCR 語言。請安裝語言 OCR 功能或使用人工確認。' }
        $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($language)
        $english = [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages | Where-Object { $_.LanguageTag -eq 'en-US' } | Select-Object -First 1
        $englishEngine = if ($null -ne $english) { [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($english) } else { $engine }
        if ($null -eq $engine) { throw 'Windows 繁體中文 OCR 無法初始化。' }
        $page = $document.GetPage([uint32]($request.Page - 1))
        try {
            $cells = @()
            foreach ($region in $request.Regions) {
                $options = [Windows.Data.Pdf.PdfPageRenderOptions]::new()
                $crop = $options.SourceRect
                $crop.X = [double]$region.Left * $page.Size.Width
                $crop.Y = [double]$region.Top * $page.Size.Height
                $crop.Width = [double]$region.Width * $page.Size.Width
                $crop.Height = [double]$region.Height * $page.Size.Height
                $options.SourceRect = $crop
                $requestedScale = if ($null -ne $request.Scale) { [double]$request.Scale } else { 6.0 }
                $scale = [Math]::Min($requestedScale, ([Windows.Media.Ocr.OcrEngine]::MaxImageDimension - 10) / [Math]::Max($crop.Width, $crop.Height))
                $options.DestinationWidth = [uint32][Math]::Ceiling($crop.Width * $scale)
                $options.DestinationHeight = [uint32][Math]::Ceiling($crop.Height * $scale)
                $stream = [Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
                try {
                    AwaitAction ($page.RenderToStreamAsync($stream, $options))
                    $stream.Seek(0)
                    $rawDecoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
                    $rawBitmap = Await ($rawDecoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
                    $enhanced = [Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
                    $managedInput = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream.GetInputStreamAt(0))
                    $managedOutput = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForWrite($enhanced.GetOutputStreamAt(0))
                    try { [SheetOcrBitmapFilter]::Enhance($managedInput, $managedOutput); $managedOutput.Flush() }
                    finally { $managedInput.Dispose(); $managedOutput.Dispose() }
                    $enhanced.Seek(0)
                    $decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($enhanced)) ([Windows.Graphics.Imaging.BitmapDecoder])
                    $bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
                    try {
                        $variants = @()
                        $variantIndex = 0
                        foreach ($recognizer in @($engine, $englishEngine, $englishEngine, $engine)) {
                            $inputBitmap = if ($variantIndex -lt 2) { $rawBitmap } else { $bitmap }
                            $ocr = Await ($recognizer.RecognizeAsync($inputBitmap)) ([Windows.Media.Ocr.OcrResult])
                            $lines = @()
                            foreach ($textLine in $ocr.Lines) {
                                $words = @()
                                foreach ($word in $textLine.Words) {
                                    $words += @{ Text = $word.Text; X = $word.BoundingRect.X / $bitmap.PixelWidth;
                                        Y = $word.BoundingRect.Y / $bitmap.PixelHeight;
                                        Width = $word.BoundingRect.Width / $inputBitmap.PixelWidth;
                                        Height = $word.BoundingRect.Height / $inputBitmap.PixelHeight }
                                }
                                $lines += @{ Text = $textLine.Text; Words = $words }
                            }
                            $variants += ,$lines
                            $variantIndex++
                        }
                        $cells += @{ Lines = $variants[0]; EnglishLines = $variants[1]; EnhancedEnglishLines = $variants[2]; EnhancedLines = $variants[3] }
                    } finally { if ($null -ne $bitmap) { $bitmap.Dispose() }; if ($null -ne $rawBitmap) { $rawBitmap.Dispose() }; $enhanced.Dispose() }
                } finally { $stream.Dispose() }
            }
            [Console]::WriteLine((@{ Cells = $cells; Error = '' } | ConvertTo-Json -Compress -Depth 8))
        } finally { $page.Dispose() }
    } catch {
        [Console]::WriteLine((@{ Cells = @(); Error = $_.Exception.Message } | ConvertTo-Json -Compress -Depth 8))
    }
}
