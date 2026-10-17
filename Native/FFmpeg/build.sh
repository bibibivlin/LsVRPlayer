#!/usr/bin/env bash
# The wrapper remains Apache-2.0. FFmpeg is built as replaceable LGPL shared libraries.
set -euo pipefail
source_dir="$1"
build_dir="$2"
ndk="$3"
make_bin="$4"
toolchain="$ndk/toolchains/llvm/prebuilt/windows-x86_64"
export PATH="$toolchain/bin:$(dirname "$make_bin"):$PATH"
mkdir -p "$build_dir"
cd "$build_dir"
bash "$source_dir/configure" \
  --prefix=/vrplayer-ffmpeg \
  --target-os=android --arch=aarch64 --enable-cross-compile \
  --cc="$toolchain/bin/clang.exe --target=aarch64-linux-android29" \
  --host-cc="$toolchain/bin/clang.exe --target=aarch64-linux-android29" \
  --cxx="$toolchain/bin/clang++.exe --target=aarch64-linux-android29" \
  --ar="$toolchain/bin/llvm-ar.exe" --nm="$toolchain/bin/llvm-nm.exe" \
  --ranlib="$toolchain/bin/llvm-ranlib.exe" --strip="$toolchain/bin/llvm-strip.exe" \
  --sysroot="$toolchain/sysroot" \
  --disable-gpl --disable-nonfree --disable-version3 \
  --disable-static --enable-shared --enable-pic \
  --disable-programs --disable-doc --disable-debug --disable-autodetect \
  --disable-everything --disable-avdevice --disable-avfilter --disable-avformat \
  --disable-swscale --disable-network --disable-asm --disable-iconv --disable-zlib --disable-bzlib --disable-lzma \
  --enable-swresample \
  --enable-decoder=aac,ac3,eac3,dca,truehd,mlp,flac,alac,opus,vorbis,mp3,amrnb,amrwb,pcm_mulaw,pcm_alaw \
  --extra-cflags="-O2 -ffile-prefix-map=$source_dir=/ffmpeg -ffile-prefix-map=$build_dir=/build" \
  --extra-ldflags=-Wl,-z,max-page-size=16384
# NDK GNU make is a native Windows executable, so its includes must use drive paths.
source_posix="$(cd "$source_dir" && pwd)"
sed -i "s|$source_posix|$source_dir|g" Makefile ffbuild/config.mak ffbuild/config.sh
"$make_bin" -j8 SHELL=sh
