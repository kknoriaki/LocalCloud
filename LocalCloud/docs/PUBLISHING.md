# Publishing the official LocalCloud release / Публикация официального релиза

This guide is for the original author. Third-party republishing is subject to LICENSE.
Инструкция для оригинального автора. Чужая перепубликация ограничена LICENSE.

## 1. Repository / Репозиторий

Extract `LocalCloud-1.0.0-Source.zip`. Open the enclosed **LocalCloud** folder.
Its README.md, README.ru.md, LICENSE, src, installer and scripts belong at the
repository root. Do not upload Setup, a local media library, settings.json,
SQLite, logs, node_modules, tools or artifacts as source. `.gitignore` excludes
common local/build data. All sample screenshots supplied here use generated
fixtures rather than the author's personal library.

Распакуйте Source и откройте вложенную папку **LocalCloud**. Её содержимое —
корень репозитория. Не загружайте папку целиком вторым вложенным уровнем.
Setup и готовые ZIP размещаются в Releases, а не в истории исходников.

Create an empty GitHub repository under your actual account. Do not select an
MIT/GPL template: it would conflict with the intended custom first-party terms.
Set your real Git identity locally, then run:

```bash
git init
git branch -M main
git add .
git commit -m "LocalCloud 1.0.0 public release"
git remote add origin YOUR_ACTUAL_REPOSITORY_URL
git push -u origin main
git tag -a v1.0.0 -m "LocalCloud 1.0.0"
git push origin v1.0.0
```

Replace `YOUR_ACTUAL_REPOSITORY_URL` with the URL GitHub gives you. It is a
placeholder in this instruction, not a baked-in URL. You may also use GitHub
Desktop. Review the staged files before committing. Keep the copyright author
and AI-development disclosure. Once the repo exists, you may add its actual URL
to NOTICE/README credits; no functionality depends on that URL.

Создайте пустой репозиторий GitHub без шаблонной MIT/GPL лицензии. Подставьте
свою настоящую ссылку; можно пользоваться GitHub Desktop. Перед commit
проверьте список файлов. После создания можно добавить реальный адрес в
уведомление об авторстве и README.

## 2. Release / Релиз

Open **Releases → Draft a new release**, select tag **v1.0.0**, set title
**LocalCloud 1.0.0**, and paste the text from `docs/RELEASE-NOTES-1.0.md`.
Attach these supplied assets:

- `LocalCloud-1.0.0-Setup-x64.exe`
- `LocalCloud-1.0.0-Windows-x64-Core.zip`
- `LocalCloud-1.0.0-Windows-x64-Update.zip`
- `LocalCloud-1.0.0-Source.zip`
- `SHA256SUMS-1.0.0.txt`

Run the Windows smoke steps in QA before claiming they passed. Publish the
release when satisfied. These are already-built assets; users should not need
to build the application. The ZIP checksums and documentation are provided.

В Releases создайте релиз по тегу **v1.0.0**, вставьте готовый двуязычный текст,
прикрепите пять файлов выше. Проверьте Setup на своём Windows и подключение
телефона. Это готовые сборки: пользователям не требуется собирать код.

## 3. GitHub Actions / Автоматическая сборка

`.github/workflows/windows-release.yml` provides a manual Windows build via
**Actions → Build Windows release → Run workflow**. It runs the build and core
checks, builds Setup with NSIS and uploads a release bundle as a workflow
artifact. It does not automatically create a public release or require a stored
personal access token. A downloaded workflow artifact contains the actual
release files; extract that outer artifact ZIP before attaching its contents.

Есть ручная Windows-сборка в Actions. Она сохраняет готовый набор как artifact,
но сама не публикует релиз. Личный токен хранить в репозитории не нужно.

## 4. Future versions / Следующие версии

Change VERSION, Directory.Build.props, API/control/tray/UI/package version
strings together. Update release notes, rebuild, test against the real previous
release, and publish a matching tag and checksum file. Keep schemaVersion 4
unless an intentional additive database migration is needed. Never simply
replace a production database with a test fixture.

Публичные номера идут 1.0.1, 1.1.0 и далее. Нумерация разработки 1.4.0 не
продолжается в публичной ветке. Меняйте все версии согласованно, тестируйте
реальное обновление и не заменяйте пользовательскую базу тестовой.

## 5. Attribution and public forks / Авторство и форки

Public GitHub repositories can be viewed and forked under GitHub's platform
terms. The custom LICENSE prevents unchanged/lightly modified independent
redistribution to the extent enforceable and permits a defined category of
substantially transformed, credited noncommercial derivatives. It does not
remove third-party license rights or guarantee copyright protection for every
AI-produced fragment. Do not call the project unrestricted "open source".

Публичный GitHub допускает форки. Наша лицензия регулирует самостоятельные
публикации копий и существенно переработанных проектов, но не отменяет правила
платформы, лицензии зависимостей и законодательство. Помощь ИИ не скрывайте.

References: [GitHub licensing](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository),
[GitHub releases](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository),
[FFmpeg licensing](https://ffmpeg.org/legal.html).
