# FFmpeg runtime source and license

Mosaic dynamically links the five separate DLLs from the BtbN FFmpeg LGPL shared Windows x64 build, FFmpeg n9.0.2-36-g6882969ce9 (autobuild-2026-10-26-14-25).
Source and build scripts: https://github.com/BtbN/FFmpeg-Builds/tree/autobuild-2026-10-26-14-25
FFmpeg source: https://github.com/FFmpeg/FFmpeg/tree/6882969ce9
Build archive: https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-26-14-25/ffmpeg-n9.0.2-36-g6882969ce9-win64-lgpl-shared-9.0.zip
Archive SHA256: 6be6e82a5cdc08aa173cf0a7b746682b4f16492d79473dc09d7c41be834e507b
The DLLs remain separate and replaceable. The LGPL-3.0 and GPL-3.0 texts are included in this directory; all upstream dependency terms continue to apply. FFmpeg.AutoGen's separate license is included as FFmpeg-AutoGen-LICENSE.txt.
This local test build only uses FFmpeg to decode the original VP9-alpha videos. It does not modify the FFmpeg libraries.
