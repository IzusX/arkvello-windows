// Integration checks against the actual GUI executables, on a private desktop.
// No desktop input is injected and no user windows are inspected or controlled.
#define UNICODE
#define _UNICODE
#define NOMINMAX
#include <windows.h>
#include <commctrl.h>
#include <gdiplus.h>
#include <string>
#include <vector>
#include <stdexcept>
#include <stdio.h>
#include "../CPP/7zip/UI/FileManager/resource.h"
#include "../CPP/7zip/UI/GUI/CompressDialogRes.h"

static HDESK g_Desktop;
static std::vector<HANDLE> g_Processes;
static std::wstring g_Output;
static void Check(bool condition, const char *message)
{
  if (!condition) throw std::runtime_error(message);
  printf("PASS %s\n", message); fflush(stdout);
}

static LRESULT Msg(HWND window, UINT message, WPARAM wp = 0, LPARAM lp = 0)
{
  DWORD_PTR result = 0;
  if (!SendMessageTimeoutW(window, message, wp, lp, SMTO_ABORTIFHUNG, 5000, &result))
    throw std::runtime_error("window message timed out");
  return (LRESULT)result;
}

static void SetText(HWND window, const std::wstring &text) { Msg(window, WM_SETTEXT, 0, (LPARAM)text.c_str()); }

static PROCESS_INFORMATION Launch(const std::wstring &exe, const std::wstring &args, const std::wstring &desktop)
{
  std::wstring command = L"\"" + exe + L"\" " + args;
  STARTUPINFOW startup = {}; startup.cb = sizeof(startup); startup.lpDesktop = (LPWSTR)desktop.c_str();
  startup.dwFlags = STARTF_USESHOWWINDOW; startup.wShowWindow = SW_SHOWNORMAL;
  PROCESS_INFORMATION process = {};
  Check(CreateProcessW(exe.c_str(), &command[0], NULL, NULL, FALSE, 0, NULL, NULL, &startup, &process) != FALSE, "launch GUI on private desktop");
  CloseHandle(process.hThread); g_Processes.push_back(process.hProcess);
  WaitForInputIdle(process.hProcess, 10000);
  return process;
}

struct CFind { DWORD Pid; int DialogControl; HWND Window; };
static BOOL CALLBACK FindTop(HWND window, LPARAM param)
{
  CFind &find = *(CFind *)param;
  DWORD pid = 0; GetWindowThreadProcessId(window, &pid);
  wchar_t name[80]; GetClassNameW(window, name, 80);
  if ((!find.Pid || pid == find.Pid) && IsWindowVisible(window)
      && (find.DialogControl ? GetDlgItem(window, find.DialogControl) != NULL : lstrcmpW(name, L"7-Zip::FM") == 0))
  { find.Window = window; return FALSE; }
  return TRUE;
}

static HWND WaitWindow(DWORD pid, int dialogControl)
{
  for (unsigned i = 0; i < 100; i++)
  {
    CFind find = {pid, dialogControl, NULL}; EnumDesktopWindows(g_Desktop, FindTop, (LPARAM)&find);
    if (find.Window) return find.Window;
    Sleep(100);
  }
  throw std::runtime_error("GUI window did not become ready");
}

static BOOL CALLBACK FindList(HWND window, LPARAM param)
{
  wchar_t name[80]; GetClassNameW(window, name, 80);
  if (lstrcmpW(name, WC_LISTVIEWW) == 0 && IsWindowVisible(window))
    ((std::vector<HWND> *)param)->push_back(window);
  return TRUE;
}

static BOOL CALLBACK FindPath(HWND window, LPARAM param)
{
  wchar_t name[80]; GetClassNameW(window, name, 80);
  if (lstrcmpW(name, WC_COMBOBOXEXW) == 0 && IsWindowVisible(window))
  { *(HWND *)param = window; return FALSE; }
  return TRUE;
}

