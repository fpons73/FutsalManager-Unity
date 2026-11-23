$ErrorActionPreference = "Stop"

$unityPluginsManaged = "desktop/UniFutsal.Unity/Assets/Plugins/Managed"
$unityPluginsNative  = "desktop/UniFutsal.Unity/Assets/Plugins/x86_64"
$tempPublish         = ".tmp-unity-publish"

if (-not (Test-Path "desktop/UniFutsal.Unity/Assets")) {
    Write-Error "No encuentro el proyecto de Unity en 'desktop/UniFutsal.Unity'."
}

if (Test-Path $tempPublish) { Remove-Item $tempPublish -Recurse -Force }

Write-Host "Compilando cerebro del juego y SQLite (win-x64) para Unity..." -ForegroundColor Cyan
dotnet publish "src/UniFutsal.Infrastructure/UniFutsal.Infrastructure.csproj" `
    -c Release `
    -r win-x64 `
    -p:CopyLocalLockFileAssemblies=true `
    -o $tempPublish

New-Item -ItemType Directory -Force -Path $unityPluginsManaged, $unityPluginsNative | Out-Null

$managedPatterns = @("UniFutsal.*.dll", "Microsoft.Data.Sqlite.dll", "SQLitePCLRaw.*.dll")
$copied = @()

foreach ($pattern in $managedPatterns) {
    Get-ChildItem -Path $tempPublish -Filter $pattern | ForEach-Object {
        Copy-Item $_.FullName -Destination $unityPluginsManaged -Force
        $copied += $_.Name
    }
}

$nativeSqlite = Get-ChildItem -Path $tempPublish -Recurse -Filter "e_sqlite3.dll" | Select-Object -First 1

if ($null -eq $nativeSqlite) {
    Write-Error "No se encontró e_sqlite3.dll en la publicación."
}

Copy-Item $nativeSqlite.FullName -Destination (Join-Path $unityPluginsNative "e_sqlite3.dll") -Force

Remove-Item $tempPublish -Recurse -Force

Write-Host "`nDLLs copiadas a Unity (Managed):" -ForegroundColor Green
$copied | Sort-Object | ForEach-Object { Write-Host "  - $_" }
Write-Host "Motor nativo copiado a Unity (x86_64):" -ForegroundColor Green
Write-Host "  - e_sqlite3.dll"
