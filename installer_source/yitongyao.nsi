; Windows 安装器（NSIS MUI2）
Unicode True
SetCompressor lzma

!include "MUI2.nsh"
!include "WinVer.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "nsDialogs.nsh"

!define APPNAME "易通遥"
!define APPNAMEEN "YiTongYao"
!define APPVERSION "1.9.1.5"
!define PUBLISHER "yty16"
!define EXENAME "YiTongYao.exe"
!define APPDIR "$LOCALAPPDATA\Programs\${APPNAME}"
!define DATADIR "$LOCALAPPDATA\${APPNAMEEN}"
; PROJECTROOT 可由命令行传入
!ifndef PROJECTROOT
  !define PROJECTROOT "${__FILEDIR__}\.."
!endif

Name "${APPNAME} ${APPVERSION}"
OutFile "${PROJECTROOT}\installer\${APPNAME}-${APPVERSION}-win-x64-setup.exe"
InstallDir "${APPDIR}"
RequestExecutionLevel user

!define MUI_ABORTWARNING
!define MUI_ICON "${PROJECTROOT}\Windows\YiTongYao.Windows\Assets\vty_icon.ico"
!define MUI_UNICON "${PROJECTROOT}\Windows\YiTongYao.Windows\Assets\vty_icon.ico"

; ---------- Install pages ----------
!define MUI_WELCOMEPAGE_TITLE "欢迎使用 ${APPNAME} ${APPVERSION} 安装向导"
!define MUI_WELCOMEPAGE_TEXT "本向导将引导您完成 ${APPNAME} 的安装。$\r$\n$\r$\n${APPNAME} 是一款局域网远程控制工具：用手表或手机通过 WiFi、蓝牙或 Wi-Fi Direct 热点直连电脑，即可控制 PPT 放映，并执行锁屏、睡眠、关机、重启等操作（开机需主板支持 WoL 网络唤醒）。点击$\"下一步$\"继续。"
!insertmacro MUI_PAGE_WELCOME

!define MUI_LICENSEPAGE_RADIOBUTTONS
!define MUI_LICENSEPAGE_TEXT_TOP "请阅读以下软件许可协议与免责声明。您必须接受后才能继续安装。"
!insertmacro MUI_PAGE_LICENSE "${PROJECTROOT}\installer_source\license.txt"

!define MUI_COMPONENTSPAGE_TEXT_TOP "选择要安装的组件。带$\"必需$\"标记的组件不可取消。"
!define MUI_COMPONENTSPAGE_NODESC
!insertmacro MUI_PAGE_COMPONENTS

!define MUI_DIRECTORYPAGE_TEXT_TOP "选择 ${APPNAME} 的安装位置。安装需要约 250 MB 可用磁盘空间，且您对该目录需具备写入权限。"
!define MUI_DIRECTORYPAGE_TEXT_DESTINATION "安装文件夹:"
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE DirectoryLeave
!insertmacro MUI_PAGE_DIRECTORY

; 安装方式页（仅在检测到已有安装痕迹时显示）
Page custom InstallTypePage InstallTypeLeave

!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_TITLE "完成 ${APPNAME} 安装"
!define MUI_FINISHPAGE_TEXT "${APPNAME} 已成功安装到您的计算机。$\r$\n$\r$\n点击$\"完成$\"结束安装向导。"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXENAME}"
!define MUI_FINISHPAGE_RUN_TEXT "立即运行 ${APPNAME}"
!define MUI_FINISHPAGE_NOREBOOTSUPPORT
!insertmacro MUI_PAGE_FINISH

; ---------- Uninstall pages ----------
!insertmacro MUI_UNPAGE_CONFIRM
UninstPage custom un.OptionsPage un.OptionsPageLeave
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "SimpChinese"

; ---------- Variables (uninstall options) ----------
Var unOptConfig
Var unOptData
Var hChkConfig
Var hChkData

; ---------- Variables (install mode) ----------
Var hRadUpgrade
Var hRadFresh
Var isFreshInstall

; ---------- OS check ----------
Function .onInit
  ${Unless} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "本软件需要 Windows 10 或更高版本。当前操作系统不受支持，安装已终止。"
    Abort
  ${EndIf}
FunctionEnd

; ---------- Directory validation: space + writable ----------
Function DirectoryLeave
  ${GetRoot} $INSTDIR $0
  ${DriveSpace} $0 "/D=F /S=M" $1
  ${If} $1 < 250
    MessageBox MB_OK|MB_ICONEXCLAMATION "磁盘可用空间不足。安装需要约 250 MB 可用空间，当前驱动器 $0 仅剩约 $1 MB。请清理空间或选择其他位置。"
    Abort
  ${EndIf}
  CreateDirectory "$INSTDIR"
  FileOpen $2 "$INSTDIR\_writetest.tmp" w
  ${If} $2 == ""
    MessageBox MB_OK|MB_ICONEXCLAMATION "无法写入安装目录 $INSTDIR。请选择其他路径，或以具有该目录写入权限的账户运行安装程序。"
    Abort
  ${Else}
    FileWrite $2 "test"
    FileClose $2
    Delete "$INSTDIR\_writetest.tmp"
  ${EndIf}
