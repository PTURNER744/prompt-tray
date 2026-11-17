$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework compiler is required to rebuild Prompt Tray.'
}

$sources = 'Program.cs', 'PromptEditor.cs', 'Library.cs', 'Foreground.cs', 'ShortcutWindow.cs', 'ItemTransfer.cs' |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
$output = Join-Path $PSScriptRoot 'PromptTray.exe'
$icon = Join-Path $PSScriptRoot 'PromptTray.ico'
$options = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/warnaserror+', '/codepage:65001',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Core.dll',
    '/reference:System.Runtime.Serialization.dll', '/reference:System.Xml.dll',
    "/win32icon:$icon", "/resource:$icon,PromptTray.Icon", "/out:$output")
& $compiler $options $sources
if ($LASTEXITCODE -ne 0) {
    throw "The build failed with exit code $LASTEXITCODE."
}
Write-Host "Built $output"
