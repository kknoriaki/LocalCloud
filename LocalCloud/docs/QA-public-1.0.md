# LocalCloud public 1.0.0 — validation

Public release based on development 1.4.0; SQLite schema 4.

| Check | Result | Boundary |
| --- | --- | --- |
| TypeScript/Vite frontend | Passed | Production web assets built. Nonfatal upstream annotation/chunk-size messages remain. |
| .NET solution | Passed, 0 errors/0 warnings | Linux-hosted compilation, including Windows targeting. |
| Core + numbering guards | 53 assertions passed | Isolated real filesystem/SQLite, not production user data. |
| API/media | 18 scenarios passed | Real MP3 tags/cover, audio SHA, byte-exact backup, ranges, stale preview refusal, search/filter, watcher, diagnostics. |
| Browser | 10 scenarios passed, no JS errors | Chromium player persistence, shortcuts, queue, preview, tools, uploads history and widths 375/390/430/768/1024/1440/1920. |
| Actual 1.4.0 → public 1.0.0 | 25 scenarios passed | Real old server and same updater engine on Linux: media bytes, IDs, albums, trash, custom metadata, settings, interrupted tus resume, new uploads, backup, rollback, retained-file hashes and public-downgrade refusal. |
| Windows x64 publishes | Passed | Self-contained .NET 10.0.12 server/desktop/updater; desktop first-run RU/EN compiled. |
| NSIS Setup | Compiled successfully | Unicode setup/uninstaller with English/Russian language tables; not executed on Windows. |
| Release archive/hash preflight | Recorded in package-public10-result.json | ZIP CRC, file length/SHA, previous baseline, source exclusions and precise uninstall list. |

Machine-readable API/UI/upgrade results accompany this source archive.
Screenshots in screenshots/1.0 use original procedural test fixtures.
The public fixture generator is tests/generate-public10-assets.py. A fixed-startup
sleep in the browser harness was replaced with observed fixture/index readiness
after an initial timing failure during concurrent checks; the final fixture run
is sequential and does not bypass playback or simulate successful results.

## Native smoke check still required

The build environment is Linux. It cannot execute Windows Setup/tray/UAC,
Microsoft's runtime bootstrapper or a physical iPhone. These are limitations
of validation, not claims that such native tests were performed. Before declaring
your public release tested on Windows, run this small real-machine check:

1. Start Setup, choose Russian, inspect license/component/destination pages.
2. Install into a separate empty app folder. Confirm Start menu/desktop shortcuts
   and Installed apps entry. Check the app version reads 1.0.0.
3. Repeat the first-run language check with English in a disposable Windows
   profile; the web UI itself is still Russian.
4. Check first launch selects a separate library and rejects an overlapping
   application/library path. Test stop/restart/exit and reopen from tray.
5. On a disposable installation, test a missing dependency, refused/missing
   internet, and optional core-only continuation. Existing media tools must stay.
6. Apply the public Update to the actual development 1.4.0 app directory.
   Confirm originals/albums/favorites/trash and an interrupted upload remain.
7. Open the LAN IP on the actual iPhone, upload a small file, play music/video
   and check Safari's suspension/resume behavior. PC probes alone are not proof.
8. Exit the app; uninstall the disposable Setup installation. Confirm media,
   settings, update snapshots and an unknown file in the app folder remain.
9. Check an update snapshot can perform compatible application-only rollback.

## Русский

Прошли сборка, 53 проверки ядра/границ смены номера, 18 API-сценариев,
10 браузерных сценариев и 25 сценариев реального перехода с 1.4.0.
Setup с RU/EN скомпилирован. Схема базы — 4.

Нативный установщик, трей, UAC, ярлыки, удаление и настоящий iPhone здесь
физически не запускались. Проверьте их на Windows перед заявлением о такой
проверке. Браузерные размеры и серверные сетевые проверки не заменяют iPhone.
