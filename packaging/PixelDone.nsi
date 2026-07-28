Unicode True
RequestExecutionLevel user
ManifestDPIAware True

!define PRODUCT_NAME "PixelDone"
!define PRODUCT_VERSION "4.0.0-beta.1"
!define PRODUCT_PUBLISHER "Miles Xue"
!define LEGACY_INSTALL_DIR "$LOCALAPPDATA\PixelDone"
!define PRODUCT_DATA_DIR "$LOCALAPPDATA\com.milesxue.pixeldone.windows"

!ifndef APP_SOURCE
  !define APP_SOURCE "..\artifacts\publish\win-x64"
!endif

!ifndef OUTPUT_DIR
  !define OUTPUT_DIR "..\artifacts\installer"
!endif

Name "${PRODUCT_NAME}"
OutFile "${OUTPUT_DIR}\PixelDone-${PRODUCT_VERSION}-win-x64-setup.exe"
InstallDir "$LOCALAPPDATA\Programs\PixelDone"
InstallDirRegKey HKCU "Software\PixelDone" "InstallDir"
Icon "${APP_SOURCE}\Assets\AppIcon.ico"
UninstallIcon "${APP_SOURCE}\Assets\AppIcon.ico"

Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

Function .onInit
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "CurrentBuildNumber"
  IntCmp $0 26200 supported unsupported supported
  unsupported:
    MessageBox MB_ICONSTOP|MB_OK "PixelDone requires Windows 11 25H2 (build 26200) or newer."
    Abort
  supported:

  ; PixelDone 4.0 is a product-version clean cut from the Tauri client. The cloud is
  ; authoritative, so the legacy program, cache, credentials, and SQLite files
  ; are not imported. Only perform this destructive cleanup when the legacy
  ; uninstaller proves that the old product is actually installed.
  IfFileExists "${LEGACY_INSTALL_DIR}\uninstall.exe" 0 legacy_done
    MessageBox MB_ICONINFORMATION|MB_OK \
      "PixelDone 4 replaces the legacy Windows client. Local legacy data will be removed; sign in after installation to restore cloud data." \
      /SD IDOK
    ExecWait '"${LEGACY_INSTALL_DIR}\uninstall.exe" /S'
    RMDir /r "${LEGACY_INSTALL_DIR}"
    RMDir /r "${PRODUCT_DATA_DIR}"
  legacy_done:

  ; Pre-release native prototypes used a versioned filename. No prototype data
  ; is retained, and released native databases never encode the product major.
  Delete "${PRODUCT_DATA_DIR}\data\pixeldone-v4.sqlite3"
  Delete "${PRODUCT_DATA_DIR}\data\pixeldone-v4.sqlite3-shm"
  Delete "${PRODUCT_DATA_DIR}\data\pixeldone-v4.sqlite3-wal"
FunctionEnd

Section "PixelDone" SecMain
  SetOutPath "$INSTDIR"
  File /r "${APP_SOURCE}\*.*"
  WriteRegStr HKCU "Software\PixelDone" "InstallDir" "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\PixelDone"
  CreateShortcut "$SMPROGRAMS\PixelDone\PixelDone.lnk" "$INSTDIR\PixelDone.exe" "" "$INSTDIR\Assets\AppIcon.ico"
  CreateShortcut "$DESKTOP\PixelDone.lnk" "$INSTDIR\PixelDone.exe" "" "$INSTDIR\Assets\AppIcon.ico"
  nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Create /TN "PixelDone Reminders" /TR "\"$INSTDIR\PixelDone.exe\" --notify-due" /SC MINUTE /MO 1 /F'
SectionEnd

Section "Uninstall"
  nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Delete /TN "PixelDone Reminders" /F'
  Delete "$DESKTOP\PixelDone.lnk"
  Delete "$SMPROGRAMS\PixelDone\PixelDone.lnk"
  RMDir "$SMPROGRAMS\PixelDone"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "Software\PixelDone"
SectionEnd