FunctionEnd

; ---------- Install mode page (标准安装 / 全新安装) ----------
Function InstallTypePage
  ReadRegStr $R0 "HKCU" "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName"
  IfFileExists "${DATADIR}\settings.json" install_mode_show
  ${If} $R0 == ""
    Abort
  ${EndIf}
install_mode_show:

  nsDialogs::Create /SIZE 330x150 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}

  ${NSD_CreateLabel} 0 0 100% 16u "检测到本机已有 ${APPNAME} 的安装，请选择安装方式："
  Pop $0

  ${NSD_CreateRadioButton} 0 24u 100% 18u "标准安装（推荐）：保留已有的设置（端口、配对码等），仅更新程序文件"
  Pop $hRadUpgrade

  ${NSD_CreateRadioButton} 0 48u 100% 18u "全新安装：清除已有的设置与日志，从零开始配置"
  Pop $hRadFresh
  ${NSD_Check} $hRadUpgrade

  ${NSD_CreateLabel} 0 74u 100% 40u "全新安装将删除 ${DATADIR} 下的设置文件与运行日志，端口与配对码将恢复默认值（此操作不可恢复）。"
  Pop $0

  nsDialogs::Show
FunctionEnd

Function InstallTypeLeave
  StrCpy $isFreshInstall 0
  SendMessage $hRadFresh 0x00F2 0 0 $0
  ${If} $0 == 1
    MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "全新安装将清除本机已有的 ${APPNAME} 设置与日志，该操作不可恢复。确定继续吗？" /SD IDCANCEL
    Pop $1
    ${If} $1 == IDCANCEL
      Abort
    ${EndIf}
    StrCpy $isFreshInstall 1
  ${EndIf}
FunctionEnd

; ---------- Install sections ----------
Section "主程序（必需）" SEC_MAIN
  SectionIn RO

  ; 结束正在运行的进程
  DetailPrint "正在关闭已运行的 ${APPNAME} 进程..."
  ExecWait 'taskkill /F /IM "${EXENAME}"' $0
  Sleep 2000

  SetOutPath "$INSTDIR"
  File /r /x "*.nsi" /x "*.pdb" "${PROJECTROOT}\dist\publish\*.*"
  File "${PROJECTROOT}\LICENSE"
  File "${PROJECTROOT}\LEGAL.md"
  File "${PROJECTROOT}\THIRD_PARTY_NOTICES.md"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ${If} $isFreshInstall == 1
    DetailPrint "正在清除旧的 ${APPNAME} 设置与日志..."
    RMDir /r "${DATADIR}"
  ${EndIf}

  ; 注册卸载信息
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayVersion" "${APPVERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayIcon" "$INSTDIR\${EXENAME}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoModify" "1"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoRepair" "1"

  ; 防火墙入站放行
  DetailPrint "正在配置防火墙入站规则…"
  ExecWait 'netsh advfirewall firewall add rule name="${APPNAME} 远程控制" dir=in action=allow enable=yes profile=any program="$INSTDIR\${EXENAME}"' $3
  ${If} $3 != 0
    DetailPrint "防火墙规则未能自动添加（退出码 $3）。可在程序主界面点「防火墙放行」按钮补做。"
  ${Else}
    DetailPrint "防火墙入站规则已添加。"
  ${EndIf}
SectionEnd

Section "创建桌面快捷方式" SEC_DESKTOP
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"
SectionEnd

Section "创建开始菜单快捷方式" SEC_STARTMENU
  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd

; ---------- Uninstall custom options page ----------
Function un.OptionsPage
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}

  ${NSD_CreateLabel} 0 0 100% 24u "请选择卸载时要执行的操作："
  Pop $0

  ${NSD_CreateCheckBox} 0 32u 100% 16u "删除配置信息（端口、配对码等设置）"
  Pop $hChkConfig
  ${NSD_Check} $hChkConfig

  ${NSD_CreateCheckBox} 0 56u 100% 16u "清空运行日志（不可恢复）"
  Pop $hChkData

  nsDialogs::Show
FunctionEnd

Function un.OptionsPageLeave
  ${NSD_GetState} $hChkConfig $unOptConfig
  ${NSD_GetState} $hChkData $unOptData
FunctionEnd

; ---------- Uninstall section ----------
Section "Uninstall"
  DetailPrint "正在关闭正在运行的 ${APPNAME}..."
  ExecWait 'taskkill /F /IM "${EXENAME}" /FI "STATUS eq RUNNING"' $R1
  Sleep 500

  ; 清除开机自启项
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${APPNAMEEN}"

  ; 清除防火墙放行规则
  ExecWait 'netsh advfirewall firewall delete rule name="${APPNAME} 远程控制"'

  Delete "$DESKTOP\${APPNAME}.lnk"
  RMDir /r "$SMPROGRAMS\${APPNAME}"
  RMDir /r "$INSTDIR"

  ${If} $unOptData == 1
    RMDir /r "${DATADIR}"
  ${Else}
    ${If} $unOptConfig == 1
      Delete "${DATADIR}\settings.json"
    ${EndIf}
    Delete "${DATADIR}\logs\crash-*.log"
  ${EndIf}

  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
SectionEnd
