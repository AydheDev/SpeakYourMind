param(
    [string]$RimWorldDir = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyDll = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [string]$BubblesDll = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\3830600373\1.7\Assemblies\Bubbles.dll'
)
$ErrorActionPreference = 'Stop'
[System.Runtime.Loader.AssemblyLoadContext]::Default.add_Resolving({ param($ctx,$name)
    $p = Join-Path $RimWorldDir ($name.Name + '.dll')
    if ($name.Name -eq '0Harmony') { $p = $HarmonyDll }
    if (Test-Path -LiteralPath $p) { return $ctx.LoadFromAssemblyPath($p) }
    return $null
})
$game = [Reflection.Assembly]::LoadFrom((Join-Path $RimWorldDir 'Assembly-CSharp.dll'))
$harmony = [Reflection.Assembly]::LoadFrom($HarmonyDll)
$bubbles = [Reflection.Assembly]::LoadFrom($BubblesDll)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
$bubble = $bubbles.GetType('Bubbles.Core.Bubble', $true)
$bubbler = $bubbles.GetType('Bubbles.Core.Bubbler', $true)
$checks = @{
    'Bubble constructor' = $bubble.GetConstructor(@([Verse.Pawn],[Verse.LogEntry]))
    'Bubble.Entry' = $bubble.GetProperty('Entry',$flags)
    'Bubble.GetFade' = $bubble.GetMethod('GetFade',$flags)
    'Bubble.Rebuild' = $bubble.GetMethod('Rebuild',$flags)
    'Bubble.Draw' = $bubble.GetMethod('Draw',$flags)
    'Bubbler.Dictionary' = $bubbler.GetField('Dictionary',$flags)
    'Bubbler.Add' = $bubbler.GetMethod('Add',$flags)
    'Bubbler.CanRender' = $bubbler.GetMethod('CanRender',$flags)
}
foreach ($key in $checks.Keys) { if ($null -eq $checks[$key]) { throw "Missing required interface: $key" } }
# Validate the Draw return contract used to record only successfully rendered lines.
$draw = $bubble.GetMethod('Draw',$flags)
if ($draw.ReturnType -ne [bool]) { throw 'Bubble.Draw no longer returns a Boolean.' }
foreach ($key in @('Bubble.GetFade','Bubble.Draw','Bubbler.Add')) {
    $instructions = [HarmonyLib.PatchProcessor]::GetOriginalInstructions($checks[$key])
    if ($instructions.Count -eq 0) { throw "Could not inspect $key" }
}
"PASS: $($checks.Count) required bubble interfaces and method bodies verified against installed assembly."
"RimWorld: $([IO.File]::ReadAllText((Join-Path $RimWorldDir 'Version.txt')).Trim())"
"Bubbles SHA256: $((Get-FileHash -LiteralPath $BubblesDll -Algorithm SHA256).Hash)"
