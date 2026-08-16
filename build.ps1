param(
    [string]$RimWorldDir = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld",
    [string]$HarmonyDll = ""
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "Source\AutomaticApparel.csproj"

if (-not $HarmonyDll) {
    $HarmonyDll = Join-Path $RimWorldDir "..\..\workshop\content\294100\2009463077\Current\Assemblies\0Harmony.dll"
}

Write-Host "Building Automatic Apparel Manager"
Write-Host "RimWorld: $RimWorldDir"
Write-Host "Harmony:  $HarmonyDll"

dotnet build $project -c Release -p:RimWorldDir="$RimWorldDir" -p:HarmonyDll="$HarmonyDll"