static void FocusPath(HWND main)
{
  HWND combo = NULL; EnumChildWindows(main, FindPath, (LPARAM)&combo);
  Check(combo != NULL, "native editable path/history control remains available");
  HWND edit = (HWND)Msg(combo, CBEM_GETEDITCONTROL);
  const DWORD thread = GetWindowThreadProcessId(edit, NULL);
  MSG queued; PeekMessageW(&queued, NULL, 0,0, PM_NOREMOVE);
  Check(AttachThreadInput(GetCurrentThreadId(), thread, TRUE) != FALSE, "attach test input queues on private desktop");
  SetFocus(edit);
  GUITHREADINFO info = {}; info.cbSize = sizeof(info); GetGUIThreadInfo(thread, &info);
  AttachThreadInput(GetCurrentThreadId(), thread, FALSE);
  Check(info.hwndFocus == edit, "editable path gives typing focus to its real edit child");
}

static void Capture(HWND window, const wchar_t *name)
{
  RedrawWindow(window, NULL, NULL, RDW_UPDATENOW | RDW_ALLCHILDREN | RDW_INVALIDATE | RDW_FRAME | RDW_ERASE);
  Sleep(150);
  RECT r; GetWindowRect(window, &r);
  HDC dc = GetDC(window), memory = CreateCompatibleDC(dc);
  HBITMAP bitmap = CreateCompatibleBitmap(dc, r.right - r.left, r.bottom - r.top);
  HGDIOBJ old = SelectObject(memory, bitmap);
  const BOOL printed = PrintWindow(window, memory, 0);
  SelectObject(memory, old); DeleteDC(memory); ReleaseDC(window, dc);
  Check(printed != FALSE, "capture actual native window");
  Gdiplus::Bitmap image(bitmap, NULL);
  unsigned visiblePixels = 0;
  for (int y = 10; y < r.bottom - r.top; y += 40)
    for (int x = 10; x < r.right - r.left; x += 40)
    {
      Gdiplus::Color color; image.GetPixel(x, y, &color);
      if (color.GetR() || color.GetG() || color.GetB()) visiblePixels++;
    }
  Check(visiblePixels > 10, "native capture contains rendered pixels");
  CLSID encoder = {0x557cf406, 0x1a04, 0x11d3, {0x9a,0x73,0x00,0x00,0xf8,0x1e,0xf3,0x2e}};
  Check(image.Save((g_Output + L"\\" + name + L".png").c_str(), &encoder, NULL) == Gdiplus::Ok, "write native PNG evidence");
  DeleteObject(bitmap);
}

static void Theme(HWND window, unsigned preference)
{
  Msg(window, WM_COMMAND, IDM_ARKVELLO_THEME_SYSTEM + preference);
  Sleep(300);
}

class CRemote
{
  HANDLE Process;
public:
  void *Data;
  CRemote(HWND window, size_t size): Process(NULL), Data(NULL)
  {
    DWORD pid; GetWindowThreadProcessId(window, &pid);
    Process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, FALSE, pid);
    if (Process) Data = VirtualAllocEx(Process, NULL, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!Data) throw std::runtime_error("allocate GUI test message buffer");
  }
  ~CRemote() { if (Data) VirtualFreeEx(Process, Data, 0, MEM_RELEASE); if (Process) CloseHandle(Process); }
  void Write(const void *data, size_t size) { if (!WriteProcessMemory(Process, Data, data, size, NULL)) throw std::runtime_error("write GUI message buffer"); }
  void Read(void *data, size_t size) { if (!ReadProcessMemory(Process, Data, data, size, NULL)) throw std::runtime_error("read GUI message buffer"); }
};

