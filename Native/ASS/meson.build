project('vrplayerass', 'c', 'cpp', version: '1.0', default_options: ['buildtype=release', 'default_library=static', 'b_ndebug=true', 'b_vscrt=mt'])

subproject('freetype', default_options: ['harfbuzz=disabled', 'zlib=disabled', 'bzip2=disabled', 'png=disabled', 'brotli=disabled'])
subproject('fribidi', default_options: ['docs=false', 'tests=false', 'bin=false'])
subproject('harfbuzz', default_options: ['tests=disabled', 'utilities=disabled', 'glib=disabled', 'gobject=disabled', 'cairo=disabled', 'freetype=disabled', 'icu=disabled'])
ass = subproject('libass', default_options: ['fontconfig=disabled', 'directwrite=disabled', 'coretext=disabled', 'require-system-font-provider=false', 'libunibreak=disabled', 'test=disabled', 'compare=disabled', 'profile=disabled', 'checkasm=disabled', 'asm=disabled'])
shared_library('vrplayerass', 'AssBridge.c', dependencies: ass.get_variable('libass_dep'), install: true)
