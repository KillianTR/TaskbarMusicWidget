# Detener proceso previo si est en ejecucin
Get-Process -Name TaskbarMusicWidget -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "Compilando y publicando TaskbarMusicWidget..." -ForegroundColor Cyan

# Publicar en Release
$projectDir = $PSScriptRoot
Set-Location $projectDir
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error al compilar el widget." -ForegroundColor Red
    exit $LASTEXITCODE
}

# Ruta del ejecutable generado
$exePath = Join-Path $projectDir "bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\TaskbarMusicWidget.exe"
$workDir = [System.IO.Path]::GetDirectoryName($exePath)

# Intentar actualizar tambin en Program Files si hay permisos suficientes
try {
    $progFilesDir = "C:\Program Files\TaskbarMusicWidget"
    if (Test-Path $progFilesDir) {
        Copy-Item -Path $exePath -Destination (Join-Path $progFilesDir "TaskbarMusicWidget.exe") -Force -ErrorAction Stop
        Write-Host "Copia en Program Files actualizada correctamente." -ForegroundColor Green
    }
} catch {
    # Si no es administrador no es crtico, el acceso directo apunta a la versin de publicacin local
}

# Actualizar el acceso directo en Inicio (Startup)
try {
    $startupFolder = [Environment]::GetFolderPath('Startup')
    $shortcutFile = Join-Path $startupFolder "TaskbarMusicWidget.exe - Acceso directo.lnk"
    $wsh = New-Object -ComObject WScript.Shell
    $shortcut = $wsh.CreateShortcut($shortcutFile)
    $shortcut.TargetPath = $exePath
    $shortcut.WorkingDirectory = $workDir
    $shortcut.Save()
    Write-Host "Acceso directo en Inicio actualizado a: $exePath" -ForegroundColor Green
} catch {
    Write-Host "Aviso: No se pudo actualizar el acceso directo en Inicio: $_" -ForegroundColor Yellow
}

# Iniciar el widget actualizado
Write-Host "Iniciando TaskbarMusicWidget..." -ForegroundColor Green
Start-Process -FilePath $exePath -WorkingDirectory $workDir

Write-Host "Widget actualizado y en ejecución con éxito!" -ForegroundColor Green
