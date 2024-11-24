#!/bin/bash
# Building Natives

echo "Script PATH"
# Caminho do script
scriptPath="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

echo "running from $scriptPath"

dcimguiPath="$scriptPath/dcimgui"

# Construir a imagem Docker
docker build --build-arg IMGUI_VERSION=v1.91.5-docking -f dear_bindings.Dockerfile -t dear_bindings:v1 .

echo "--- BUILT ---"

folder="dcimgui"
if [ ! -d "$folder" ]; then
    mkdir "$folder"
else
    echo "$folder directory is already present"
    echo "Clearing $folder directory"
    rm -rf "$folder"
    echo "$folder directory is empty"
fi

# Executar o container Docker
docker run --rm -i -v "$scriptPath/dcimgui:/dcimgui_build" dear_bindings:v1

echo "--- RAN ---"

echo "Press any key to exit..."
read -n 1 -s