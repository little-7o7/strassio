; Установщик Strassio (docs/SPEC.md, раздел 14.2).
;
; Что делает:
;   - сам находит все установленные CorelDRAW (X7 и новее, 32 и 64 бит) и показывает их списком
;     с галочками;
;   - копирует аддон в папку Addons\Strassio каждой отмеченной версии;
;   - не даёт ставить поверх запущенного CorelDRAW (файлы были бы заняты);
;   - удаляется через «Программы и компоненты», настройки пользователя в %APPDATA%\Strassio
;     при этом не трогаются.
;
; Сборка: installer\build-installer.ps1  (или ISCC.exe installer\Strassio.iss)

#define AppName "Strassio"
#define AppVersion "0.7.2"
#define AppPublisher "Strassio"
#define BuildConfig "Release"
#define BuildDir "..\src\Strassio.Corel\bin\" + BuildConfig + "\net48"

[Setup]
AppId={{8F2A6C14-5B3D-4E27-9A61-7C0E4D8B3A52}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=out
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile=..\assets\icon\strassio-stone.ico
UninstallDisplayIcon={app}\Strassio.Corel.dll
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=auto

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ru.PageCaption=Версии CorelDRAW
ru.PageDescription=Куда поставить Strassio
ru.PageSubCaption=Отметьте версии CorelDRAW, в которые нужно поставить плагин. Найдено автоматически.
ru.NoCorelFound=CorelDRAW на этом компьютере не найден.%n%nСтразы создаются внутри CorelDRAW, поэтому без него плагин работать не будет. Установите CorelDRAW и запустите установщик заново.
ru.SelectAtLeastOne=Отметьте хотя бы одну версию CorelDRAW.
ru.CorelIsRunning=Сейчас запущен CorelDRAW.%n%nЗакройте его полностью и нажмите «Повторить» — иначе Windows не даст заменить файлы плагина.
ru.CopyFailed=Не удалось скопировать файлы в папку:%n%s%n%nПопробуйте запустить установщик от имени администратора.
ru.Bits64=64 бита
ru.Bits32=32 бита
ru.AfterInstall=Запустите CorelDRAW — вверху появится панель «Strassio» с кнопкой-камнем. Если панели нет, откройте докер через меню «Окно → Докеры → Strassio».

en.PageCaption=CorelDRAW versions
en.PageDescription=Where to install Strassio
en.PageSubCaption=Select the CorelDRAW versions to install the plugin into. Detected automatically.
en.NoCorelFound=CorelDRAW was not found on this computer.%n%nStrassio works inside CorelDRAW, so it cannot run without it. Install CorelDRAW and run this setup again.
en.SelectAtLeastOne=Select at least one CorelDRAW version.
en.CorelIsRunning=CorelDRAW is currently running.%n%nClose it completely and click Retry — otherwise Windows will not let the plugin files be replaced.
en.CopyFailed=Could not copy files to:%n%s%n%nTry running the installer as administrator.
en.Bits64=64-bit
en.Bits32=32-bit
en.AfterInstall=Start CorelDRAW — the "Strassio" toolbar with the stone button will appear. If it is missing, open the docker via "Window - Dockers - Strassio".

[Files]
; Эталонная копия в Program Files\Strassio. Из неё файлы расходятся по версиям CorelDRAW
; (см. CurStepChanged ниже) — так деинсталлятор точно знает, что ставил именно он.
Source: "{#BuildDir}\Strassio.Corel.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\Strassio.Core.dll";  DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\Strassio.Licensing.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\AppUI.xslt";         DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\UserUI.xslt";        DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\CorelDrw.addon";     DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\lang\*.json";        DestDir: "{app}\lang"; Flags: ignoreversion
Source: "{#BuildDir}\Strassio.Corel.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#BuildDir}\Strassio.Core.pdb";  DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Registry]
Root: HKLM; Subkey: "Software\Strassio"; Flags: uninsdeletekey

[Code]
const
  CorelExeName = 'CorelDRW.exe';

var
  CorelPage: TInputOptionWizardPage;
  CorelTitles: TStringList;   { что видит пользователь }
  CorelAddonDirs: TStringList; { ...\Programs64\Addons\Strassio }

{ ---------- Список файлов аддона ---------- }

function AddonFileCount: Integer;
begin
  Result := 8;
end;

function AddonFileName(Index: Integer): string;
begin
  case Index of
    0: Result := 'Strassio.Corel.dll';
    1: Result := 'Strassio.Core.dll';
    2: Result := 'AppUI.xslt';
    3: Result := 'UserUI.xslt';
    4: Result := 'CorelDrw.addon';
    5: Result := 'Strassio.Corel.pdb';
    6: Result := 'Strassio.Core.pdb';
    7: Result := 'Strassio.Licensing.dll';
  else
    Result := '';
  end;
end;

{ ---------- Запущен ли CorelDRAW ---------- }