static void SelectSource(HWND list)
{
  struct CFindBuffer { LVFINDINFOW Find; wchar_t Text[32]; } buffer = {};
  lstrcpyW(buffer.Text, L"source.txt");
  CRemote remote(list, sizeof(buffer));
  buffer.Find.flags = LVFI_STRING;
  buffer.Find.psz = (wchar_t *)((BYTE *)remote.Data + offsetof(CFindBuffer, Text));
  remote.Write(&buffer, sizeof(buffer));
  const LRESULT index = Msg(list, LVM_FINDITEMW, (WPARAM)-1, (LPARAM)remote.Data);
  Check(index >= 0, "file manager lists real fixture file");
  LVITEMW item = {}; item.stateMask = LVIS_SELECTED | LVIS_FOCUSED;
  remote.Write(&item, sizeof(item)); Msg(list, LVM_SETITEMSTATE, (WPARAM)-1, (LPARAM)remote.Data);
  item.state = LVIS_SELECTED | LVIS_FOCUSED;
  remote.Write(&item, sizeof(item)); Msg(list, LVM_SETITEMSTATE, (WPARAM)index, (LPARAM)remote.Data);
  RECT row = {LVIR_BOUNDS, 0,0,0}; remote.Write(&row, sizeof(row));
  Msg(list, LVM_GETITEMRECT, (WPARAM)index, (LPARAM)remote.Data); remote.Read(&row, sizeof(row));
  HDC dc = GetDC(list); const int dpi = GetDeviceCaps(dc, LOGPIXELSY); ReleaseDC(list, dc);
  printf("Report row: %ld px at %d DPI\n", row.bottom - row.top, dpi);
  Check(row.bottom - row.top >= MulDiv(24,dpi,96) && row.bottom - row.top <= MulDiv(32,dpi,96), "report rows retain compact desktop density");
}

static void CheckOptions(HWND dialog, DWORD pid, HWND main)
{
  PostMessageW(dialog, WM_COMMAND, IDB_COMPRESS_OPTIONS, 0);
  HWND options = WaitWindow(pid, IDX_COMPRESS_NT_SYM_LINKS);
  const int before = (int)Msg(GetDlgItem(options, IDX_COMPRESS_MTIME), BM_GETCHECK);
  HWND overrideBox = GetDlgItem(options, IDX_COMPRESS_MTIME_SET);
  if (Msg(overrideBox, BM_GETCHECK) != BST_UNCHECKED) Msg(overrideBox, BM_CLICK);
  Check(!IsWindowEnabled(GetDlgItem(options, IDX_COMPRESS_MTIME)), "time value disabled without explicit override");
  Check(Msg(GetDlgItem(options, IDX_COMPRESS_MTIME), BM_GETCHECK) == before, "disabled time value retains its stored state");
  Theme(main, 1); Capture(options, L"options-disabled-checked-light");
  Theme(main, 2); Capture(options, L"options-disabled-checked-dark");
  Msg(overrideBox, BM_CLICK);
  Check(IsWindowEnabled(GetDlgItem(options, IDX_COMPRESS_MTIME)) != FALSE, "explicit override enables its independent value");
  Theme(main, 1); Capture(options, L"options-light");
  Theme(main, 2); Capture(options, L"options-dark");
  Msg(options, WM_COMMAND, IDCANCEL);
  Check(IsWindowEnabled(dialog) != FALSE, "cancel nested options restores original modal owner");
}

static void SelectFormat(HWND dialog, const wchar_t *format)
{
  HWND combo = GetDlgItem(dialog, IDC_COMPRESS_FORMAT);
  LRESULT index = Msg(combo, CB_FINDSTRINGEXACT, (WPARAM)-1, (LPARAM)format);
  Check(index != CB_ERR, "original archive format is available");
  Msg(combo, CB_SETCURSEL, (WPARAM)index);
  Msg(dialog, WM_COMMAND, MAKEWPARAM(IDC_COMPRESS_FORMAT, CBN_SELCHANGE), (LPARAM)combo);
}

