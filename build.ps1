param(
    [string]$RimWorldDir = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyDll = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [switch]$Tests
)
$ErrorActionPreference = 'Stop'
if ([Environment]::Version.Major -lt 10) { throw 'Use PowerShell 7.6+ (.NET 10), or build the csproj with the .NET 10 SDK.' }
if (!(Test-Path -LiteralPath (Join-Path $RimWorldDir 'Assembly-CSharp.dll'))) { throw 'Set -RimWorldDir to your RimWorld 1.7 installation.' }
if (!(Test-Path -LiteralPath $HarmonyDll)) { throw 'Set -HarmonyDll to the installed Harmony 1.7 assembly.' }
[Reflection.Assembly]::LoadFrom((Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')) | Out-Null
$paths = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($path in ([AppContext]::GetData('TRUSTED_PLATFORM_ASSEMBLIES') -split [IO.Path]::PathSeparator)) { $paths[[IO.Path]::GetFileNameWithoutExtension($path)] = $path }
# Use runtime references without bundling them. The game remains the authority for its own APIs.
foreach ($name in @('Assembly-CSharp.dll','UnityStubs.dll','Waylith.UnityCompat.dll','Waylith.dll','Brutal.Core.Numerics.dll','System.Security.Cryptography.ProtectedData.dll')) {
    $path = Join-Path $RimWorldDir $name
    if (Test-Path -LiteralPath $path) { $paths[[IO.Path]::GetFileNameWithoutExtension($path)] = $path }
}
$paths['0Harmony'] = $HarmonyDll
$references = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($p in $paths.Values) { $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($p)) }
$trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
$files = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Source') -Recurse -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/]obj[\\/]' })
if ($Tests) { $files += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Tests') -Filter '*.cs' }
foreach ($file in $files) { $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file.FullName), $null, $file.FullName)) }
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable)
$assemblyName = if ($Tests) { 'SpeakYourMind.Tests' } else { 'SpeakYourMind' }
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($assemblyName, $trees, $references, $options)
$outDir = Join-Path $PSScriptRoot $(if ($Tests) { 'artifacts/tests' } else { '1.7/Assemblies' })
[IO.Directory]::CreateDirectory($outDir) | Out-Null
$output = Join-Path $outDir ($assemblyName + '.dll')
$stream = [IO.File]::Create($output)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
$result.Diagnostics | Where-Object { $_.Severity -eq 'Error' -or $_.Severity -eq 'Warning' } | ForEach-Object ToString
if (!$result.Success) { throw 'Compilation failed.' }
"Built $output"
if ($Tests) {
    [System.Runtime.Loader.AssemblyLoadContext]::Default.add_Resolving({ param($ctx,$name)
        $p = Join-Path $RimWorldDir ($name.Name + '.dll')
        if ($name.Name -eq '0Harmony') { $p = $HarmonyDll }
        if (Test-Path -LiteralPath $p) { return $ctx.LoadFromAssemblyPath($p) }
        return $null
    })
    $a = [Reflection.Assembly]::LoadFrom($output)
    $a.GetType('SpeakYourMind.Tests.Suite').GetMethod('Run').Invoke($null, $null)
}
