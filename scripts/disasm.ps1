param(
    [Parameter(Mandatory=$false)][string]$Class,
    [Parameter(Mandatory=$false)][string]$Method,
    [string]$OutFile,
    [string]$Binary = "H:\steam\steamapps\common\Sixtar Gate STARGAZER\GameAssembly.dll",
    [string]$DumpCs = "$PSScriptRoot\..\il2cpp_dump\dump.cs",
    [string]$ScriptJson = "$PSScriptRoot\..\il2cpp_dump\script.json",
    [string]$ToolPy = "H:\source\repos\rea tool mods\tools\il2cpp_asm.py"
)

$cmdArgs = @(
    $ToolPy,
    "--binary", $Binary,
    "--dump", $DumpCs,
    "--script-json", $ScriptJson
)

if ($Class) { $cmdArgs += @("--class", $Class) }
if ($Method) { $cmdArgs += @("--method", $Method) }
if ($OutFile) { $cmdArgs += @("--out", $OutFile) }

& python $cmdArgs
