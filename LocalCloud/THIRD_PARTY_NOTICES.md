# Third-party notices

LocalCloud uses unmodified third-party dependencies under their respective licenses. Original user assets are never bundled in the application. Procedural test images belong to this project.

- .NET / ASP.NET Core — Microsoft, MIT. https://github.com/dotnet/runtime / https://github.com/dotnet/aspnetcore
- Microsoft.Data.Sqlite / EF Core component — MIT. https://github.com/dotnet/efcore
- SQLite / SQLitePCLRaw — SQLite public domain / SQLitePCLRaw Apache-2.0. https://sqlite.org / https://github.com/ericsink/SQLitePCL.raw
- tusdotnet / tus-js-client — MIT. https://github.com/tusdotnet/tusdotnet / https://github.com/tus/tus-js-client
- React — Meta, MIT. https://github.com/facebook/react
- Vite / TypeScript / Lucide / TanStack Virtual / qrcode.react / SignalR — see included package metadata and upstream licenses; MIT/Apache-2.0 as applicable.
- Manrope — SIL Open Font License 1.1. https://github.com/sharanda/manrope . Distributed locally through Fontsource; OFL is included in LICENSES.
- MetadataExtractor — Apache-2.0. https://github.com/drewnoakes/metadata-extractor-dotnet
- SixLabors.ImageSharp — Apache-2.0 for this source-available application under the qualifying criteria in the Six Labors Split License. https://github.com/SixLabors/ImageSharp/blob/main/LICENSE . The granted Apache-2.0 text is included as LICENSES/Apache-2.0.txt; the upstream split-license eligibility text is preserved as supporting metadata. Six Labors retains its copyright.
- Magick.NET / ImageMagick — Apache-2.0 / ImageMagick License, plus embedded native library licenses. https://github.com/dlemstra/Magick.NET / https://imagemagick.org/script/license.php . Includes libheif and HEIC decoding. Upstream packages preserve their embedded licensing notices.
- Makaretu.Dns.Multicast — MIT. https://github.com/richardschneider/net-mdns

## Separately downloaded FFmpeg tools

Public Setup/Core do not contain FFmpeg binaries. At the user's choice Setup
obtains unmodified FFmpeg/FFprobe 9.0.2 essentials directly from Gyan Doshi:
https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip

Pinned archive SHA-256:
`60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`

The provider licenses this static essentials build under GPLv3. It is a separate
command-line tool, not linked into LocalCloud. Its LICENSE and README.txt are
retained in server/tools when downloaded. The provider's build/source information
is at https://www.gyan.dev/ffmpeg/builds/ and its linked FFmpeg commit:
https://github.com/FFmpeg/FFmpeg/commit/946fcce07b .
The LocalCloud first-party restrictions do not apply to FFmpeg or its components.
Existing installations retain their separately acquired media tools during update.
The application itself does not download tools while serving the library.

Microsoft.Web.WebView2 SDK 1.0.4258.31 (Microsoft license, NuGet package). SDK Core/WinForms/Loader shipped in desktop; Evergreen WebView2 Runtime is a separate system component, not included in the application payload. Setup may explicitly obtain the official Microsoft bootstrapper when selected; it validates the Microsoft Authenticode signature before execution. Official documentation: https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution .

## PDF tools added in 1.3

PDF.js / pdfjs-dist: Apache-2.0, Mozilla. pdf-lib and @pdf-lib/fontkit: MIT. PDF.js decoder/CMap/standard-font licenses accompany the locally copied assets. DejaVu Sans is used for Cyrillic annotations; its license is in LICENSES/DejaVu.txt.

## Installer

NSIS — zlib/libpng license and applicable component licenses. https://nsis.sourceforge.io/License . Compiler/runtime notices are included in LICENSES/NSIS.txt.

Detailed package versions and included license texts: LICENSES/DEPENDENCIES.md and LICENSES/dependencies/.
