param(
    [Parameter(Mandatory=$false)][string]$Class,
    [Parameter(Mandatory=$false)][string]$Method,
    [Parameter(Mandatory=$false)][string]$Rva,
    [string]$OutFile,
    [string]$Dll = "H:\steam\steamapps\common\Sixtar Gate STARGAZER\GameAssembly.dll",
    [string]$ScriptJson = "$PSScriptRoot\..\il2cpp_dump\script.json",
    [string]$ToolPy = "H:\source\repos\rea tool mods\tools\pe_slice.py"
)

$cmdArgs = @(
    $ToolPy,
    "--dll", $Dll,
    "--script-json", $ScriptJson
)

if ($Class) { $cmdArgs += @("--class", $Class) }
if ($Method) { $cmdArgs += @("--method", $Method) }
if ($Rva) { $cmdArgs += @("--rva", $Rva) }
if ($OutFile) { $cmdArgs += @("--out", $OutFile) }

& python $cmdArgs
