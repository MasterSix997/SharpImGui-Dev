@echo off
title Building Natives

setlocal enabledelayedexpansion

set scriptPath=%~dp0

echo running from %scriptPath%

set dcimguiPath=%scriptPath%dcimgui

:: Build docker image for building natives
docker build --build-arg IMGUI_VERSION=v1.91.5-docking -f dear_bindings.Dockerfile -t dear_bindings:v1 .

echo --- BUILT ---

set folder="dcimgui"
if not exist "%folder%" (
    mkdir "%folder%"
) else (
    echo "%folder%" dir is already present
    echo Clearing "%folder%" directory
    rmdir /s /q "%folder%"
)

:: Execute docker container to build natives
docker run --rm -i -v "%scriptPath%dcimgui:/dcimgui_build" dear_bindings:v1

echo --- RAN ---

echo Press any key to exit...
pause >nul