static void Configure(HWND dialog, const std::wstring &archive, bool encrypted)
{
  SetText(GetDlgItem(dialog, IDC_COMPRESS_ARCHIVE), archive);
  SetText(GetDlgItem(dialog, IDC_COMPRESS_VOLUME), L"");
  SetText(GetDlgItem(dialog, IDE_COMPRESS_PARAMETERS), L"");
  Msg(GetDlgItem(dialog, IDX_COMPRESS_DEL), BM_SETCHECK, BST_UNCHECKED);
  Msg(GetDlgItem(dialog, IDX_COMPRESS_SFX), BM_SETCHECK, BST_UNCHECKED);
  SetText(GetDlgItem(dialog, IDE_COMPRESS_PASSWORD1), encrypted ? L"arkvello-fixture" : L"");
  SetText(GetDlgItem(dialog, IDE_COMPRESS_PASSWORD2), encrypted ? L"arkvello-fixture" : L"");
  Msg(GetDlgItem(dialog, IDX_COMPRESS_ENCRYPT_FILE_NAMES), BM_SETCHECK, encrypted ? BST_CHECKED : BST_UNCHECKED);
}

static void CheckForm(HWND dialog)
{
  RECT client, button; GetClientRect(dialog, &client); GetWindowRect(GetDlgItem(dialog, IDOK), &button);
  MapWindowPoints(NULL, dialog, (POINT *)&button, 2);
  Check(button.bottom <= client.bottom && button.top >= 0, "dialog footer remains inside the client area");
  // WM_NEXTDLGCTL runs the real dialog manager's focus and scroll-into-view path.
  HWND last = GetDlgItem(dialog, IDX_COMPRESS_ENCRYPT_FILE_NAMES);
  Msg(dialog, WM_NEXTDLGCTL, (WPARAM)last, TRUE);
  RECT field; GetWindowRect(last, &field); MapWindowPoints(NULL, dialog, (POINT *)&field, 2);
  Check(field.top >= 0 && field.bottom <= button.top, "Tab target is scrolled above the footer");
}