function IsCorelRunning: Boolean;
var
  WbemLocator, WbemServices, ProcessSet: Variant;
begin
  Result := False;
  try
    WbemLocator := CreateOleObject('WbemScripting.SWbemLocator');
    WbemServices := WbemLocator.ConnectServer('localhost', 'root\CIMV2');
    ProcessSet := WbemServices.ExecQuery(
      'SELECT Name FROM Win32_Process WHERE Name = "' + CorelExeName + '"');
    Result := ProcessSet.Count > 0;
  except
    { Если WMI недоступен — не мешаем установке, просто не проверяем. }
    Result := False;
  end;
end;

function WaitUntilCorelClosed: Boolean;
begin
  Result := True;
  while IsCorelRunning do
  begin
    if MsgBox(CustomMessage('CorelIsRunning'), mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

{ ---------- Поиск установленных CorelDRAW ---------- }

function IsAllDigits(const S: string): Boolean;
var
  I: Integer;
begin
  Result := Length(S) > 0;
  for I := 1 to Length(S) do
    if (S[I] < '0') or (S[I] > '9') then
    begin
      Result := False;
      Exit;
    end;
end;

{ SuiteDir — папка версии (…\CorelDRAW Graphics Suite 2018 или …\CorelDRAW Graphics Suite\26),
  ProgramsName — 'Programs64' или 'Programs'. }
procedure CheckProgramsDir(const SuiteDir, ProgramsName: string);
var
  ExePath, AddonDir, Title, VersionText, Bits, ParentName: string;
begin
  ExePath := AddBackslash(SuiteDir) + ProgramsName + '\' + CorelExeName;
  if not FileExists(ExePath) then
    Exit;

  AddonDir := AddBackslash(SuiteDir) + ProgramsName + '\Addons\Strassio';
  if CorelAddonDirs.IndexOf(AddonDir) >= 0 then
    Exit;

  { У новых версий папка называется просто числом (…\CorelDRAW Graphics Suite\26),
    у старых — целиком (…\CorelDRAW Graphics Suite 2018). }
  Title := ExtractFileName(SuiteDir);
  if IsAllDigits(Title) then
  begin
    ParentName := ExtractFileName(ExtractFileDir(SuiteDir));
    if ParentName <> '' then
      Title := ParentName + ' ' + Title;
  end;

  if ProgramsName = 'Programs64' then
    Bits := CustomMessage('Bits64')
  else
    Bits := CustomMessage('Bits32');

  if not GetVersionNumbersString(ExePath, VersionText) then
    VersionText := '?';

  CorelTitles.Add(Title + '   (' + Bits + ', ' + VersionText + ')');
  CorelAddonDirs.Add(AddonDir);
end;

procedure CheckSuiteDir(const SuiteDir: string);
begin
  CheckProgramsDir(SuiteDir, 'Programs64');
  CheckProgramsDir(SuiteDir, 'Programs');
end;

{ Обходит …\Corel\* и …\Corel\*\* — двух уровней хватает на обе раскладки папок Corel. }
procedure ScanCorelRoot(const Root: string);
var
  Level1, Level2: TFindRec;
  Dir1: string;
begin
  if not DirExists(Root) then
    Exit;

  if FindFirst(AddBackslash(Root) + '*', Level1) then
  try
    repeat
      if (Level1.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and
         (Level1.Name <> '.') and (Level1.Name <> '..') then
      begin
        Dir1 := AddBackslash(Root) + Level1.Name;
        CheckSuiteDir(Dir1);

        if FindFirst(AddBackslash(Dir1) + '*', Level2) then
        try
          repeat
            if (Level2.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and
               (Level2.Name <> '.') and (Level2.Name <> '..') then
              CheckSuiteDir(AddBackslash(Dir1) + Level2.Name);
          until not FindNext(Level2);
        finally
          FindClose(Level2);
        end;
      end;
    until not FindNext(Level1);
  finally
    FindClose(Level1);
  end;
end;

procedure FindCorelInstallations;
begin
  CorelTitles := TStringList.Create;
  CorelAddonDirs := TStringList.Create;

  { На 32-битной Windows константы commonpf64 нет — туда и не заглядываем. }
  if IsWin64 then
    ScanCorelRoot(ExpandConstant('{commonpf64}') + '\Corel');

  ScanCorelRoot(ExpandConstant('{commonpf32}') + '\Corel');
end;

{ ---------- Мастер установки ---------- }

function InitializeSetup: Boolean;
begin
  Result := WaitUntilCorelClosed;
end;

procedure InitializeWizard;
var
  I: Integer;
begin
  FindCorelInstallations;

  CorelPage := CreateInputOptionPage(
    wpSelectDir,
    CustomMessage('PageCaption'),
    CustomMessage('PageDescription'),
    CustomMessage('PageSubCaption'),
    False, { не «выбрать только одно» — можно отметить сразу несколько версий }
    False);

  for I := 0 to CorelTitles.Count - 1 do
  begin
    CorelPage.Add(CorelTitles[I]);
    CorelPage.Values[I] := True;
  end;

  if CorelTitles.Count = 0 then
    MsgBox(CustomMessage('NoCorelFound'), mbInformation, MB_OK);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  I: Integer;
  AnyChecked: Boolean;
begin
  Result := True;
  if CurPageID <> CorelPage.ID then
    Exit;

  AnyChecked := False;
  for I := 0 to CorelTitles.Count - 1 do
    if CorelPage.Values[I] then
      AnyChecked := True;

  if not AnyChecked then
  begin
    MsgBox(CustomMessage('SelectAtLeastOne'), mbError, MB_OK);
    Result := False;
    Exit;
  end;

  { Пользователь мог открыть CorelDRAW, пока листал мастер. }
  Result := WaitUntilCorelClosed;
end;

{ ---------- Раскладка файлов по версиям CorelDRAW ---------- }

function CopyAddonTo(const TargetDir: string): Boolean;
var
  I: Integer;
  SourceFile: string;
  FindRec: TFindRec;
begin
  Result := False;
  if not ForceDirectories(TargetDir) then
    Exit;

  for I := 0 to AddonFileCount - 1 do
  begin
    SourceFile := ExpandConstant('{app}\') + AddonFileName(I);
    if not FileExists(SourceFile) then
      Continue; { .pdb может не быть — это нормально }

    if not FileCopy(SourceFile, AddBackslash(TargetDir) + AddonFileName(I), False) then
      Exit;
  end;

  { Файлы языков — все, сколько есть (новый язык = новый файл, список менять не нужно). }
  if not ForceDirectories(AddBackslash(TargetDir) + 'lang') then
    Exit;
  if FindFirst(ExpandConstant('{app}\lang\*.json'), FindRec) then
  try
    repeat
      if not FileCopy(ExpandConstant('{app}\lang\') + FindRec.Name,
                      AddBackslash(TargetDir) + 'lang\' + FindRec.Name, False) then
        Exit;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;

  Result := True;
end;

{ Убирает то, что этот же установщик поставил в прошлый раз. Нужно при установке поверх:
  иначе, если версию CorelDRAW сняли галочкой, плагин остался бы в ней навсегда — и деинсталлятор
  про него бы уже не знал. Чужого не трогаем: удаляются только записанные нами папки. }
procedure RemovePreviousInstall;
var
  I: Integer;
  OldCount: Cardinal;
  OldDir: string;
begin
  if not RegQueryDWordValue(HKLM, 'Software\Strassio', 'TargetCount', OldCount) then
    Exit;

  for I := 0 to Integer(OldCount) - 1 do
  begin
    if RegQueryStringValue(HKLM, 'Software\Strassio', 'Target' + IntToStr(I), OldDir) then
      DelTree(OldDir, True, True, True);

    RegDeleteValue(HKLM, 'Software\Strassio', 'Target' + IntToStr(I));
  end;

  RegDeleteValue(HKLM, 'Software\Strassio', 'TargetCount');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  I, Installed: Integer;
  InstalledCount: Cardinal;
begin
  if CurStep <> ssPostInstall then
    Exit;

  RemovePreviousInstall;

  Installed := 0;
  for I := 0 to CorelAddonDirs.Count - 1 do
  begin
    if not CorelPage.Values[I] then
      Continue;

    if CopyAddonTo(CorelAddonDirs[I]) then
    begin
      { Запоминаем, куда поставили — деинсталлятор уберёт ровно эти папки. }
      RegWriteStringValue(HKLM, 'Software\Strassio', 'Target' + IntToStr(Installed), CorelAddonDirs[I]);
      Installed := Installed + 1;
    end
    else
      MsgBox(FmtMessage(CustomMessage('CopyFailed'), [CorelAddonDirs[I]]), mbError, MB_OK);
  end;

  InstalledCount := Installed;
  RegWriteDWordValue(HKLM, 'Software\Strassio', 'TargetCount', InstalledCount);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
    WizardForm.FinishedLabel.Caption := CustomMessage('AfterInstall');
end;

{ ---------- Удаление ---------- }

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  I: Integer;
  Count: Cardinal;
  TargetDir: string;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  while IsCorelRunning do
    if MsgBox(CustomMessage('CorelIsRunning'), mbError, MB_RETRYCANCEL) = IDCANCEL then
      Abort;

  if not RegQueryDWordValue(HKLM, 'Software\Strassio', 'TargetCount', Count) then
    Count := 0;

  for I := 0 to Integer(Count) - 1 do
    if RegQueryStringValue(HKLM, 'Software\Strassio', 'Target' + IntToStr(I), TargetDir) then
      DelTree(TargetDir, True, True, True);

  { Настройки и таблицу камней в %APPDATA%\Strassio намеренно НЕ трогаем (SPEC, раздел 14.2). }
end;
