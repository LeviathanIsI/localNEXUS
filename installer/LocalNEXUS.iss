; LocalNEXUS installer.
;
; Builds with Inno Setup 6.4 or newer, which is where the download page gained SHA-256
; verification and where archive extraction became something Setup does itself rather than
; something it shells out for.
;
; The inference engines are not in this installer and are not redistributed by it. They are
; fetched from their own release pages at install time, so their licences stay theirs and this
; installer stays small enough to download on a phone tether.
;
;   iscc installer\LocalNEXUS.iss
;
; See installer\README.md for how to bump a pinned engine version and how to sign a build.

#define AppName "localNEXUS"
#define AppVersion "1.6.0"
#define AppPublisher "You Know Its Me Studios"
#define AppUrl "https://github.com/You-Know-Its-Me-Studios/LocalNEXUS"
; The file name the build produces, which is not the product name. AssemblyName is
; LocalNEXUS and renaming it would be an application change, so the brand is applied to
; what people read and the file keeps the spelling the build gives it.
#define AppExeName "LocalNEXUS.exe"

; Where the user data folder lives. Spelled the way AppPaths.Root spells it, because that
; is what decides where the engines are looked for. Not renamed to match the brand: it
; would orphan the settings, saved graphs and models catalogue of every existing install.
#define DataDir "{localappdata}\LocalNEXUS"
#define VendorDir DataDir + "\vendor"

; ---------------------------------------------------------------------------
; Pinned engine releases.
;
; llama.cpp puts its build number in every asset name, so its "latest" URL cannot be used and
; the build is pinned. uv and Mesh LLM both publish unversioned asset names that would work
; with a latest URL, but they are pinned too, because a pinned URL is the only one whose
; checksum can be stated in advance and an unverified engine binary produces failures that
; look exactly like application bugs.
;
; Every hash below came from the GitHub release API rather than from hashing a local download.
; ---------------------------------------------------------------------------
#define LlamaBuild "b10549"
#define LlamaUrl "https://github.com/ggml-org/llama.cpp/releases/download/" + LlamaBuild + "/"

#define LlamaCuda13Name "llama-" + LlamaBuild + "-bin-win-cuda-13.3-x64.zip"
#define LlamaCuda13Hash "67a1097716a4b4c20b94d248d1b3886fd7b91b73d9af5e0630fd6a25a32309a5"
#define LlamaCuda13Size 146945631
#define CudartCuda13Name "cudart-llama-bin-win-cuda-13.3-x64.zip"
#define CudartCuda13Hash "1462a050eb4c684921ba51dcc4cc488a036674c3e73e9945ee705b854808d03e"
#define CudartCuda13Size 390970417

#define LlamaCuda12Name "llama-" + LlamaBuild + "-bin-win-cuda-12.4-x64.zip"
#define LlamaCuda12Hash "2e980ae28b40c92c9c30bdbcf3f28064b40104472e213c52edbeb89b920d65fe"
#define LlamaCuda12Size 250969968
#define CudartCuda12Name "cudart-llama-bin-win-cuda-12.4-x64.zip"
#define CudartCuda12Hash "8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6"
#define CudartCuda12Size 391443627

#define LlamaVulkanName "llama-" + LlamaBuild + "-bin-win-vulkan-x64.zip"
#define LlamaVulkanHash "8e7b0e6382a5bcbf57c79cf54b61483e9f7b26561d4413f28095cdaee256207b"
#define LlamaVulkanSize 34936498

#define LlamaCpuName "llama-" + LlamaBuild + "-bin-win-cpu-x64.zip"
#define LlamaCpuHash "11d38f2ed878489b2c3d02b3d1a67683c02fbfb3d265876b9ede749a8dff5f1c"
#define LlamaCpuSize 18581129

; Vulkan only, deliberately. The CUDA bundle is 824 MB against this 50 MB, and on a CUDA 13
; era driver it has been seen to report zero GPUs and fall back to the processor anyway. There
; is no flavour here to get wrong.
#define MeshVersion "v0.75.1"
#define MeshUrl "https://github.com/Mesh-LLM/mesh-llm/releases/download/" + MeshVersion + "/"
#define MeshName "mesh-llm-" + MeshVersion + "-x86_64-pc-windows-msvc-vulkan.zip"
#define MeshHash "92ecb0bef7678651264d35d8f41ae210e82ca78dcfba796ee4df50cd75776ff2"
#define MeshSize 53220543

#define UvVersion "0.12.5"
#define UvUrl "https://github.com/astral-sh/uv/releases/download/" + UvVersion + "/"
#define UvName "uv-x86_64-pc-windows-msvc.zip"
#define UvHash "4c4d49d8738847d9b71ba319e49a5688c93eac0fe6204b1df24e98528dddf39a"
#define UvSize 20329591