int wmain(int argc, wchar_t **argv)
{
  if (argc != 3) { puts("usage: test-native-ui <binary-directory> <evidence-directory>"); return 2; }
  SetProcessDPIAware();
  g_Output = argv[2]; CreateDirectoryW(g_Output.c_str(), NULL);
  const std::wstring binaries = argv[1];
  const std::wstring desktopName = L"ArkvelloUITest-" + std::to_wstring(GetCurrentProcessId());
  g_Desktop = CreateDesktopW(desktopName.c_str(), NULL, NULL, 0, GENERIC_ALL, NULL);
  if (g_Desktop) SetThreadDesktop(g_Desktop);
  Gdiplus::GdiplusStartupInput gdiplus; ULONG_PTR token;
  Gdiplus::GdiplusStartup(&token, &gdiplus, NULL);
  DWORD previousTheme = 0, themeBytes = sizeof(previousTheme); bool hadTheme = false;
  HKEY key = NULL;
  if (RegCreateKeyExW(HKEY_CURRENT_USER, L"Software\\Arkvello\\Windows\\Appearance", 0, NULL, 0, KEY_QUERY_VALUE | KEY_SET_VALUE, NULL, &key, NULL) == ERROR_SUCCESS)
    hadTheme = RegQueryValueExW(key, L"Theme", NULL, NULL, (BYTE *)&previousTheme, &themeBytes) == ERROR_SUCCESS;
  int result = 0;
  try
  {
    Check(g_Desktop != NULL, "create private test desktop");
    const std::wstring fixtureFolder = g_Output + L"\\fixture";
    CreateDirectoryW(fixtureFolder.c_str(), NULL);
    CreateDirectoryW((fixtureFolder + L"\\Documents").c_str(), NULL);
    CreateDirectoryW((fixtureFolder + L"\\Projects").c_str(), NULL);
    const std::wstring fixture = fixtureFolder + L"\\source.txt";
    HANDLE file = CreateFileW(fixture.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
    const char payload[] = "Arkvello native dialog archive round trip\r\n";
    DWORD written; WriteFile(file, payload, sizeof(payload) - 1, &written, NULL); CloseHandle(file);
    const wchar_t *extras[] = {L"README.md", L"\u041e\u0442\u0447\u0451\u0442 \u0437\u0430 \u043d\u043e\u044f\u0431\u0440\u044c.txt", L"long filename with spaces for navigation and selection.txt"};
    for (const wchar_t *name : extras)
    {
      file = CreateFileW((fixtureFolder + L"\\" + name).c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
      WriteFile(file, payload, sizeof(payload) - 1, &written, NULL); CloseHandle(file);
    }
    PROCESS_INFORMATION fm = Launch(binaries + L"\\7zFM.exe", L"\"" + fixtureFolder + L"\"", desktopName);
    HWND main = WaitWindow(fm.dwProcessId, 0);
    HDC dc = GetDC(main); const int dpi = GetDeviceCaps(dc, LOGPIXELSY); ReleaseDC(main, dc);
    printf("Native test DPI: %d\n", dpi);
    SetWindowPos(main, NULL, 40,40,MulDiv(1180,dpi,96),MulDiv(740,dpi,96), SWP_NOZORDER | SWP_NOACTIVATE);
    std::vector<HWND> lists; EnumChildWindows(main, FindList, (LPARAM)&lists);
    if (lists.size() == 2)
    { Msg(main, WM_COMMAND, IDM_VIEW_TWO_PANELS); lists.clear(); EnumChildWindows(main, FindList, (LPARAM)&lists); }
    Check(lists.size() == 1, "file manager exposes the original one-panel mode");
    // Exercise real column sizing without saving it into upstream preferences.
    Msg(lists[0], LVM_SETCOLUMNWIDTH, 0, MulDiv(320,dpi,96));
    Msg(lists[0], LVM_SETCOLUMNWIDTH, 1, MulDiv(112,dpi,96));
    Msg(lists[0], LVM_SETCOLUMNWIDTH, 2, MulDiv(160,dpi,96));
    SelectSource(lists[0]);
    Theme(main, 1); Capture(main, L"main-light");
    Theme(main, 2); Capture(main, L"main-dark");
    FocusPath(main); Capture(main, L"main-path-focus-dark");
    const LRESULT itemCount = Msg(lists[0], LVM_GETITEMCOUNT);
    const unsigned modes[] = {IDM_VIEW_LARGE_ICONS, IDM_VIEW_SMALL_ICONS, IDM_VIEW_LIST, IDM_VIEW_DETAILS};
    for (unsigned mode : modes)
    { Msg(main, WM_COMMAND, mode); Check(Msg(lists[0], LVM_GETITEMCOUNT) == itemCount, "original list mode preserves real file items"); }
    Msg(main, WM_COMMAND, IDM_VIEW_TWO_PANELS);
    lists.clear(); EnumChildWindows(main, FindList, (LPARAM)&lists);
    Check(lists.size() == 2, "original two-panel command creates two visible native lists");
    Capture(main, L"main-two-panels-dark");
    Msg(main, WM_COMMAND, IDM_VIEW_TWO_PANELS);
    lists.clear(); EnumChildWindows(main, FindList, (LPARAM)&lists);
    Check(lists.size() == 1, "original two-panel command returns to one panel");
    SelectSource(lists[0]);
    PostMessageW(main, WM_COMMAND, 1070, 0); // original toolbar Add command
    HWND fmAdd = WaitWindow(0, IDC_COMPRESS_ARCHIVE);
    DWORD helperPid; GetWindowThreadProcessId(fmAdd, &helperPid);
    HANDLE helper = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE | SYNCHRONIZE, FALSE, helperPid);
    Check(helper != NULL && helperPid != fm.dwProcessId, "file manager Add starts the original separate GUI helper");
    g_Processes.push_back(helper);
    wchar_t helperPath[MAX_PATH]; DWORD helperLength = MAX_PATH;
    Check(QueryFullProcessImageNameW(helper, 0, helperPath, &helperLength) && _wcsicmp(helperPath, (binaries + L"\\7zG.exe").c_str()) == 0, "Add uses the sibling development 7zG executable");
    Msg(fmAdd, WM_COMMAND, IDCANCEL);
    Check(WaitForSingleObject(helper, 10000) == WAIT_OBJECT_0, "cancel Add returns from the real helper handshake");
    const std::wstring archive = g_Output + L"\\native-encrypted.7z";
    PROCESS_INFORMATION gui = Launch(binaries + L"\\7zG.exe", L"a -ad \"" + archive + L"\" \"" + fixture + L"\"", desktopName);
    HWND dialog = WaitWindow(gui.dwProcessId, IDC_COMPRESS_ARCHIVE);
    SelectFormat(dialog, L"zip");
    Check(!IsWindowEnabled(GetDlgItem(dialog, IDX_COMPRESS_ENCRYPT_FILE_NAMES)), "ZIP disables unsupported encrypted filenames");
    SelectFormat(dialog, L"7z");
    Check(IsWindowEnabled(GetDlgItem(dialog, IDX_COMPRESS_ENCRYPT_FILE_NAMES)) != FALSE, "7z restores encrypted filenames option");
    Configure(dialog, archive, true);
    Theme(main, 1); Capture(dialog, L"archive-light");
    Theme(main, 2); Capture(dialog, L"archive-dark");
    CheckOptions(dialog, gui.dwProcessId, main);
    RECT original; GetWindowRect(dialog, &original);
    SetWindowPos(dialog, NULL, 40,40,700,480, SWP_NOZORDER | SWP_NOACTIVATE);
    CheckForm(dialog); Capture(dialog, L"archive-constrained-dark");
    SetWindowPos(dialog, NULL, original.left, original.top, original.right - original.left, original.bottom - original.top, SWP_NOZORDER | SWP_NOACTIVATE);
    Msg(dialog, WM_VSCROLL, SB_TOP);
    Capture(dialog, L"archive-restored-dark");
    PostMessageW(dialog, WM_COMMAND, IDOK, 0);
    Check(WaitForSingleObject(gui.hProcess, 20000) == WAIT_OBJECT_0, "native Add to Archive operation finishes");
    DWORD exitCode; GetExitCodeProcess(gui.hProcess, &exitCode);
    Check(exitCode == 0 && GetFileAttributesW(archive.c_str()) != INVALID_FILE_ATTRIBUTES, "native dialog creates an encrypted 7z archive");
    const std::wstring zip = g_Output + L"\\native.zip";
    PROCESS_INFORMATION zipGui = Launch(binaries + L"\\7zG.exe", L"a -ad -tzip \"" + zip + L"\" \"" + fixture + L"\"", desktopName);
    HWND zipDialog = WaitWindow(zipGui.dwProcessId, IDC_COMPRESS_ARCHIVE);
    SelectFormat(zipDialog, L"zip"); Configure(zipDialog, zip, false);
    PostMessageW(zipDialog, WM_COMMAND, IDOK, 0);
    Check(WaitForSingleObject(zipGui.hProcess, 20000) == WAIT_OBJECT_0, "native ZIP operation finishes");
    GetExitCodeProcess(zipGui.hProcess, &exitCode);
    Check(exitCode == 0 && GetFileAttributesW(zip.c_str()) != INVALID_FILE_ATTRIBUTES, "native dialog creates ZIP archive");
    puts("Native UI integration checks passed. Archive content is checked by the PowerShell runner.");
  }
  catch (const std::exception &error) { fprintf(stderr, "FAIL %s\n", error.what()); result = 1; }
  // Only processes launched by this test are terminated. Avoid saving FM window
  // geometry/panel changes into the user's upstream 7-Zip preferences.
  for (HANDLE process : g_Processes)
  { if (WaitForSingleObject(process, 0) == WAIT_TIMEOUT) TerminateProcess(process, 0); CloseHandle(process); }
  if (key)
  {
    if (hadTheme) RegSetValueExW(key, L"Theme", 0, REG_DWORD, (BYTE *)&previousTheme, sizeof(previousTheme));
    else RegDeleteValueW(key, L"Theme");
    RegCloseKey(key);
  }
  if (g_Desktop) CloseDesktop(g_Desktop);
  Gdiplus::GdiplusShutdown(token);
  return result;
}
