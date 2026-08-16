param(
    [string]$RimWorldDir = "",
    [string]$HarmonyDll = ""
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "Source\AutomaticApparel.csproj"

if (-not $RimWorldDir) {
    $steamPath = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    if ($steamPath) {
        $RimWorldDir = Join-Path $steamPath "steamapps\common\RimWorld"
    }
}

if (-not $RimWorldDir -or -not (Test-Path (Join-Path $RimWorldDir "RimWorldWin64_Data\Managed\Assembly-CSharp.dll"))) {
    throw "RimWorld 1.6 was not found. Pass -RimWorldDir with the game's install directory."
}

if (-not $HarmonyDll) {
    $HarmonyDll = Join-Path $RimWorldDir "..\..\workshop\content\294100\2009463077\Current\Assemblies\0Harmony.dll"
}

if (-not (Test-Path $HarmonyDll)) {
    throw "Harmony was not found. Pass -HarmonyDll with the path to 0Harmony.dll."
}

Write-Host "Building Automatic Apparel Manager"
Write-Host "RimWorld: $RimWorldDir"
Write-Host "Harmony:  $HarmonyDll"

$sdkAvailable = dotnet --list-sdks
if ($sdkAvailable) {
    dotnet build $project -c Release -p:RimWorldDir="$RimWorldDir" -p:HarmonyDll="$HarmonyDll"
    exit $LASTEXITCODE
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    throw "No .NET SDK or Visual Studio C# compiler was found."
}

$visualStudioDir = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$compiler = Join-Path $visualStudioDir "MSBuild\Current\Bin\Roslyn\csc.exe"
if (-not (Test-Path $compiler)) {
    throw "Visual Studio's C# compiler was not found at $compiler."
}

$managedDir = Join-Path $RimWorldDir "RimWorldWin64_Data\Managed"
$outputDir = Join-Path $PSScriptRoot "1.6\Assemblies"
$outputDll = Join-Path $outputDir "AutomaticApparel.dll"
$sources = Get-ChildItem (Join-Path $PSScriptRoot "Source") -Recurse -Filter "*.cs" | Select-Object -ExpandProperty FullName
$references = @(
    (Join-Path $managedDir "netstandard.dll"),
    (Join-Path $managedDir "Assembly-CSharp.dll"),
    (Join-Path $managedDir "UnityEngine.CoreModule.dll"),
    (Join-Path $managedDir "UnityEngine.IMGUIModule.dll"),
    (Join-Path $managedDir "UnityEngine.TextRenderingModule.dll"),
    $HarmonyDll
) | ForEach-Object { "/reference:$_" }

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
& $compiler /nologo /target:library /langversion:latest /nullable:disable "/out:$outputDll" $references $sources
if ($LASTEXITCODE -ne 0) {
    throw "C# compilation failed with exit code $LASTEXITCODE."
}

Write-Host "Built: $outputDll"
