Unicode true
!include MUI2.nsh
!include LogicLib.nsh
!include x64.nsh
!include Sections.nsh
!ifndef BUILD_DIR
  !define BUILD_DIR "../artifacts/win-x64"
!endif
!ifndef RELEASE_DIR
  !define RELEASE_DIR "../artifacts/releases"
!endif
!ifndef VERSION
  !define VERSION "1.0.0"
!endif
Name "LocalCloud ${VERSION}"
OutFile "${RELEASE_DIR}/LocalCloud-${VERSION}-Setup-x64.exe"
InstallDir "$LOCALAPPDATA\Programs\LocalCloud"
InstallDirRegKey HKCU "Software\LocalCloud" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "LocalCloud"
VIAddVersionKey /LANG=1033 "FileDescription" "LocalCloud Setup / English and Russian"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright 2026 Ilya Gavrilov"
!define MUI_ICON "../src/LocalCloud.Desktop/LocalCloud.ico"
!define MUI_UNICON "../src/LocalCloud.Desktop/LocalCloud.ico"
!define MUI_ABORTWARNING
!define MUI_LANGDLL_ALWAYSSHOW
!define MUI_LANGDLL_REGISTRY_ROOT HKCU
!define MUI_LANGDLL_REGISTRY_KEY "Software\LocalCloud"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"
!define MUI_WELCOMEPAGE_TITLE "LocalCloud ${VERSION}"
!define MUI_WELCOMEPAGE_TEXT "$(Welcome)"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "$(LicenseText)"
!insertmacro MUI_PAGE_COMPONENTS
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE ValidateDirectory
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\LocalCloud.exe"
!define MUI_FINISHPAGE_RUN_TEXT "$(LaunchApp)"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Russian"
LicenseLangString LicenseText ${LANG_ENGLISH} "../LICENSE"
LicenseLangString LicenseText ${LANG_RUSSIAN} "../LICENSE.ru.md"
LangString Welcome ${LANG_ENGLISH} "Your local photo, video, music and file library.$\r$\n$\r$\nChoose a separate application folder. Your media library is selected at first launch.$\r$\n$\r$\nSetup can download FFmpeg directly from its provider and WebView2 from Microsoft when selected. The web interface is currently Russian."
LangString Welcome ${LANG_RUSSIAN} "Локальная библиотека фото, видео, музыки и документов.$\r$\n$\r$\nВыберите отдельную папку программы. Папка библиотеки выбирается при первом запуске.$\r$\n$\r$\nSetup может загрузить FFmpeg у поставщика и WebView2 у Microsoft при выборе компонентов. Веб-интерфейс пока на русском."
LangString LaunchApp ${LANG_ENGLISH} "Launch LocalCloud"
LangString LaunchApp ${LANG_RUSSIAN} "Запустить LocalCloud"
LangString AppSection ${LANG_ENGLISH} "LocalCloud application and .NET runtime"
LangString AppSection ${LANG_RUSSIAN} "Программа LocalCloud и среда .NET"
LangString MediaSection ${LANG_ENGLISH} "FFmpeg media tools (109 MB upstream download if absent)"
LangString MediaSection ${LANG_RUSSIAN} "FFmpeg: медиа-инструменты (109 МБ, если отсутствуют)"
LangString WebSection ${LANG_ENGLISH} "Install WebView2 from Microsoft if missing (internet)"
LangString WebSection ${LANG_RUSSIAN} "Установить WebView2 от Microsoft, если отсутствует (интернет)"
LangString ShortSection ${LANG_ENGLISH} "Desktop shortcut"
LangString ShortSection ${LANG_RUSSIAN} "Ярлык на рабочем столе"
LangString PathError ${LANG_ENGLISH} "Choose a separate valid application folder. Details:"
LangString PathError ${LANG_RUSSIAN} "Выберите отдельную подходящую папку программы. Подробности:"
LangString DependencyError ${LANG_ENGLISH} "An optional dependency could not be installed. Media features or the desktop window may be unavailable.$\r$\n$\r$\n$1$\r$\n$\r$\nContinue installing the core application?"
LangString DependencyError ${LANG_RUSSIAN} "Дополнительный компонент не установлен. Часть медиа-функций или окно приложения могут быть недоступны.$\r$\n$\r$\n$1$\r$\n$\r$\nПродолжить установку ядра приложения?"
LangString UpdateError ${LANG_ENGLISH} "The checked update failed. Existing data and any update snapshot were retained. Review the updater details below."
LangString UpdateError ${LANG_RUSSIAN} "Проверяемое обновление не завершено. Данные и созданный снимок сохранены. Посмотрите сообщение updater ниже."
LangString ExitBeforeUninstall ${LANG_ENGLISH} "Exit LocalCloud through the tray first. Library, settings and unknown files will be retained."
LangString ExitBeforeUninstall ${LANG_RUSSIAN} "Сначала завершите LocalCloud через трей. Библиотека, настройки и неизвестные файлы сохраняются."
Var UiLanguage

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "LocalCloud requires 64-bit Windows. / Нужна 64-битная Windows."
    Abort
  ${EndIf}
  SetShellVarContext current
  !insertmacro MUI_LANGDLL_DISPLAY
  InitPluginsDir
  File /oname=$PLUGINSDIR\Test-InstallPath.ps1 "Test-InstallPath.ps1"
  File /oname=$PLUGINSDIR\Install-Dependencies.ps1 "Install-Dependencies.ps1"
  File /oname=$PLUGINSDIR\Verify-Installed.ps1 "Verify-Installed.ps1"
  ${If} $LANGUAGE == ${LANG_RUSSIAN}
    StrCpy $UiLanguage "ru"
  ${Else}
    StrCpy $UiLanguage "en"
  ${EndIf}
