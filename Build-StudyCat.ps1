$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
$references = @([System.Uri].Assembly.Location, [System.Linq.Enumerable].Assembly.Location, [System.Xml.XmlDocument].Assembly.Location, [System.Xaml.XamlReader].Assembly.Location, [System.Windows.Threading.Dispatcher].Assembly.Location, [System.Windows.Media.Brush].Assembly.Location, [System.Windows.Window].Assembly.Location)
$output = Join-Path $PSScriptRoot 'StudyCat-Pets.exe'
$temporaryOutput = Join-Path $PSScriptRoot 'StudyCat.build.exe'
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Start-StudyCat.ps1') -ExportIcon
if ($LASTEXITCODE -ne 0) { throw 'Cat icon generation failed' }
if (Test-Path -LiteralPath $temporaryOutput) { Remove-Item -LiteralPath $temporaryOutput }
$compiler = New-Object System.CodeDom.Compiler.CompilerParameters
$compiler.CompilerOptions = '/target:winexe /win32icon:"' + (Join-Path $PSScriptRoot 'cat.ico') + '" /resource:"' + (Join-Path $PSScriptRoot 'assets\calico-atlas.png') + '",StudyCat.CalicoAtlas /resource:"' + (Join-Path $PSScriptRoot 'assets\ragdoll-atlas.png') + '",StudyCat.RagdollAtlas'
$compiler.GenerateExecutable = $true
$compiler.GenerateInMemory = $false
$compiler.OutputAssembly = $temporaryOutput
$compiler.ReferencedAssemblies.AddRange($references)
Add-Type -TypeDefinition ([System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'StudyCat.cs'))) -CompilerParameters $compiler
Move-Item -LiteralPath $temporaryOutput -Destination $output -Force
Write-Output 'PASS: independent desktop executable compiled'
