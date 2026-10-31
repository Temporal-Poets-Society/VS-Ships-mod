param(
    [string]$SeaRoot = 'C:\VS mods\Sea',
    [string]$SeaSourceRoot = 'C:\VS mods\Sea\.claude\worktrees\codex-storm-waves',
    [string]$GameRoot = 'C:\Users\andri\AppData\Roaming\Vintagestory',
    [string]$Python = 'C:\AstoCAD\bin\python.exe'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$work = Join-Path $root 'obj/issue30'
New-Item -ItemType Directory -Force $work | Out-Null
$nuget = Join-Path $work 'nuget.config'
Set-Content -LiteralPath $nuget -Value '<configuration><packageSources><clear /></packageSources></configuration>'
$oldAppData = $env:APPDATA
$oldCliHome = $env:DOTNET_CLI_HOME
Push-Location $root
try {
    # Keep NuGet/CLI state out of the user's profile. Read game references explicitly.
    $env:APPDATA = Join-Path $work 'build-appdata'
    $env:DOTNET_CLI_HOME = Join-Path $work 'dotnet-home'
    New-Item -ItemType Directory -Force $env:APPDATA | Out-Null
    $common = @("-p:VintageStoryPath=$GameRoot", "-p:SeaRoot=$SeaRoot", "-p:RestoreConfigFile=$nuget", '-p:UseSharedCompilation=false')
    & dotnet build Hansa.csproj @common '-p:BaseIntermediateOutputPath=obj/issue30-build/' '-p:OutputPath=bin/issue30-build/' --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Hansa build failed' }

    # Another job may be building Sea. Freeze a read-only input copy for this run.
    $snapshot = Join-Path $root ('obj/issue30-inputs/' + [Guid]::NewGuid().ToString('N') + '/Sea')
    foreach ($file in @('src/Water/WaterShader.cs', 'src/Water/WaterRenderer.cs', 'src/Water/WaveField.cs', 'modinfo.json', 'bin/Debug/mod/Sea.dll')) {
        $target = Join-Path $snapshot $file
        New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
        Copy-Item -LiteralPath (Join-Path $SeaSourceRoot $file) -Destination $target
    }
    & dotnet build tests/Issue30Tests @common "-p:HansaAssembly=$root/bin/issue30-build/Ships.dll" "-p:SeaAssembly=$snapshot/bin/Debug/mod/Sea.dll" --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Issue30Tests build failed' }
    & dotnet tests/Issue30Tests/bin/Debug/net8.0/Issue30Tests.dll $root $snapshot | Tee-Object -FilePath (Join-Path $work 'test.log')
    if ($LASTEXITCODE -ne 0) { throw 'Issue30Tests failed' }
    & $Python tests/Issue30Tests/plot.py
    if ($LASTEXITCODE -ne 0) { throw 'Plot generation failed' }
} finally {
    Pop-Location
    $env:APPDATA = $oldAppData
    $env:DOTNET_CLI_HOME = $oldCliHome
}
