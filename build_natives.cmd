@echo off
title Building Natives

setlocal enabledelayedexpansion

echo Script PATH
:: Caminho do script
set scriptPath=%~dp0

echo running from %scriptPath%

set dcimguiPath=%scriptPath%dcimgui

:: Construir a imagem Docker
docker build -f dear_bindings.Dockerfile -t dear_bindings:v1 .

echo --- BUILT ---

set folder="dcimgui"
if not exist "%folder%" (
    mkdir "%folder%"
) else (
    echo "%folder%" dir is already present
    echo Clearing "%folder%" directory
    rmdir /s /q "%folder%"
    echo "%folder%" directory is empty
)

:: Executar o container Docker
docker run --rm -i -v "%scriptPath%dcimgui:/dcimgui_build" dear_bindings:v1

echo --- RAN ---

echo Press any key to exit...
pause >nul