[Setup]
; Never change AppId. It is what lets a later build recognise this install, keep the previous
; component choice, and replace rather than duplicate it in Add or remove programs.
AppId={{8B4E2C71-5F3A-4D89-9E6B-2A7C1D0F3E58}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} installer

; Per user, under Programs, the way VS Code and Discord install. Three reasons, in order of
; how much they matter:
;
; 1. The application has to be able to repair or replace an engine binary later. Program Files
;    is not writable by a standard user, so an install there could never do that without
;    asking for elevation every time.
; 2. No elevation prompt at all, which removes the scariest dialog in the flow for something
;    already unsigned.
; 3. The Python runtime already lives under %LOCALAPPDATA%, so this keeps everything the
;    application owns in one place the user can delete.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=no
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
LicenseFile=..\LICENSE

; WPF and a win-x64 self contained build, so there is nothing to offer anyone else.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

OutputDir=..\dist\installer
OutputBaseFilename={#AppName}-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes

; The engine archives are .zip rather than .7z, which is what "full" covers.
ArchiveExtraction=full

WizardStyle=modern
WizardSizePercent=120
; The modern style hides the welcome page by default. Keep it: it is where the large
; image lives, and it is the first thing that says this is a product rather than a
; generic setup wrapped around somebody's zip.
DisableWelcomePage=no
WizardImageFile=assets\wizard-large.bmp,assets\wizard-large-125.bmp,assets\wizard-large-150.bmp,assets\wizard-large-200.bmp
WizardSmallImageFile=assets\wizard-small.bmp,assets\wizard-small-125.bmp,assets\wizard-small-150.bmp,assets\wizard-small-200.bmp
SetupIconFile=assets\LocalNEXUS.ico
WizardImageAlphaFormat=none

; ---------------------------------------------------------------------------
; Signing.
;
; Not signed today. Most people taking a build from GitHub Releases understand an unsigned
; installer, and the SmartScreen warning is a known cost of that.
;
; Nothing here has to be restructured to add it. Define a sign tool once, either in the Inno
; Setup IDE under Tools then Configure Sign Tools, or by passing it on the command line:
;
;   iscc /Ssigntool="C:\path\signtool.exe sign /fd sha256 /tr http://timestamp.url /td sha256 $f" installer\LocalNEXUS.iss
;
; then uncomment the two lines below. SignedUninstaller signs the uninstaller that Setup
; writes at install time, which is a separate executable and is missed easily.
; ---------------------------------------------------------------------------
; SignTool=signtool
; SignedUninstaller=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Everything"
Name: "typical"; Description: "Local models only"
Name: "custom"; Description: "Choose what to install"; Flags: iscustom

[Components]
Name: "app"; Description: "LocalNEXUS"; Types: full typical custom; Flags: fixed; ExtraDiskSpaceRequired: 190000000
Name: "llama"; Description: "llama.cpp, so you can run GGUF models. Most people want this"; Types: full typical
Name: "mesh"; Description: "Mesh LLM, so the Network tab can split a model across machines"; Types: full
Name: "uv"; Description: "uv, so safetensors models can be served"; Types: full

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\dist\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion; Components: app

; The dependency lockfiles are committed rather than fetched, because the whole point of them
; is that every install resolves to identical packages. They are text and weigh nothing, so
; they travel inside this installer.
Source: "..\vendor\python\requirements-cu132.txt"; DestDir: "{#VendorDir}\python"; Flags: ignoreversion; Components: uv
Source: "..\vendor\python\requirements-cpu.txt"; DestDir: "{#VendorDir}\python"; Flags: ignoreversion; Components: uv
Source: "..\vendor\python\requirements.in"; DestDir: "{#VendorDir}\python"; Flags: ignoreversion; Components: uv

; Everything below is downloaded into {tmp} first and extracted from there. The external flag
; says the file is not inside this installer; extractarchive says to unpack it rather than
; copy it. Each has a Check so that a component already present is neither downloaded nor
; unpacked again.
Source: "{tmp}\llama.zip"; DestDir: "{#VendorDir}\llama"; Flags: external extractarchive recursesubdirs ignoreversion; Components: llama; Check: WillInstallLlama
Source: "{tmp}\cudart.zip"; DestDir: "{#VendorDir}\llama"; Flags: external extractarchive recursesubdirs ignoreversion; Components: llama; Check: WillInstallCudart
Source: "{tmp}\mesh.zip"; DestDir: "{#VendorDir}\mesh"; Flags: external extractarchive recursesubdirs ignoreversion; Components: mesh; Check: WillInstallMesh
Source: "{tmp}\uv.zip"; DestDir: "{#VendorDir}\uv"; Flags: external extractarchive recursesubdirs ignoreversion; Components: uv; Check: WillInstallUv

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The engines this installer downloaded. Everything else under the data folder belongs to the
; user, and removing it is a separate question asked at uninstall time.
Type: filesandordirs; Name: "{#VendorDir}\llama"
Type: filesandordirs; Name: "{#VendorDir}\mesh"
Type: filesandordirs; Name: "{#VendorDir}\uv"
Type: filesandordirs; Name: "{#VendorDir}\python"
Type: dirifempty; Name: "{#VendorDir}"

[Code]
type
  TGpuKind = (GpuNvidiaCuda13, GpuNvidiaCuda12, GpuOther, GpuNone);

var
  DownloadPage: TDownloadWizardPage;
  FlavourPage: TInputOptionWizardPage;

  DetectedGpu: TGpuKind;
  DetectedName: String;
  DetectedDriver: String;
  DetectionNote: String;

  RemoveDataCheck: TNewCheckBox;

const
  FlavourCuda13 = 0;
  FlavourCuda12 = 1;
  FlavourVulkan = 2;
  FlavourCpu    = 3;

  { The oldest NVIDIA driver the CUDA 13 build runs on. Same floor AcceleratorProbe uses for
    the CUDA 13 torch wheels, and the same reason: the driver is what constrains it, and a
    driver can be updated without buying a graphics card. }
  MinimumCuda13Driver = 580;

{ ---------------------------------------------------------------------------
  Dark mode.

  Inno has no dark theme and there is no setting for one, so the wizard is repainted from
  script. A third party skinning DLL would have done it too, and was not used: it is another
  binary to redistribute under someone else's licence, inside an installer whose entire point
  is that it does not redistribute other people's binaries.

  Two things fight back and both are handled below.

  A control with the Windows visual style attached draws itself and ignores whatever Color it
  was given, which is why buttons, checkboxes and edits would stay light no matter what is
  assigned. SetWindowTheme with empty strings detaches the style, after which the control
  honours Color again.

  The title bar is not the application's to paint at all. It belongs to the desktop window
  manager, and the only way to darken it is to ask DWM directly.

  Colours are lifted from Views\Themes\EditorDark.xaml so the installer and the application
  are the same grey rather than two different greys.
  --------------------------------------------------------------------------- }

procedure SetWindowTheme(Wnd: HWND; SubAppName: String; SubIdList: String);
  external 'SetWindowTheme@uxtheme.dll stdcall';

function DwmSetWindowAttribute(Wnd: HWND; Attribute: Integer; var Value: Integer;
  Size: Integer): Integer; external 'DwmSetWindowAttribute@dwmapi.dll stdcall';

const
  { TColor is $00BBGGRR, so these are the theme's hex values with the ends swapped. }
  ClrWindow  = $1E1E1E;  { #1E1E1E Surface.Window }
  ClrPanel   = $262525;  { #252526 Surface.Panel }
  ClrInput   = $3C3C3C;  { #3C3C3C Surface.Input }
  ClrBorder  = $423E3E;  { #3E3E42 Surface.Border }
  ClrText    = $D4D4D4;  { #D4D4D4 Text.Primary }
  ClrMuted   = $A6A6A6;  { #A6A6A6 Text.Secondary }
  ClrAccent  = $CC7A00;  { #007ACC Accent.Primary }

  DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

procedure DarkTitleBar(Wnd: HWND);
var
  Enabled: Integer;
begin
  Enabled := 1;
  { Fails harmlessly on a Windows build that does not know the attribute. }
  try
    DwmSetWindowAttribute(Wnd, DWMWA_USE_IMMERSIVE_DARK_MODE, Enabled, SizeOf(Enabled));
  except
    Log('The window manager would not darken the title bar on this build of Windows.');
  end;
end;

procedure Darken(Ctl: TControl); forward;

procedure DarkenChildren(Parent: TWinControl);
var
  I: Integer;
begin
  for I := 0 to Parent.ControlCount - 1 do
    Darken(Parent.Controls[I]);
end;

procedure Darken(Ctl: TControl);
begin
  if Ctl = nil then
    Exit;

  if Ctl is TNewStaticText then
  begin
    TNewStaticText(Ctl).Color := ClrWindow;
    TNewStaticText(Ctl).Font.Color := ClrText;
  end
  else if Ctl is TNewLinkLabel then
  begin
    TNewLinkLabel(Ctl).Color := ClrWindow;
    TNewLinkLabel(Ctl).Font.Color := ClrText;
  end
  else if Ctl is TLabel then
  begin
    TLabel(Ctl).Color := ClrWindow;
    TLabel(Ctl).Font.Color := ClrText;
  end
  else if Ctl is TNewCheckBox then
  begin
    SetWindowTheme(TNewCheckBox(Ctl).Handle, '', '');
    TNewCheckBox(Ctl).Color := ClrWindow;
    TNewCheckBox(Ctl).Font.Color := ClrText;
  end
  else if Ctl is TNewRadioButton then
  begin
    SetWindowTheme(TNewRadioButton(Ctl).Handle, '', '');
    TNewRadioButton(Ctl).Color := ClrWindow;
    TNewRadioButton(Ctl).Font.Color := ClrText;
  end
  else if Ctl is TNewEdit then
  begin
    TNewEdit(Ctl).Color := ClrInput;
    TNewEdit(Ctl).Font.Color := ClrText;
  end
  else if Ctl is TNewMemo then
  begin
    TNewMemo(Ctl).Color := ClrInput;
    TNewMemo(Ctl).Font.Color := ClrText;
  end
  else if Ctl is TNewComboBox then
  begin
    TNewComboBox(Ctl).Color := ClrInput;
    TNewComboBox(Ctl).Font.Color := ClrText;
  end
  else if Ctl is TNewListBox then
  begin
    TNewListBox(Ctl).Color := ClrInput;
    TNewListBox(Ctl).Font.Color := ClrText;
  end
  else if Ctl is TNewCheckListBox then
  begin
    TNewCheckListBox(Ctl).Color := ClrPanel;
    TNewCheckListBox(Ctl).Font.Color := ClrText;
    { The tick itself is drawn by the theme, so it has to be detached like the checkboxes. }
    SetWindowTheme(TNewCheckListBox(Ctl).Handle, '', '');
  end
  else if Ctl is TRichEditViewer then
  begin
    TRichEditViewer(Ctl).Color := ClrPanel;
    TRichEditViewer(Ctl).Font.Color := ClrText;
  end
  else if Ctl is TNewButton then
  begin
    { TNewButton exposes no Color to script, so the button face cannot be repainted from
      here. Detaching the theme still darkens it, because an unthemed button takes its face
      from the parent's colour. }
    SetWindowTheme(TNewButton(Ctl).Handle, '', '');
    TNewButton(Ctl).Font.Color := ClrText;
  end
  else if Ctl is TNewProgressBar then
  begin
    { Detaching the theme is what lets the bar take the accent colour instead of system green. }
    SetWindowTheme(TNewProgressBar(Ctl).Handle, '', '');
  end
  else if Ctl is TBevel then
  begin
    TBevel(Ctl).Visible := False;
  end
  else if Ctl is TPanel then
  begin
    TPanel(Ctl).Color := ClrWindow;
    TPanel(Ctl).Font.Color := ClrText;
  end;

  if Ctl is TWinControl then
    DarkenChildren(TWinControl(Ctl));
end;

procedure DarkenWizard;
begin
  WizardForm.Color := ClrWindow;
  WizardForm.Font.Color := ClrText;

  { The notebooks expose no Color and their page class cannot be matched with "is", but the
    named page properties can be coloured one by one. These are the page bodies, which is the
    largest area on screen and the one thing that stays white if this is skipped. }
  WizardForm.WelcomePage.Color := ClrWindow;
  WizardForm.InnerPage.Color := ClrWindow;
  WizardForm.LicensePage.Color := ClrWindow;
  WizardForm.SelectDirPage.Color := ClrWindow;
  WizardForm.SelectComponentsPage.Color := ClrWindow;
  WizardForm.SelectTasksPage.Color := ClrWindow;
  WizardForm.ReadyPage.Color := ClrWindow;
  WizardForm.PreparingPage.Color := ClrWindow;
  WizardForm.InstallingPage.Color := ClrWindow;
  WizardForm.FinishedPage.Color := ClrWindow;

  DarkenChildren(WizardForm);
  DarkTitleBar(WizardForm.Handle);
end;

function BytesToMb(Bytes: Int64): String;
begin
  Result := IntToStr((Bytes + 524288) div 1048576) + ' MB';
end;

{ ---------------------------------------------------------------------------
  GPU detection.

  nvidia-smi is asked first, because it is what the application itself asks and because it
  reports the display driver version directly. A machine with two GPUs, an integrated one and
  a discrete one, is the ordinary laptop case and nvidia-smi answers for the discrete card,
  which is the one worth building for.

  WMI is the fallback, and only has to answer one question: is there a GPU here at all.
  --------------------------------------------------------------------------- }

function RunAndCapture(const CommandLine: String; var Output: String): Boolean;
var
  TempFile: String;
  ResultCode: Integer;
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := False;
  Output := '';
  TempFile := ExpandConstant('{tmp}\probe.txt');

  { Setup cannot read another process's output directly, so it goes through a file. }
  if not Exec(ExpandConstant('{cmd}'), '/C ' + CommandLine + ' > "' + TempFile + '" 2>&1',
              '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Exit;

  if ResultCode <> 0 then
    Exit;

  if not LoadStringsFromFile(TempFile, Lines) then
    Exit;

  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    if Trim(Lines[I]) <> '' then
    begin
      Output := Trim(Lines[I]);
      Result := True;
      Exit;
    end;
  end;
end;

function DriverMajor(const Version: String): Integer;
var
  Head: String;
  Dot: Integer;
begin
  Head := Trim(Version);
  Dot := Pos('.', Head);
  if Dot > 0 then
    Head := Copy(Head, 1, Dot - 1);
  Result := StrToIntDef(Head, 0);
end;

procedure DetectGpu;
var
  Line, Rest: String;
  Comma, Major: Integer;
begin
  DetectedGpu := GpuNone;
  DetectedName := '';
  DetectedDriver := '';

  if RunAndCapture('nvidia-smi --query-gpu=driver_version,name --format=csv,noheader', Line) then
  begin
    Comma := Pos(',', Line);
    if Comma > 0 then
    begin
      DetectedDriver := Trim(Copy(Line, 1, Comma - 1));
      Rest := Trim(Copy(Line, Comma + 1, Length(Line)));
      DetectedName := Rest;

      Major := DriverMajor(DetectedDriver);
      if Major >= MinimumCuda13Driver then
      begin
        DetectedGpu := GpuNvidiaCuda13;
        DetectionNote := DetectedName + ' with driver ' + DetectedDriver +
          ', which runs the CUDA 13 build.';
      end
      else if Major > 0 then
      begin
        DetectedGpu := GpuNvidiaCuda12;
        DetectionNote := DetectedName + ' with driver ' + DetectedDriver +
          ', older than the ' + IntToStr(MinimumCuda13Driver) +
          ' the CUDA 13 build needs, so the CUDA 12 build was chosen.';
      end
      else
      begin
        DetectedGpu := GpuOther;
        DetectionNote := DetectedName + ' reported driver ' + DetectedDriver +
          ', which could not be read, so Vulkan was chosen because it works on anything.';
      end;
      Exit;
    end;
  end;

  { No NVIDIA driver answered. Ask Windows whether there is any display adapter at all. }
  if RunAndCapture('wmic path win32_VideoController get name', Line) then
  begin
    if (Line <> '') and (CompareText(Line, 'Name') <> 0) then
    begin
      DetectedGpu := GpuOther;
      DetectedName := Line;
      DetectionNote := DetectedName + ' is not an NVIDIA card, so Vulkan was chosen. ' +
        'It runs on AMD and Intel graphics.';
      Exit;
    end;
  end;

  DetectedGpu := GpuNone;
  DetectionNote := 'No graphics card answered, so the processor build was chosen. ' +
    'It works, and it is slow.';
end;

function DefaultFlavour: Integer;
begin
  case DetectedGpu of
    GpuNvidiaCuda13: Result := FlavourCuda13;
    GpuNvidiaCuda12: Result := FlavourCuda12;
    GpuOther:        Result := FlavourVulkan;
  else
    Result := FlavourCpu;
  end;
end;

function SelectedFlavour: Integer;
var
  I: Integer;
begin
  Result := DefaultFlavour;
  if FlavourPage = nil then
    Exit;
  for I := 0 to FlavourPage.CheckListBox.Items.Count - 1 do
    if FlavourPage.Values[I] then
    begin
      Result := I;
      Exit;
    end;
end;

function FlavourNeedsCudart: Boolean;
begin
  Result := (SelectedFlavour = FlavourCuda13) or (SelectedFlavour = FlavourCuda12);
end;

{ ---------------------------------------------------------------------------
  What is already there.

  Re-running Setup is how somebody adds a component they skipped, so a component that is
  already present is neither downloaded nor unpacked again. Half a gigabyte is too much to
  fetch twice for no reason.
  --------------------------------------------------------------------------- }

function VendorPath(const Leaf: String): String;
begin
  Result := ExpandConstant('{#VendorDir}\') + Leaf;
end;

function LlamaPresent: Boolean;
begin
  Result := FileExists(VendorPath('llama\llama-server.exe'));
end;

function MeshPresent: Boolean;
begin
  Result := FileExists(VendorPath('mesh\mesh-bundle\mesh-llm.exe')) or
            FileExists(VendorPath('mesh\mesh-llm.exe'));
end;

function UvPresent: Boolean;
begin
  Result := FileExists(VendorPath('uv\uv.exe'));
end;

function WillInstallLlama: Boolean;
begin
  Result := WizardIsComponentSelected('llama') and not LlamaPresent;
end;

function WillInstallCudart: Boolean;
begin
  Result := WillInstallLlama and FlavourNeedsCudart;
end;

function WillInstallMesh: Boolean;
begin
  Result := WizardIsComponentSelected('mesh') and not MeshPresent;
end;

function WillInstallUv: Boolean;
begin
  Result := WizardIsComponentSelected('uv') and not UvPresent;
end;

function LlamaDownloadBytes: Int64;
begin
  case SelectedFlavour of
    FlavourCuda13: Result := Int64({#LlamaCuda13Size}) + Int64({#CudartCuda13Size});
    FlavourCuda12: Result := Int64({#LlamaCuda12Size}) + Int64({#CudartCuda12Size});
    FlavourVulkan: Result := Int64({#LlamaVulkanSize});
  else
    Result := Int64({#LlamaCpuSize});
  end;
end;

function TotalDownloadBytes: Int64;
begin
  Result := 0;
  if WillInstallLlama then Result := Result + LlamaDownloadBytes;
  if WillInstallMesh then Result := Result + Int64({#MeshSize});
  if WillInstallUv then Result := Result + Int64({#UvSize});
end;

{ ---------------------------------------------------------------------------
  Wizard
  --------------------------------------------------------------------------- }

procedure InitializeWizard;
begin
  DetectGpu;

  FlavourPage := CreateInputOptionPage(wpSelectComponents,
    'Which llama.cpp build',
    'The right build for your graphics card.',
    DetectionNote + #13#10#13#10 +
    'Chosen for you below. Change it if you know better, and note what each one costs to ' +
    'download. Vulkan is the small safe answer that runs on any card.',
    True, False);

  FlavourPage.Add('CUDA 13, for NVIDIA with driver ' + IntToStr(MinimumCuda13Driver) +
    ' or newer. ' + BytesToMb(Int64({#LlamaCuda13Size}) + Int64({#CudartCuda13Size})) +
    ' to download, in two files');
  FlavourPage.Add('CUDA 12, for NVIDIA with an older driver. ' +
    BytesToMb(Int64({#LlamaCuda12Size}) + Int64({#CudartCuda12Size})) +
    ' to download, in two files');
  FlavourPage.Add('Vulkan, for AMD, Intel, and NVIDIA if you would rather. ' +
    BytesToMb(Int64({#LlamaVulkanSize})) + ' to download');
  FlavourPage.Add('Processor only, no graphics card needed. ' +
    BytesToMb(Int64({#LlamaCpuSize})) + ' to download, and slow');

  FlavourPage.Values[DefaultFlavour] := True;

  DownloadPage := CreateDownloadPage(
    'Getting the engines',
    'These come from their own release pages rather than from inside this installer.',
    nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;

  DarkenWizard;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  { Cheap and idempotent, which is what lets it run on every page rather than trying to work
    out which pages have new controls on them. }
  DarkenWizard;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  { The flavour question only matters if llama.cpp is being fetched at all. }
  if (FlavourPage <> nil) and (PageID = FlavourPage.ID) then
    Result := not WillInstallLlama;
end;

function UpdateReadyMemo(const Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  Engines: String;
begin
  Result := MemoDirInfo + NewLine + NewLine + MemoComponentsInfo + NewLine;

  if MemoTasksInfo <> '' then
    Result := Result + NewLine + MemoTasksInfo + NewLine;

  Engines := '';

  if WizardIsComponentSelected('llama') then
  begin
    if LlamaPresent then
      Engines := Engines + Space + 'llama.cpp is already installed and will be kept' + NewLine
    else
      case SelectedFlavour of
        FlavourCuda13: Engines := Engines + Space + 'llama.cpp CUDA 13, ' + BytesToMb(LlamaDownloadBytes) + NewLine;
        FlavourCuda12: Engines := Engines + Space + 'llama.cpp CUDA 12, ' + BytesToMb(LlamaDownloadBytes) + NewLine;
        FlavourVulkan: Engines := Engines + Space + 'llama.cpp Vulkan, ' + BytesToMb(LlamaDownloadBytes) + NewLine;
      else
        Engines := Engines + Space + 'llama.cpp processor only, ' + BytesToMb(LlamaDownloadBytes) + NewLine;
      end;
  end;

  if WizardIsComponentSelected('mesh') then
  begin
    if MeshPresent then
      Engines := Engines + Space + 'Mesh LLM is already installed and will be kept' + NewLine
    else
      Engines := Engines + Space + 'Mesh LLM Vulkan, ' + BytesToMb(Int64({#MeshSize})) + NewLine;
  end;

  if WizardIsComponentSelected('uv') then
  begin
    if UvPresent then
      Engines := Engines + Space + 'uv is already installed and will be kept' + NewLine
    else
      Engines := Engines + Space + 'uv, ' + BytesToMb(Int64({#UvSize})) + NewLine;
  end;

  if Engines = '' then
    Engines := Space + 'Nothing. LocalNEXUS will start but cannot run a model.' + NewLine;

  Result := Result + NewLine + 'Engines:' + NewLine + Engines;

  if TotalDownloadBytes > 0 then
    Result := Result + NewLine + Space + 'To download: ' + BytesToMb(TotalDownloadBytes) + NewLine;
end;

function HaveRoomFor(const Path: String; Needed: Int64; const What: String): Boolean;
var
  FreeBytes, TotalBytes: Int64;
begin
  Result := True;
  if not GetSpaceOnDisk64(Path, FreeBytes, TotalBytes) then
    Exit;

  if FreeBytes >= Needed then
    Exit;

  Result := False;
  SuppressibleMsgBox(
    'Not enough room on ' + Copy(Path, 1, 2) + ' for ' + What + '.' + #13#10#13#10 +
    'Needed: ' + BytesToMb(Needed) + #13#10 +
    'Free: ' + BytesToMb(FreeBytes) + #13#10#13#10 +
    'Free some space and press Next again, or go back and untick a component.',
    mbCriticalError, MB_OK, IDOK);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Total: Int64;
  Error: String;
begin
  Result := True;

  if CurPageID = wpSelectComponents then
  begin
    if not (WizardIsComponentSelected('llama') or WizardIsComponentSelected('mesh') or
            WizardIsComponentSelected('uv')) then
    begin
      { Better to say this now than to let somebody find out after installing.

        Suppressible with a default of Yes, which matters: a silent install that asked for
        only the application has said what it wants, and stopping to argue with a script
        that cannot answer would turn a deliberate choice into a failed install. }
      Result := SuppressibleMsgBox(
        'Nothing is ticked except LocalNEXUS itself.' + #13#10#13#10 +
        'It will install and start, and it will not be able to run a model, because the ' +
        'engine that does that is one of the boxes above. llama.cpp is the one most people ' +
        'want.' + #13#10#13#10 +
        'You can add them later by running this installer again, or by hand: each folder ' +
        'under the data folder has a README naming the release to download.' + #13#10#13#10 +
        'Continue with nothing ticked?',
        mbConfirmation, MB_YESNO, IDYES) = IDYES;
    end;
    Exit;
  end;

  if CurPageID = wpReady then
  begin
    Total := TotalDownloadBytes;

    { Downloads land in the temporary folder and are then unpacked into the data folder, so
      both have to have room, and they are often not the same drive. Roughly two and a half
      times the archive once unpacked. }
    if Total > 0 then
      if not HaveRoomFor(ExpandConstant('{tmp}'), Total, 'the downloads') then
      begin
        Result := False;
        Exit;
      end;

    if not HaveRoomFor(ExpandConstant('{localappdata}'),
                       (Total * 5) div 2 + Int64(190000000), 'LocalNEXUS and its engines') then
    begin
      Result := False;
      Exit;
    end;

    if Total = 0 then
      Exit;

    DownloadPage.Clear;

    if WillInstallLlama then
    begin
      case SelectedFlavour of
        FlavourCuda13:
          begin
            DownloadPage.Add('{#LlamaUrl}{#LlamaCuda13Name}', 'llama.zip', '{#LlamaCuda13Hash}');
            DownloadPage.Add('{#LlamaUrl}{#CudartCuda13Name}', 'cudart.zip', '{#CudartCuda13Hash}');
          end;
        FlavourCuda12:
          begin
            DownloadPage.Add('{#LlamaUrl}{#LlamaCuda12Name}', 'llama.zip', '{#LlamaCuda12Hash}');
            DownloadPage.Add('{#LlamaUrl}{#CudartCuda12Name}', 'cudart.zip', '{#CudartCuda12Hash}');
          end;
        FlavourVulkan:
          DownloadPage.Add('{#LlamaUrl}{#LlamaVulkanName}', 'llama.zip', '{#LlamaVulkanHash}');
      else
        DownloadPage.Add('{#LlamaUrl}{#LlamaCpuName}', 'llama.zip', '{#LlamaCpuHash}');
      end;
    end;

    if WillInstallMesh then
      DownloadPage.Add('{#MeshUrl}{#MeshName}', 'mesh.zip', '{#MeshHash}');

    if WillInstallUv then
      DownloadPage.Add('{#UvUrl}{#UvName}', 'uv.zip', '{#UvHash}');

    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        Result := True;
      except
        if DownloadPage.AbortedByUser then
        begin
          Log('Download cancelled by the user.');
          Result := False;
        end
        else
        begin
          Error := GetExceptionMessage;
          Log('Download failed: ' + Error);

          { Name the file, say what went wrong, and say what to do about it. A generic
            failure here is worse than never having offered the download. }
          SuppressibleMsgBox(
            'Could not get ' + DownloadPage.LastBaseNameOrUrl + '.' + #13#10#13#10 +
            Error + #13#10#13#10 +
            'What usually causes this:' + #13#10 +
            '  No internet, or a proxy or firewall blocking github.com' + #13#10 +
            '  The release has moved, if this installer is an old one' + #13#10 +
            '  The file arrived damaged, which is what the checksum caught' + #13#10#13#10 +
            'You can press Back and untick the engines, install LocalNEXUS on its own, and ' +
            'add the binaries by hand later. Each folder under the data folder has a README ' +
            'naming the release to download and where to put it.',
            mbCriticalError, MB_OK, IDOK);
          Result := False;
        end;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

{ Leaves a note beside the engines saying where they came from, so somebody looking at the
  folder later can tell what put them there and how to replace them by hand. }
procedure WriteVendorNote(const Leaf, Text: String);
var
  Folder: String;
begin
  Folder := VendorPath(Leaf);
  if not DirExists(Folder) then
    Exit;
  SaveStringToFile(Folder + '\INSTALLED-BY-SETUP.txt', Text, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then
    Exit;

  if WizardIsComponentSelected('llama') and LlamaPresent then
    WriteVendorNote('llama',
      'Placed here by the LocalNEXUS {#AppVersion} installer, from llama.cpp {#LlamaBuild}.' + #13#10 +
      'https://github.com/ggml-org/llama.cpp/releases/tag/{#LlamaBuild}' + #13#10 +
      'Licensed under MIT by the llama.cpp authors. Not part of LocalNEXUS.' + #13#10 +
      'Delete this folder and run the installer again to replace it.' + #13#10);

  if WizardIsComponentSelected('mesh') and MeshPresent then
    WriteVendorNote('mesh',
      'Placed here by the LocalNEXUS {#AppVersion} installer, from Mesh LLM {#MeshVersion}.' + #13#10 +
      'https://github.com/Mesh-LLM/mesh-llm/releases/tag/{#MeshVersion}' + #13#10 +
      'Licensed under Apache-2.0. Not part of LocalNEXUS.' + #13#10 +
      'Delete this folder and run the installer again to replace it.' + #13#10);

  if WizardIsComponentSelected('uv') and UvPresent then
    WriteVendorNote('uv',
      'Placed here by the LocalNEXUS {#AppVersion} installer, from uv {#UvVersion}.' + #13#10 +
      'https://github.com/astral-sh/uv/releases/tag/{#UvVersion}' + #13#10 +
      'Licensed under Apache-2.0 or MIT, at your option. Not part of LocalNEXUS.' + #13#10 +
      'Delete this folder and run the installer again to replace it.' + #13#10);
end;

{ ---------------------------------------------------------------------------
  Uninstall.

  The data folder holds the config, saved graphs, the models catalogue and the Python
  environment. Deleting that by default would throw away work somebody did, so it is a
  separate question with the box unticked.
  --------------------------------------------------------------------------- }

procedure InitializeUninstallProgressForm;
var
  Page: TNewNotebookPage;
  Label1: TNewStaticText;
begin
  Page := UninstallProgressForm.InnerPage;

  RemoveDataCheck := TNewCheckBox.Create(UninstallProgressForm);
  RemoveDataCheck.Parent := Page;
  RemoveDataCheck.Left := ScaleX(0);
  RemoveDataCheck.Top := ScaleY(76);
  RemoveDataCheck.Width := Page.Width;
  RemoveDataCheck.Height := ScaleY(17);
  RemoveDataCheck.Caption := 'Also delete my settings, saved graphs and models catalogue';
  RemoveDataCheck.Checked := False;

  Label1 := TNewStaticText.Create(UninstallProgressForm);
  Label1.Parent := Page;
  Label1.Left := ScaleX(16);
  Label1.Top := ScaleY(96);
  Label1.Width := Page.Width - ScaleX(16);
  Label1.Height := ScaleY(48);
  Label1.WordWrap := True;
  Label1.Caption :=
    'Leave this unticked and the engines are removed but your work stays, so reinstalling ' +
    'picks up where you left off. Model files themselves are never touched wherever they live.';

  UninstallProgressForm.Color := ClrWindow;
  UninstallProgressForm.Font.Color := ClrText;
  DarkenChildren(UninstallProgressForm);
  DarkTitleBar(UninstallProgressForm.Handle);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataFolder: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  if (RemoveDataCheck = nil) or (not RemoveDataCheck.Checked) then
    Exit;

  DataFolder := ExpandConstant('{#DataDir}');
  if DirExists(DataFolder) then
  begin
    Log('Removing the data folder at the user request: ' + DataFolder);
    DelTree(DataFolder, True, True, True);
  end;
end;
