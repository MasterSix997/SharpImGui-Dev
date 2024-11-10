# syntax=docker/dockerfile:1

# Setup Sources
FROM alpine AS sources
WORKDIR /dear_bindings
RUN apk update
RUN apk add git openssh-client
RUN git clone https://github.com/dearimgui/dear_bindings.git .
RUN git clone https://github.com/ocornut/imgui.git imgui

# Run Python Code Generator
FROM python AS generator
WORKDIR /dear_bindings
COPY --from=sources /dear_bindings .
RUN pip3 install ply
RUN python3 dear_bindings.py --output dcimgui imgui/imgui.h --generateunformattedfunctions

# Compile for Windows x64
FROM ubuntu:20.04 AS compile-windows-x64
WORKDIR /dcimgui
COPY --from=generator /dear_bindings .
RUN apt-get update && apt-get install -y mingw-w64
RUN x86_64-w64-mingw32-gcc -std=c++11 -shared -DCIMGUI_API='extern "C" __declspec(dllexport)' -DIMGUI_STATIC -DIMGUI_DISABLE_OBSOLETE_FUNCTIONS=1 \
    -O2 -fno-exceptions -fno-rtti -fno-threadsafe-statics -o dcimgui_x64.dll -I. -Iimgui -limm32 -lstdc++ \
    -x c++ dcimgui.cpp imgui/imgui.cpp imgui/imgui_demo.cpp imgui/imgui_draw.cpp imgui/imgui_tables.cpp imgui/imgui_widgets.cpp -lm

# Compile for Windows x86
FROM ubuntu:20.04 AS compile-windows-x86
WORKDIR /dcimgui
COPY --from=generator /dear_bindings .
RUN apt-get update && apt-get install -y mingw-w64
RUN i686-w64-mingw32-gcc -std=c++11 -shared -DCIMGUI_API='extern "C" __declspec(dllexport)' -DIMGUI_STATIC -DIMGUI_DISABLE_OBSOLETE_FUNCTIONS=1 \
    -O2 -fno-exceptions -fno-rtti -fno-threadsafe-statics -o dcimgui_x86.dll -I. -Iimgui -limm32 -lstdc++ \
    -x c++ dcimgui.cpp imgui/imgui.cpp imgui/imgui_demo.cpp imgui/imgui_draw.cpp imgui/imgui_tables.cpp imgui/imgui_widgets.cpp -lm

# Compile for macOS ARM
FROM --platform=linux/amd64 ghcr.io/shepherdjerred/macos-cross-compiler AS compile-macos-arm
WORKDIR /workspace
COPY --from=generator /dear_bindings .
RUN aarch64-apple-darwin22-gcc -std=c++11 -shared -fPIC -DIMGUI_STATIC -DIMGUI_DISABLE_OBSOLETE_FUNCTIONS=1 \
    -O2 -fno-exceptions -fno-rtti -fno-threadsafe-statics -o dcimgui.dylib -I. -Iimgui \
    -x c++ dcimgui.cpp imgui/imgui.cpp imgui/imgui_demo.cpp imgui/imgui_draw.cpp imgui/imgui_tables.cpp imgui/imgui_widgets.cpp -lm -lstdc++ 

# Compile for Linux
FROM gcc AS compile-linux
WORKDIR /dcimgui
COPY --from=generator /dear_bindings .
RUN gcc -std=c++11 -shared -fPIC -DCIMGUI_API='extern "C"' -DIMGUI_STATIC -DIMGUI_DISABLE_OBSOLETE_FUNCTIONS=1 \
    -O2 -fno-exceptions -fno-rtti -fno-threadsafe-statics -o dcimgui.so -Iimgui -I. -Wall \
    -x c++ dcimgui.cpp imgui/imgui.cpp imgui/imgui_demo.cpp imgui/imgui_draw.cpp imgui/imgui_tables.cpp imgui/imgui_widgets.cpp -lm -lstdc++

# Compile for Android (ARM and x86)
FROM ubuntu:20.04 AS compile-android
WORKDIR /dcimgui
COPY --from=generator /dear_bindings .
RUN apt-get update && apt-get install -y openjdk-8-jdk wget unzip
RUN wget https://dl.google.com/android/repository/android-ndk-r21e-linux-x86_64.zip && unzip android-ndk-r21e-linux-x86_64.zip
ENV ANDROID_NDK_HOME=/dcimgui/android-ndk-r21e
ENV PATH=$PATH:$ANDROID_NDK_HOME/toolchains/llvm/prebuilt/linux-x86_64/bin
# Compilar para ARM
RUN aarch64-linux-android21-clang++ -std=c++11 -shared -fPIC -DIMGUI_STATIC -DIMGUI_DISABLE_OBSOLETE_FUNCTIONS=1 \
    -o libdcimgui_arm.so -I. -Iimgui -x c++ dcimgui.cpp imgui/imgui.cpp imgui/imgui_demo.cpp imgui/imgui_draw.cpp imgui/imgui_tables.cpp imgui/imgui_widgets.cpp -lm
# Compilar para x86
RUN i686-linux-android21-clang++ -std=c++11 -shared -fPIC -DIMGUI_STATIC -DIMGUI_DISABLE_OBSOLETE_FUNCTIONS=1 \
    -o libdcimgui_x86.so -I. -Iimgui -x c++ dcimgui.cpp imgui/imgui.cpp imgui/imgui_demo.cpp imgui/imgui_draw.cpp imgui/imgui_tables.cpp imgui/imgui_widgets.cpp -lm

# Final stage to gather all artifacts
FROM alpine AS final
COPY --from=compile-linux /dcimgui/dcimgui.so /final/dcimgui.so
COPY --from=compile-windows-x64 /dcimgui/dcimgui_x64.dll /final/dcimgui_x64.dll
COPY --from=compile-windows-x86 /dcimgui/dcimgui_x86.dll /final/dcimgui_x86.dll
COPY --from=compile-macos-arm /workspace/dcimgui.dylib /final/dcimgui.dylib
COPY --from=compile-android /dcimgui/libdcimgui_arm.so /final/libdcimgui_arm.so
COPY --from=compile-android /dcimgui/libdcimgui_x86.so /final/libdcimgui_x86.so
COPY --from=generator /dear_bindings/dcimgui.json /final/dcimgui.json
COPY --from=generator /dear_bindings/dcimgui.h /final/dcimgui.h
COPY --from=generator /dear_bindings/dcimgui.cpp /final/dcimgui.cpp

# uncomment to get a full build directory
#COPY --from=compile-linux /dcimgui/ /final/linux-build
#COPY --from=compile-windows /dcimgui/ /final/windows-build
#COPY --from=compile-macos-arm /dcimgui/ /final/macos-arm-build
#COPY --from=compile-android /dcimgui/ /final/android-build

RUN echo "Success"
CMD ["/bin/cp", "-r", "/final/.", "/dcimgui_build"]