FunctionEnd

Function ValidateDirectory
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\Test-InstallPath.ps1" -Destination "$INSTDIR"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "$(PathError)$\r$\n$1"
    Abort
  ${EndIf}
FunctionEnd

Section "$(AppSection)" SEC_APP
  SectionIn RO
  Call ValidateDirectory
  SetOutPath "$PLUGINSDIR\payload"
  File /r /x *.pdb "${BUILD_DIR}/*"
  ${If} ${SectionIsSelected} 1
    nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\Install-Dependencies.ps1" -Destination "$INSTDIR" -Component Media -Language "$UiLanguage"'
    Pop $0
    Pop $1
    ${If} $0 != 0
      MessageBox MB_ICONEXCLAMATION|MB_YESNO "$(DependencyError)" IDYES +2
      Abort
    ${EndIf}
  ${EndIf}
  ${If} ${SectionIsSelected} 2
    nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\Install-Dependencies.ps1" -Destination "$INSTDIR" -Component WebView2 -Language "$UiLanguage"'
    Pop $0
    Pop $1
    ${If} $0 != 0
      MessageBox MB_ICONEXCLAMATION|MB_YESNO "$(DependencyError)" IDYES +2
      Abort
    ${EndIf}
  ${EndIf}
  ${If} ${FileExists} "$INSTDIR\LocalCloud.exe"
    nsExec::ExecToLog '"$PLUGINSDIR\payload\updater\LocalCloud.Updater.exe" --install "$INSTDIR" --package "$PLUGINSDIR\payload" --non-interactive'
    Pop $0
    ${If} $0 != 0
      MessageBox MB_ICONSTOP "$(UpdateError)"
      Abort
    ${EndIf}
  ${Else}
    !include "InstallFiles.nsh"
    nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\Verify-Installed.ps1" -Destination "$INSTDIR" -ExpectedVersion "${VERSION}"'
    Pop $0
    Pop $1
    ${If} $0 != 0
      MessageBox MB_ICONSTOP "$1"
      Abort
    ${EndIf}
  ${EndIf}
  CreateDirectory "$LOCALAPPDATA\LocalCloud"
  FileOpen $0 "$LOCALAPPDATA\LocalCloud\language.txt" w
  FileWrite $0 "$UiLanguage"
  FileClose $0
  WriteRegStr HKCU "Software\LocalCloud" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\LocalCloud" "InstallerLanguage" "$LANGUAGE"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalCloud" "DisplayName" "LocalCloud"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalCloud" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalCloud" "Publisher" "Ilya Gavrilov"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalCloud" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalCloud" "DisplayIcon" "$INSTDIR\LocalCloud.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalCloud" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalCloud" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalCloud" "NoRepair" 1
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\LocalCloud"
  CreateShortcut "$SMPROGRAMS\LocalCloud\LocalCloud.lnk" "$INSTDIR\LocalCloud.exe"
  CreateShortcut "$SMPROGRAMS\LocalCloud\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd
Section "$(MediaSection)" SEC_MEDIA
SectionEnd
Section "$(WebSection)" SEC_WEB
SectionEnd
Section "$(ShortSection)" SEC_SHORT
  CreateShortcut "$DESKTOP\LocalCloud.lnk" "$INSTDIR\LocalCloud.exe"
SectionEnd

Function un.onInit
  SetShellVarContext current
  !insertmacro MUI_UNGETLANGUAGE
  InitPluginsDir
  File /oname=$PLUGINSDIR\Test-Uninstall.ps1 "Test-Uninstall.ps1"
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\Test-Uninstall.ps1" -Destination "$INSTDIR"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "$(ExitBeforeUninstall)$\r$\n$1"
    Abort
  ${EndIf}
FunctionEnd
Section "Uninstall"
  !include "UninstallFiles.nsh"
  Delete "$INSTDIR\server\tools\ffmpeg.exe"
  Delete "$INSTDIR\server\tools\ffprobe.exe"
  Delete "$INSTDIR\server\tools\LICENSE"
  Delete "$INSTDIR\server\tools\README.txt"
  RMDir "$INSTDIR\server\tools"
  RMDir "$INSTDIR\server"
  Delete "$INSTDIR\Uninstall.exe"
  ReadRegStr $0 HKCU "Software\LocalCloud" "InstallDir"
  ${If} $0 == $INSTDIR
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalCloud"
    DeleteRegKey HKCU "Software\LocalCloud"
    Delete "$DESKTOP\LocalCloud.lnk"
    Delete "$SMPROGRAMS\LocalCloud\LocalCloud.lnk"
    Delete "$SMPROGRAMS\LocalCloud\Uninstall.lnk"
    RMDir "$SMPROGRAMS\LocalCloud"
    ReadRegStr $1 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "LocalCloud"
    ${If} $1 == '$\"$INSTDIR\LocalCloud.exe$\" --background'
      DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "LocalCloud"
    ${EndIf}
  ${EndIf}
  ; Deliberately non-recursive: user library, settings, update snapshots and unknown files stay.
  RMDir "$INSTDIR"
SectionEnd
