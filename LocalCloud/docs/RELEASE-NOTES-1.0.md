# LocalCloud 1.0.0 — first public release / первый публичный релиз

## English

LocalCloud turns a Windows PC into a local personal library for photos, videos,
music and documents, accessible through a desktop app and browsers on your home
network. Originals stay in normal folders. This public release contains the
features of development 1.4.0, adds an English/Russian installer and native
first-run configuration, a checked numbering transition, and publication-ready
documentation. Database schema remains 4.

Download **LocalCloud-1.0.0-Setup-x64.exe** for installation. The app and .NET runtime
are included; optional FFmpeg is downloaded directly upstream with a pinned
checksum, and missing WebView2 can be installed from Microsoft. Existing users
should select their current application folder or use the separate Update ZIP.
Source code is provided under the custom Source-Available License; AI development
assistance is disclosed. Read LICENSE and THIRD_PARTY_NOTICES.md.

Validation includes cross-platform application/API/UI/upgrade tests and Windows
x64 publishing. Native Windows installer/tray and a physical iPhone were not
executed in the build environment. Full English web-interface localization is
not included. No automatic internet update service or AI integration is included.

## Русский

LocalCloud — личная локальная библиотека фото, видео, музыки и документов на
компьютере Windows, с окном приложения и доступом через браузер в домашней сети.
Оригиналы остаются в обычных папках. Публичная 1.0.0 сохраняет возможности
разработки 1.4.0 и добавляет RU/EN установку, двуязычное первое окно, проверяемый
переход нумерации и документацию для публикации. Схема базы — 4.

Для установки: **LocalCloud-1.0.0-Setup-x64.exe**. Программа и .NET включены;
опциональный FFmpeg загружается напрямую у поставщика с проверкой SHA-256,
отсутствующий WebView2 — у Microsoft. Для обновления выберите существующую
папку программы либо отдельный Update ZIP. Исходники — по собственной
source-available лицензии; помощь ИИ в разработке указана открыто.

Автоматические проверки и Windows x64 сборка выполнены. Нативный Setup/трей
Windows и физический iPhone в среде сборки не запускались. Веб-интерфейс пока
на русском. Нет автоматической интернет-загрузки обновлений и интеграции ИИ.
