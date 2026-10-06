# LocalCloud
# LocalCloud 1.0.0

**Your files. Your Windows PC. Your home network.**

[Русская документация](README.ru.md) · [Installation](#installation) · [Existing installations](#updating-an-existing-installation) · [Build from source](#building-from-source) · [Publication guide](docs/PUBLISHING.md) · [License](LICENSE)

LocalCloud turns a Windows computer into a personal file and media library. Run the desktop application, choose a normal folder on a disk, and access the same library from a desktop browser or a phone on your home network. Photos, videos, music and documents stay in ordinary folders you control. There is no hosted storage account, subscription, cloud backend or integrated AI service.

This is the first **public 1.0.0 release**, based on the previous development build 1.4.0. The public version number starts a new release series; it is not a reduction in features. The SQLite database remains at **schema 4**. A specifically checked transition from development 1.4.0 is provided.

Created by **Ilya Gavrilov** with substantial assistance from AI coding tools, including ChatGPT/Codex. Read [NOTICE.md](NOTICE.md) and [LICENSE](LICENSE) before reusing or publishing source code. This repository is **source-available under custom terms**, not an unrestricted open-source project.

![LocalCloud music library](docs/screenshots/1.0/music-1440.png)

## Contents

- [Requirements](#requirements)
- [Release downloads](#release-downloads)
- [Installation](#installation)
- [First launch and phone access](#first-launch-and-phone-access)
- [Features](#features)
- [Storage and data ownership](#storage-and-data-ownership)
- [Updating an existing installation](#updating-an-existing-installation)
- [Backup, recovery and uninstall](#backup-recovery-and-uninstall)
- [Security and privacy](#security-and-privacy)
- [Troubleshooting](#troubleshooting)
- [Known limitations](#known-limitations)
- [Building from source](#building-from-source)
- [Architecture and tests](#architecture-and-tests)
- [Publishing and reuse](#publishing-and-reuse)

## Requirements

| Component | Requirement |
| --- | --- |
| Host computer | Windows 11, x64. Windows 10 x64 is best effort and was not physically validated for this release. |
| .NET | The release contains the .NET runtime; no SDK is required to use it. |
| Desktop renderer | Microsoft Edge **WebView2 Evergreen Runtime**. This is a runtime, not a requirement to use Edge as your browser. |
| Browser client | A current browser with JavaScript, cookies and local storage enabled. Safari is supported as a browser client subject to its platform limits. |
| Network | A trusted home LAN for phone access. The computer must remain powered on and the server must be running. |
| Disk | Allow roughly 1 GB for installation, temporary extraction and dependencies, plus your media, caches and update backups. |
| Media processing | Compatible `ffmpeg.exe` and `ffprobe.exe`, downloaded by Setup when selected, already installed in `server/tools`, or on PATH. |

Internet is needed during installation only if you choose to download missing WebView2 or FFmpeg. The core application runs locally. Microsoft may update the separately installed WebView2 runtime through its own mechanisms.

**Language:** Setup and the native first-run configuration support English and Russian. The web application and most tray actions are currently Russian. Full English application localization is not included in 1.0.0.

## Release downloads

Open this repository's **Releases** page and choose the desired version. Use actual release assets rather than GitHub's automatically generated source archive when you want a ready application.

| File | Purpose |
| --- | --- |
| `LocalCloud-1.0.0-Setup-x64.exe` | Windows installer with English/Russian selection, shortcuts, uninstall entry and optional dependency downloads. Recommended for new users. |
| `LocalCloud-1.0.0-Windows-x64-Core.zip` | Portable core application and bundled .NET runtime. FFmpeg and WebView2 are separate prerequisites. |
| `LocalCloud-1.0.0-Windows-x64-Update.zip` | Application update, including the development 1.4.0 → public 1.0.0 transition. Existing media tools are retained. |
| `LocalCloud-1.0.0-Source.zip` | Clean repository source, documentation and build/installer scripts; no user library, build output or npm packages. |
| `SHA256SUMS-1.0.0.txt` | SHA-256 checksums for the supplied release assets. |

The Setup/core archive does **not redistribute FFmpeg binaries**. When selected, Setup downloads an unmodified, checksum-pinned FFmpeg 9.0.2 essentials archive directly from [Gyan Doshi's upstream distribution](https://www.gyan.dev/ffmpeg/builds/), preserves its notices, and installs FFmpeg/FFprobe as separate tools. Their GPL terms remain independent of the LocalCloud license. See [third-party notices](THIRD_PARTY_NOTICES.md).

## Installation

1. Download `LocalCloud-1.0.0-Setup-x64.exe` from Releases.
2. Start it and choose **English** or **Русский**.
3. Read the license and choose an **application folder**. The default is `%LOCALAPPDATA%\Programs\LocalCloud`. A separate disk folder is also possible.
4. Keep the optional media tools selected for video thumbnails, metadata, preview proxies and physical MP3 tag writing. Select WebView2 installation if the runtime is missing.
5. Complete installation and open LocalCloud from the Start menu or desktop shortcut.

The installer runs for the current Windows user. Downloading a dependency or allowing a firewall rule may invoke a separate system prompt. Application installation and library selection are different operations: do not install application binaries inside your media library. The installer rejects storage overlap and updates an existing recognized installation through the checked updater rather than blindly overwriting it.

The release is not Authenticode-signed. Windows reputation checks may appear for a new executable. Verify that the file came from the author's release and compare its checksum; do not assume any similarly named download is official.

### Portable core

Extract the **entire** Core ZIP into a separate application folder, provide the WebView2 runtime and FFmpeg tools, then run `LocalCloud.exe`. Moving only the EXE is insufficient: the `server`, `updater` and runtime files must remain with it. The portable package does not create an uninstall entry or shortcuts.

## First launch and phone access

On first launch, choose a **library folder**, for example `D:\LocalCloudLibrary`. You may enable launch with Windows and home-network access. LocalCloud creates standard `Photos`, `Videos`, `Music` and `Files` folders and stores its index under `.localcloud`.

For a phone:

1. Connect the computer and phone to the same trusted network. Avoid an isolated guest Wi-Fi network.
2. Enable home-network access in LocalCloud.
3. Use the QR code or one of the displayed addresses, such as `http://192.168.1.20:43110`, in the phone's browser. `localhost` on the phone means the phone, not your PC.
4. If access is blocked, open **Tools / Инструменты → Network diagnostics** and review the actual listener, addresses and Windows firewall/profile information.
5. Allow LocalCloud on the private home network through the supplied firewall action when appropriate.

The default port is **43110**. Numeric IP/QR access works independently of best-effort mDNS discovery. A connectivity probe made by the PC does not establish that a particular iPhone can reach the server.

On iPhone, you can use Safari's **Add to Home Screen**. Keep Safari visible during long uploads; iOS can suspend background tabs and locked-screen transfers. This is browser access, not a native iOS application or automatic Apple Photos synchronization.

## Features

### Uploads and files

- Resumable tus uploads with 8 MB chunks, configurable parallelism, queue, pause/resume, retries and cancellation.
- Confirmed offsets are retained for interrupted uploads. After reopening a tab, select the same file and destination to continue; do not choose a different file with the same name.
- SHA-256 duplicate detection with choices to skip or keep a separate copy.
- Original names are retained; collisions receive a readable suffix.
- Physical nested folders, create/rename/move/copy operations, multi-selection and desktop context menus.
- An upload history entry is directly accessible from the desktop navigation.
- External filesystem changes are picked up by an incremental watcher, with a reconciliation scan on startup, when necessary, periodically and on explicit request.

### Photos and Live Photos

- Gallery/list views, lazy thumbnail loading, search, date/extension/size filters, sorting and virtualized rows.
- JPEG, PNG, WebP, GIF, BMP, TIFF, HEIC/HEIF and AVIF previews, subject to available decoders and file validity.
- Zoom, pan, swipe, keyboard navigation and metadata view.
- Nondestructive rotation, cropping and exposure adjustments; export a separate JPEG rather than rewriting the original.
- Live Photo pairing using available Apple content identifiers, with a clearly identified same-folder/same-basename fallback. Pair-aware operations retain both members.

### Video

- Original playback using HTTP byte ranges, so seeking does not require downloading a whole video first.
- FFmpeg thumbnails and FFprobe metadata.
- A separately generated H.264/AAC preview proxy when requested; originals are not automatically transcoded.
- Actual codec playback depends on the client browser. A file being indexed does not mean every browser can decode it.

### Music

- A persistent player across library sections, compact mode, queue editing, shuffle and repeat modes.
- Device-local saved queue, current track, position, volume, mute and display preferences. Restoring a session does **not autoplay**.
- Search by title, artist and album; corresponding sort options and a missing title/artist filter.
- Media Session integration where supported by the browser/system.
- Keyboard: `Space` play/pause, `Alt + Left/Right` previous/next, `M` mute. Typing in controls/dialogs does not activate these shortcuts.
- Library labels can be saved independently of a file's embedded metadata.
- Actual MP3 tag writing requires a preview token and explicit confirmation, preserves a verified original backup, verifies the audio stream, and refuses to apply a stale preview to a changed file.

Browser restrictions still apply: iOS may route volume control through the device rather than the page, and autoplay/media-session behavior varies across clients.

### Documents and organization

- Local PDF.js rendering, pages and zoom.
- For eligible unprotected PDFs up to 64 MB: rotate/remove pages and add new text annotations, saving a separate copy. Existing PDF text is not edited in place.
- Virtual albums, favorites, recent items and saved filters/smart albums.
- Batch rename with preview and explicit organization of files directly inside the standard folders. Custom nested folders are retained.
- Trash, collision-aware restore and confirmed permanent deletion. Empty folders are not separate trash records.
- Manual verified copying to another local/UNC directory, retaining previous versions and a consistent metadata snapshot.
- Revocable, expiring folder-scoped share links; optional Radmin access uses the limited sharing API rather than unrestricted owner administration.

### Desktop and diagnostics

- Windows desktop shell using WebView2, browser access and tray controls for open/start/stop/restart/exit.
- Direct desktop navigation for **Music, Downloads and Tools**.
- Light/dark/system appearance, responsive phone/tablet/desktop layouts and local font/PDF assets.
- Server memory/CPU and media-process information, current background work and pause/resume.
- Lower background activity: debounced incremental changes, constrained media work and fewer gallery rerenders during music playback.

## Storage and data ownership

Keep these paths separate:

| Path | Contents |
| --- | --- |
| `%LOCALAPPDATA%\Programs\LocalCloud` or your chosen install folder | Program, local web assets and runtime dependencies. |
| `D:\LocalCloudLibrary` or your chosen library | Original physical files in normal folders. |
| `<library>\.localcloud\database.sqlite` | Index, albums, favorites and other application metadata. |
| `<library>\.localcloud\cache` | Rebuildable previews and proxies. |
| `<library>\.localcloud\uploads` | Incomplete resumable transfers. |
| `<library>\.localcloud\trash` | Trashed originals until restoration or permanent deletion. |
| `<library>\.localcloud\backups\audio-tags` | Original MP3 backups created before physical tag writes. |
| `%LOCALAPPDATA%\LocalCloud\settings.json` | Selected library and application configuration. |
| `%LOCALAPPDATA%\LocalCloud\language.txt` | Installer/native first-run language preference. |

Original file bytes are not stored in SQLite. Deleting the database loses metadata such as virtual albums and favorites even if physical files remain. Rebuilding a cache is different from deleting the database. A single local disk is not a backup.

## Updating an existing installation

### Development 1.4.0 → public 1.0.0

Use the **new** public Setup or public Update ZIP. Choose the **existing application folder**, not the library. The old development updater cannot recognize this numbering transition.

For the Update ZIP:

1. Finish important active transfers and extract Update into a temporary folder **outside** the application and library.
2. Run `Update-LocalCloud.cmd` and select the existing application folder containing `LocalCloud.exe` and `server`.
3. Read the detected paths and confirmation, then allow the checked update.
4. Open the installed LocalCloud after completion. The temporary Update folder may then be removed.

The updater validates all changed and retained files, checks disk space and path separation, waits for active work, stops only owned processes, and backs up application files, settings and SQLite. The library originals, file IDs, albums, favorites, trash and resumable upload state are retained. An application backup is not a complete copy of all media.

The exceptional numbering transition is limited to development **1.4.0**, public **1.0.0** and **schema 4**. A newer database or an already-public newer version cannot use this exception to downgrade. Future public releases continue as 1.0.1, 1.1.0 and so on.

You may keep using development 1.4.0 privately; moving to public 1.0.0 adds release packaging, language support for installation, publication documentation and the new release identity rather than replacing your library.

## Backup, recovery and uninstall

Back up the whole library and configuration to another disk if the files matter. Use the verified manual backup tool for a second location; it does not provide an automatic schedule.

Update snapshots are kept under `<install>\_update_backup\<timestamp>`. Read [UPDATE-USER.txt](UPDATE-USER.txt) before rollback. Compatible application rollback retains current metadata. Explicit database restore returns metadata to the backup time and requires separate confirmation; it does not remove original media.

For a Setup installation, use Windows **Installed apps → LocalCloud → Uninstall**. Exit LocalCloud through the tray first. Uninstall removes known application files and this installation's shortcuts/registration; it preserves the library, configuration, update backups and unknown files. A nonempty application directory can remain for that reason. For a portable installation, close the app and remove only the application directory after checking it contains no personal library.

## Security and privacy

LocalCloud is intended for a trusted private network. It is not a hardened public internet storage service. Do not expose its HTTP port through router forwarding or a public reverse proxy without a separate deployment/security design.

- Optional PIN protection uses a derived hash and HttpOnly sessions with attempt limits.
- Path boundaries, symlink/junction rejection for original access and Host/Origin/LAN checks are built into the server.
- Sensitive owner actions are restricted; a folder share link is scoped to its selected folder.
- Share links are bearer links: anyone with an eligible link and network access may use it until revoked/expired.
- Disabling download removes original-file endpoints where applicable; it cannot prevent copying media that a browser can play.
- HTTP on LAN does not encrypt the connection. A PIN is not TLS.
- Local logs, SQLite metadata and original EXIF/GPS may contain private information. Do not attach them publicly without review.
- No telemetry, AI model or hosted authentication account is required by the application. Setup's selected dependency downloads are explicit external network requests.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Phone cannot open the page | Server running, same LAN, correct IP/port, no guest isolation, LAN enabled, private Windows profile and firewall rule. |
| Desktop window does not open | Install/repair WebView2; try the browser address while investigating. |
| Missing video thumbnails or MP3 tag writing | Ensure both FFmpeg and FFprobe exist in `server/tools` or on PATH, then restart LocalCloud. |
| Dependency download fails | Retry with internet access; Setup offers core-only continuation. Install dependencies manually from their official providers if needed. |
| Upload pauses around a chunk boundary | Keep the tab visible, inspect transfer state/network, pause/resume and reselect the same original after reopening. See logs for persistent failures. |
| Duplicate reported | SHA-256 matches an existing original; choose skip or a separate copy. |
| Video indexed but not playable | Browser codec support may be missing; request a preview proxy. |
| Update refuses a path | Select the actual application directory; separate it from library/storage and extract Update elsewhere. |
| Update rejects a retained component | The installation differs from the expected baseline; use public Setup for a checked full repair/update. |
| Missing albums after manually deleting SQLite | Albums are metadata, not folders; recover from a database backup. |

Logs are under `<library>\.localcloud\logs`. Review [docs/QA-public-1.0.md](docs/QA-public-1.0.md) for the exact validation boundary. When reporting a bug, include version, Windows/browser versions and reproduction steps; never post a PIN, share token, personal photo, raw database or full private path unnecessarily.

## Known limitations

- Setup is cross-compiled on Linux. Its Windows UI, UAC, Start menu, uninstall and the physical Windows tray need a real-machine smoke test before you advertise them as tested on your PC.
- iPhone/Safari, real network throughput and tens-of-gigabytes transfers were not physically verified in the build environment.
- No native iOS client, automatic camera-roll backup, direct USB/PTP/WPD importer, family accounts or trusted-device system.
- Connecting a USB cable does not combine USB and Wi-Fi bandwidth. Windows may import files separately, or expose a supported tethering interface; LocalCloud uses the actual network route.
- LAN HTTP is not a secure context. Full service-worker offline PWA features require HTTPS or localhost; a Home Screen shortcut does not change that.
- Live Photo pairing and metadata extraction depend on real source formats. HEVC playback and volume handling depend on the client.
- Mounted external drives and UNC locations must remain accessible. Avoid disconnecting storage during active work.
- Automatic internet update checking/downloading is not provided; release packages are applied locally.
- Full English translation of the web UI is a future change, not a feature promised by this release.

## Building from source

For developers: .NET **SDK 10**, Node.js **22.12+ or 24**, npm, Python 3 and **NSIS 3** for the installer. The release binaries do not need Node, Python or the SDK.

From the repository root on Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
```

Put `makensis.exe` on PATH or supply `-NsisPath`. `package.ps1` publishes self-contained Windows x64 application/server/updater components, assembles the core and checked update package, and builds the multilingual Setup. The public packaging path deliberately excludes FFmpeg binaries; Setup obtains them directly upstream when selected.

Artifacts appear in `artifacts/releases`. Do not publish the `artifacts` directory as repository source. For development with hot reload, use `scripts/dev.ps1`. Linux server development/build is available through `scripts/build.sh`; the distributed desktop/installer target remains Windows x64.

Environment overrides for isolated development: `LOCALCLOUD_CONFIG`, `LOCALCLOUD_STORAGE`, `LOCALCLOUD_PORT`. These change real storage selection: point them to a dedicated test directory. Test scripts accept `LOCALCLOUD_DOTNET`; browser tests can accept `LOCALCLOUD_CHROMIUM`.

The lockfile fixes frontend dependency versions. The runtime version is pinned during release publishing. See [docs/BUILD.md](docs/BUILD.md) for package inputs and additional verification.

## Architecture and tests

| Directory | Responsibility |
| --- | --- |
| `src/LocalCloud.Core` | Settings models, filesystem boundaries and local process control contracts. |
| `src/LocalCloud.Infrastructure` | SQLite, indexing, media processing, file operations, sharing, backups and audio tags. |
| `src/LocalCloud.Server` | ASP.NET Core APIs, tus, SignalR and local static asset hosting. |
| `src/LocalCloud.Web` | React/TypeScript/Vite browser interface. |
| `src/LocalCloud.Desktop` | WinForms/WebView2 host, first-run settings and tray lifecycle. |
| `src/LocalCloud.Updater` | Hash-checked application updates, snapshots and compatible rollback. |
| `installer` | NSIS English/Russian installer and dependency/preflight helpers. |
| `tests` | Isolated core, API, UI and upgrade checks. |

The frontend is compiled into the server's `wwwroot`. SQLite uses WAL and additive migrations. No reset/reseed operation is part of release update. Watcher events are debounced; background media work is bounded and can be paused.

Automated checks cover original-byte preservation, filesystem safety, database behavior, upload finalization/resume, sharing boundaries, player persistence, responsive layouts and upgrade behavior. These are distinct from a native Windows/iPhone smoke test. Detailed results and required release smoke steps are documented in [QA](docs/QA-public-1.0.md).

## Publishing and reuse

The source ZIP is ready to extract and commit. Read [docs/PUBLISHING.md](docs/PUBLISHING.md) for the exact GitHub steps and [docs/RELEASE-NOTES-1.0.md](docs/RELEASE-NOTES-1.0.md) for ready release text. No GitHub username, contact address or published repository URL is invented in this archive.

The author may publish the official source and binaries. Other users may personally use/study/privately modify them. Unchanged or lightly changed republishing is prohibited by the first-party license, with the platform fork exception and a defined permission for substantially transformed noncommercial derivatives. Such a derivative must retain credit and link to the **actual original repository**. Third-party licenses retain their own permissions.

Public GitHub repositories can be viewed and forked under GitHub's platform rules. AI assistance is openly disclosed. Neither that disclosure nor the first-party copyright notice claims ownership of .NET, FFmpeg, React or other independent components. Read [LICENSE](LICENSE), [NOTICE.md](NOTICE.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
