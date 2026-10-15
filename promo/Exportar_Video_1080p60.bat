@echo off
title Renderizando Video TaskbarMusicWidget 1080p 60fps
cd /d "%~dp0"
echo ====================================================================
echo  Iniciando renderizado directo de video (Sin grabacion de pantalla)
echo ====================================================================
python render_video.py --duration 50.0 --fps 60
echo.
pause
