[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameRoot)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $MyInvocation.MyCommand.Path
$pwsh=Split-Path -Parent (Get-Process -Id $PID).Path
Add-Type -LiteralPath (Join-Path $pwsh 'Microsoft.CodeAnalysis.dll')
Add-Type -LiteralPath (Join-Path $pwsh 'Microsoft.CodeAnalysis.CSharp.dll')
$parse=[Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp7_3)
$sourceFiles=@(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter *.cs | Where-Object {$_.Name -notlike 'SanctuaryLobbyUi*.cs' -and $_.Name -ne 'SanctuaryLobbyCanvasUi.cs'} | Sort-Object Name)
[Microsoft.CodeAnalysis.SyntaxTree[]]$trees=$sourceFiles | ForEach-Object {
  [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_.FullName),$parse,$_.FullName,[Text.Encoding]::UTF8)
}
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$paths=@(
 (Join-Path $framework 'mscorlib.dll'),(Join-Path $framework 'System.dll'),(Join-Path $framework 'System.Core.dll'),(Join-Path $framework 'System.Xml.dll'),
 (Join-Path $GameRoot 'BepInEx\core\BepInEx.dll'),(Join-Path $GameRoot 'BepInEx\core\0Harmony.dll'),(Join-Path $GameRoot 'BepInEx\core\Mono.Cecil.dll'),
 (Join-Path $GameRoot 'Sanctuary_Data\Managed\netstandard.dll'),(Join-Path $GameRoot 'Sanctuary_Data\Managed\Trebuchet.dll'),
 (Join-Path $GameRoot 'Sanctuary_Data\Managed\UnityEngine.dll'),(Join-Path $GameRoot 'Sanctuary_Data\Managed\UnityEngine.CoreModule.dll'),
 (Join-Path $GameRoot 'Sanctuary_Data\Managed\UnityEngine.IMGUIModule.dll'),(Join-Path $GameRoot 'Sanctuary_Data\Managed\UnityEngine.TextRenderingModule.dll'),
 (Join-Path $GameRoot 'Sanctuary_Data\Managed\UnityEngine.UI.dll'),(Join-Path $GameRoot 'Sanctuary_Data\Managed\UnityEngine.UIModule.dll'),
 (Join-Path $GameRoot 'Sanctuary_Data\Managed\UnityEngine.ImageConversionModule.dll'),
 (Join-Path $GameRoot 'Sanctuary_Data\Managed\UnityEngine.InputLegacyModule.dll'),
 (Join-Path $GameRoot 'Sanctuary_Data\Managed\Unity.TextMeshPro.dll'),(Join-Path $GameRoot 'Sanctuary_Data\Managed\Unity.Collections.dll'),
 (Join-Path $GameRoot 'Sanctuary_Data\Managed\Newtonsoft.Json.dll')
)
foreach($p in $paths){if(-not(Test-Path -LiteralPath $p)){throw "Missing build reference: $p"}}
[Microsoft.CodeAnalysis.MetadataReference[]]$refs=$paths|ForEach-Object{[Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_)}
$options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary,[Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithDeterministic($true)
$comp=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('SanctuaryOverhauledLobby',$trees,$refs,$options)
$build=Join-Path $root 'build';New-Item -ItemType Directory -Path $build -Force|Out-Null
$compileHistory=Join-Path $build 'compile-history';New-Item -ItemType Directory -Path $compileHistory -Force|Out-Null
$dll=Join-Path $build 'SanctuaryOverhauledLobby.dll';$pending=Join-Path $compileHistory ('compile-'+[Guid]::NewGuid().ToString('N')+'.pending');$stream=[IO.File]::Open($pending,[IO.FileMode]::CreateNew)
try{$emit=$comp.Emit($stream)}finally{$stream.Dispose()}
$emit.Diagnostics|Where-Object{$_.Severity -eq 'Error'}|ForEach-Object{Write-Error $_.ToString()}
if(-not $emit.Success){throw 'Compile failed.'}
Copy-Item -LiteralPath $pending -Destination $dll -Force
$payload=Join-Path $root 'package\SanctuaryOverhauledLobby';Copy-Item -LiteralPath $dll -Destination (Join-Path $payload 'SanctuaryOverhauledLobby.dll') -Force
[ordered]@{builtUtc=(Get-Date).ToUniversalTime().ToString('o');version='1.2.0';dll=$dll;sha256=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant();sourceFiles=@($sourceFiles|ForEach-Object{[ordered]@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $build 'build-receipt.json') -Encoding utf8
Get-Content -Raw -LiteralPath (Join-Path $build 'build-receipt.json')
