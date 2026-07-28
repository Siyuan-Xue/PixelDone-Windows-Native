Unicode True
RequestExecutionLevel user
ManifestDPIAware True

!define PRODUCT_NAME "PixelDone"
!define PRODUCT_VERSION "4.0.0-beta.1"
!define PRODUCT_PUBLISHER "Miles Xue"

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
FunctionEnd

Section "PixelDone" SecMain
  SetOutPath "$INSTDIR"
  File /r "${APP_SOURCE}\*.*"
  WriteRegStr HKCU "Software\PixelDone" "InstallDir" "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\PixelDone"
  CreateShortcut "$SMPROGRAMS\PixelDone\PixelDone.lnk" "$INSTDIR\PixelDone.exe" "" "$INSTDIR\Assets\AppIcon.ico"
  CreateShortcut "$DESKTOP\PixelDone.lnk" "$INSTDIR\PixelDone.exe" "" "$INSTDIR\Assets\AppIcon.ico"
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\PixelDone.lnk"
  Delete "$SMPROGRAMS\PixelDone\PixelDone.lnk"
  RMDir "$SMPROGRAMS\PixelDone"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "Software\PixelDone"
SectionEnd
