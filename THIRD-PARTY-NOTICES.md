# Third-party notices

ComfyGenReader bundles the components below. Each is used unmodified under its own license.

| Component | Version | License | Source |
|---|---|---|---|
| FFmpeg (BtbN `lgpl-shared` build; ffmpeg, ffprobe and shared libs in `lib\ffmpeg\`) | n9.0.2 (build autobuild-2026-10-20-13-08, `n9.0.2-29-g6dfaba896f`) | LGPL v2.1 or later | https://ffmpeg.org/ , https://github.com/BtbN/FFmpeg-Builds , https://git.ffmpeg.org/ffmpeg.git |
| LibVLC / VLC native libraries (`lib\libvlc\`, NuGet `VideoLAN.LibVLC.Windows`) | 3.0.24 | LGPL v2.1 or later | https://www.videolan.org/vlc/ , https://code.videolan.org/videolan/vlc , https://www.nuget.org/packages/VideoLAN.LibVLC.Windows |
| LibVLCSharp, LibVLCSharp.WPF | 3.10.2 | LGPL-2.1-or-later | https://github.com/videolan/libvlcsharp |
| MetadataExtractor | 2.9.3 | Apache-2.0 | https://github.com/drewnoakes/metadata-extractor-dotnet |
| XmpCore (dependency of MetadataExtractor, bundled in the exe) | 6.1.10.1 | BSD-style license of the Adobe XMP SDK (nuspec licenseUrl https://www.adobe.com/devnet/xmp/library/eula-xmp-library-java.html; project README: "the same BSD license" as Adobe XMP SDK) | https://github.com/drewnoakes/xmp-core-dotnet/ |
| .NET runtime (self-contained, bundled in the exe) | 10.x | MIT | https://github.com/dotnet/runtime |

The LGPL components are shipped as separate shared libraries (DLLs) in `lib\`, so they can be replaced by the user with a compatible build. Corresponding source is available at the URLs above.

## Vulnerability check

Performed 2026-10-20.

1. NuGet: `dotnet list package --vulnerable --include-transitive` on the solution reported "has no vulnerable packages" for ComfyGenReader.Core, ComfyGenReader and ComfyGenReader.Core.Tests. Builds also run with NuGetAudit (all levels) and NU1901-NU1904 as errors.
2. FFmpeg n9.0.2 against the NVD API (https://services.nvd.nist.gov/rest/json/cves/2.0):
   - `virtualMatchString=cpe:2.3:a:ffmpeg:ffmpeg:9.0.2` returned 7 CVEs, none Critical or High (CVE-2025-25468/25469 Medium; CVE-2026-107675, -107677, -107678, -107696 Medium; CVE-2026-107676 Low). The Medium/Low issues concern network protocol handlers (sftp host-key check, RTSP redirect loop) and memory leaks; ComfyGenReader only opens local files and never uses network URLs, so they are not reachable.
   - `keywordSearch=ffmpeg&cvssV3Severity=CRITICAL` for 2026-01-01..2026-10-20 returned only CVE-2026-33024 (AVideo) and CVE-2026-35033 (Jellyfin), which are other products that invoke ffmpeg, not FFmpeg itself.
   - Finding: no unpatched Critical CVE affecting FFmpeg n9.0.2.
3. VLC / LibVLC 3.0.24 (exact version taken from the `libvlc.dll` / `libvlccore.dll` file version of the NuGet package):
   - `virtualMatchString=cpe:2.3:a:videolan:vlc_media_player:3.0.24` returned 0 CVEs.
   - `keywordSearch=vlc&cvssV3Severity=CRITICAL` returned only CVE-2014-6440, CVE-2016-5108, CVE-2017-10699, CVE-2018-19857, CVE-2019-12874, CVE-2019-13962, CVE-2023-47359, all fixed in releases before 3.0.24.
   - `keywordSearch=vlc` for 2026-01-01..2026-10-20 showed CVE-2026-56711 (High, affects 3.0.0 through 3.0.23) and CVE-2026-102875 (High, skins2 path traversal, fixed before 3.0.24); both are fixed in the bundled 3.0.24. The remaining 2026 hits were Medium/Low or other products.
   - Finding: no unpatched Critical CVE affecting VLC/LibVLC 3.0.24.

Re-run these queries (and bump the pins) when updating FFmpeg or the LibVLC package.
