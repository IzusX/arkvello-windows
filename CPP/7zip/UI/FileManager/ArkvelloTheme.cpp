// Arkvello Win32 presentation adapter. Does not replace control behavior.
#include "StdAfx.h"

#undef min
#undef max
#pragma warning(push, 0)
#include <commctrl.h>
#include <dwmapi.h>
#include <uxtheme.h>
#include <roapi.h>
#include <windows.ui.viewmanagement.h>
#include <oleacc.h>
#pragma warning(pop)
#include <vector>
#include <algorithm>
#include <math.h>

#include "ArkvelloTheme.h"
#include "App.h"
#include "resource.h"
#include "../GUI/CompressDialogRes.h"

#ifndef WM_DWMCOLORIZATIONCOLORCHANGED
#define WM_DWMCOLORIZATIONCOLORCHANGED 0x0320
#endif

namespace NArkvello {

static CColors g_Colors;
static bool g_Initialized = false;
static unsigned g_Preference = 0;
static HBRUSH g_Frame = NULL, g_Panel = NULL, g_Path = NULL;
static const UINT kChanged = RegisterWindowMessageW(L"Arkvello.AppearanceChanged.v1");
static const wchar_t kAppearanceKey[] = L"Software\\Arkvello\\Windows\\Appearance";

static COLORREF Mix(COLORREF a, COLORREF b, unsigned percent)
{
  return RGB((GetRValue(a) * (100 - percent) + GetRValue(b) * percent) / 100,
      (GetGValue(a) * (100 - percent) + GetGValue(b) * percent) / 100,
      (GetBValue(a) * (100 - percent) + GetBValue(b) * percent) / 100);
}

static double Linear(BYTE c)
{
  const double x = c / 255.0;
  return x <= 0.04045 ? x / 12.92 : pow((x + 0.055) / 1.055, 2.4);
}

static double Luminance(COLORREF c)
{
  return .2126 * Linear(GetRValue(c)) + .7152 * Linear(GetGValue(c)) + .0722 * Linear(GetBValue(c));
}

static double Contrast(COLORREF a, COLORREF b)
{
  const double x = Luminance(a), y = Luminance(b);
  return (std::max(x, y) + .05) / (std::min(x, y) + .05);
}

static bool ReadSystemColors(COLORREF &accent, bool &dark)
{
  // Documented WinRT API, using its ABI to keep the existing C++ toolchain.
  // Each refresh is balanced even when the caller already initialized COM.
  const HRESULT init = RoInitialize(RO_INIT_SINGLETHREADED);
  HSTRING name = NULL;
  IInspectable *instance = NULL;
  ABI::Windows::UI::ViewManagement::IUISettings3 *settings = NULL;
  bool ok = false;
  const wchar_t className[] = L"Windows.UI.ViewManagement.UISettings";
  if (SUCCEEDED(WindowsCreateString(className, (UINT32)(sizeof(className) / sizeof(wchar_t) - 1), &name)))
  {
    if (SUCCEEDED(RoActivateInstance(name, &instance)))
    {
      if (SUCCEEDED(instance->QueryInterface(IID_PPV_ARGS(&settings))))
      {
        ABI::Windows::UI::Color color, foreground;
        if (SUCCEEDED(settings->GetColorValue(ABI::Windows::UI::ViewManagement::UIColorType_Accent, &color))
            && SUCCEEDED(settings->GetColorValue(ABI::Windows::UI::ViewManagement::UIColorType_Foreground, &foreground)))
        {
          accent = RGB(color.R, color.G, color.B);
          dark = (5 * foreground.G + 2 * foreground.R + foreground.B) > 8 * 128;
          ok = true;
        }
        settings->Release();
      }
      instance->Release();
    }
    WindowsDeleteString(name);
  }
  if (SUCCEEDED(init))
    RoUninitialize();
  return ok;
}

static void Refresh()
{
  DWORD value = 0, bytes = sizeof(value);
  HKEY key;
  g_Preference = 0;
  if (RegOpenKeyExW(HKEY_CURRENT_USER, kAppearanceKey, 0, KEY_QUERY_VALUE, &key) == ERROR_SUCCESS)
  {
    DWORD type = 0;
    if (RegQueryValueExW(key, L"Theme", NULL, &type, (BYTE *)&value, &bytes) == ERROR_SUCCESS
        && type == REG_DWORD && value <= 2)
      g_Preference = value;
    RegCloseKey(key);
  }
  COLORREF accent = RGB(0, 95, 184);
  bool dark = false;
  if (!ReadSystemColors(accent, dark))
  {
    DWORD color;
    BOOL opaque;
    if (SUCCEEDED(DwmGetColorizationColor(&color, &opaque)))
      accent = RGB((color >> 16) & 255, (color >> 8) & 255, color & 255);
  }
  if (g_Preference)
    dark = g_Preference == 2;
  HIGHCONTRASTW hc = { sizeof(hc), 0, NULL };
  SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(hc), &hc, 0);
  CColors &p = g_Colors;
  p.Dark = dark;
  p.HighContrast = (hc.dwFlags & HCF_HIGHCONTRASTON) != 0;
  p.Frame = dark ? RGB(32,32,32) : RGB(243,243,243);
  p.Panel = dark ? RGB(25,25,25) : RGB(255,255,255);
  p.Hover = dark ? RGB(45,45,45) : RGB(234,234,234);
  p.Pressed = dark ? RGB(61,61,61) : RGB(223,223,223);
  p.Divider = dark ? RGB(56,56,56) : RGB(213,213,213);
  p.Edge = dark ? RGB(150,150,150) : RGB(138,138,138);
  p.Text = dark ? RGB(243,243,243) : RGB(26,26,26);
  p.Secondary = dark ? RGB(192,192,192) : RGB(92,92,92);
  p.Disabled = dark ? RGB(160,160,160) : RGB(109,109,109);
  p.Selection = dark ? RGB(65,65,65) : RGB(229,229,229);
  p.InactiveSelection = dark ? RGB(48,48,48) : RGB(238,238,238);
  p.Focus = dark ? RGB(176,176,176) : RGB(102,102,102);
  p.Error = dark ? RGB(255,180,171) : RGB(180,35,24);
  // Tint surfaces, rather than copy a potentially saturated accent as a fill.
  p.Navigation = Mix(p.Frame, accent, dark ? 14 : 5);
  p.Path = Mix(dark ? RGB(43,43,43) : RGB(231,231,231), accent, dark ? 12 : 5);
  p.Accent = accent;
  for (unsigned i = 0; i < 20 && Contrast(p.Accent, p.Frame) < 3.0; i++)
    p.Accent = Mix(p.Accent, dark ? RGB(255,255,255) : RGB(0,0,0), 10);
  p.OnAccent = Contrast(p.Accent, RGB(255,255,255)) >= Contrast(p.Accent, RGB(0,0,0))
      ? RGB(255,255,255) : RGB(0,0,0);
  if (p.HighContrast)
  {
    p.Frame = p.Navigation = GetSysColor(COLOR_BTNFACE);
    p.Panel = p.Path = GetSysColor(COLOR_WINDOW);
    p.Text = p.Secondary = GetSysColor(COLOR_WINDOWTEXT);
    p.Disabled = GetSysColor(COLOR_GRAYTEXT);
    p.Edge = p.Divider = p.Focus = GetSysColor(COLOR_WINDOWTEXT);
    p.Accent = p.Selection = GetSysColor(COLOR_HIGHLIGHT);
    p.OnAccent = GetSysColor(COLOR_HIGHLIGHTTEXT);
    p.Hover = p.Pressed = p.InactiveSelection = p.Selection;
    p.Error = p.Text;
  }
  if (g_Frame) DeleteObject(g_Frame);
  if (g_Panel) DeleteObject(g_Panel);
  if (g_Path) DeleteObject(g_Path);
  g_Frame = CreateSolidBrush(p.Frame);
  g_Panel = CreateSolidBrush(p.Panel);
  g_Path = CreateSolidBrush(p.Path);
  g_Initialized = true;
}

const CColors &Colors() { if (!g_Initialized) Refresh(); return g_Colors; }
unsigned Preference() { Colors(); return g_Preference; }

int Scale(HWND window, int dip)
{
  HDC dc = GetDC(window);
  const int dpi = dc ? GetDeviceCaps(dc, LOGPIXELSY) : 96;
  if (dc) ReleaseDC(window, dc);
  return MulDiv(dip, dpi, 96);
}

struct CFonts
{
  int Dpi;
  HFONT Normal;
  CFonts(int dpi): Dpi(dpi), Normal(NULL) {}
};
static std::vector<CFonts> g_Fonts;

HFONT Font(HWND window)
{
  const int dpi = Scale(window, 96);
  for (size_t i = 0; i < g_Fonts.size(); i++)
    if (g_Fonts[i].Dpi == dpi)
      return g_Fonts[i].Normal;
  CFonts f(dpi);
  f.Normal = CreateFontW(-MulDiv(14, dpi, 96), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
      DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
  g_Fonts.push_back(f);
  return f.Normal;
}

static void Fill(HDC dc, const RECT &r, COLORREF color)
{
  HBRUSH brush = CreateSolidBrush(color);
  FillRect(dc, &r, brush);
  DeleteObject(brush);
}

static void Outline(HDC dc, RECT r, COLORREF color, int width = 1)
{
  for (int i = 0; i < width; i++)
  {
    HBRUSH brush = CreateSolidBrush(color);
    FrameRect(dc, &r, brush);
    DeleteObject(brush);
    InflateRect(&r, -1, -1);
  }
}

void DrawCommand(HDC dc, const RECT &r, unsigned command, COLORREF color)
{
  // Original line geometry on a 20-unit grid; no font glyph dependency.
  const int size = std::min(r.right - r.left, r.bottom - r.top);
  const int x = r.left + (r.right - r.left - size) / 2;
  const int y = r.top + (r.bottom - r.top - size) / 2;
  const int saved = SaveDC(dc);
  SetMapMode(dc, MM_ANISOTROPIC);
  SetWindowExtEx(dc, 20, 20, NULL);
  SetViewportExtEx(dc, size, size, NULL);
  SetViewportOrgEx(dc, x, y, NULL);
  HPEN pen = CreatePen(PS_SOLID, 1, color);
  SelectObject(dc, pen);
  SelectObject(dc, GetStockObject(NULL_BRUSH));
  switch (command)
  {
    case kMenuCmdID_Toolbar_Add:
      Rectangle(dc, 3, 3, 17, 18);
      MoveToEx(dc, 7, 9, NULL); LineTo(dc, 13, 9);
      MoveToEx(dc, 10, 6, NULL); LineTo(dc, 10, 12); break;
    case kMenuCmdID_Toolbar_Extract:
      Rectangle(dc, 3, 11, 17, 18);
      MoveToEx(dc, 10, 2, NULL); LineTo(dc, 10, 13);
      MoveToEx(dc, 6, 9, NULL); LineTo(dc, 10, 13); LineTo(dc, 14, 9); break;
    case kMenuCmdID_Toolbar_Test:
      MoveToEx(dc, 10, 2, NULL); LineTo(dc, 17, 5); LineTo(dc, 16, 13);
      LineTo(dc, 10, 18); LineTo(dc, 4, 13); LineTo(dc, 3, 5); LineTo(dc, 10, 2);
      MoveToEx(dc, 6, 9, NULL); LineTo(dc, 9, 12); LineTo(dc, 14, 7); break;
    case IDM_COPY_TO:
      Rectangle(dc, 7, 2, 18, 14); Rectangle(dc, 2, 7, 13, 19); break;
    case IDM_MOVE_TO:
      Rectangle(dc, 2, 7, 12, 18);
      MoveToEx(dc, 7, 3, NULL); LineTo(dc, 17, 3); LineTo(dc, 17, 13);
      MoveToEx(dc, 12, 8, NULL); LineTo(dc, 17, 3); break;
    case IDM_DELETE:
      MoveToEx(dc, 3, 5, NULL); LineTo(dc, 17, 5);
      Rectangle(dc, 6, 2, 14, 5); Rectangle(dc, 5, 5, 15, 18);
      MoveToEx(dc, 8, 8, NULL); LineTo(dc, 8, 15);
      MoveToEx(dc, 12, 8, NULL); LineTo(dc, 12, 15); break;
    case IDM_PROPERTIES:
      Ellipse(dc, 2, 2, 19, 19);
      MoveToEx(dc, 10, 9, NULL); LineTo(dc, 10, 15);
      MoveToEx(dc, 10, 5, NULL); LineTo(dc, 10, 7); break;
    default: // Up
      MoveToEx(dc, 4, 9, NULL); LineTo(dc, 10, 3); LineTo(dc, 16, 9);
      MoveToEx(dc, 10, 3, NULL); LineTo(dc, 10, 18); break;
  }
  RestoreDC(dc, saved);
  DeleteObject(pen);
}

HBITMAP CommandBitmap(HWND window, unsigned command, int size)
{
  HDC dc = GetDC(window), mem = CreateCompatibleDC(dc);
  HBITMAP bitmap = CreateCompatibleBitmap(dc, size, size);
  HGDIOBJ old = SelectObject(mem, bitmap);
  RECT r = {0, 0, size, size};
  Fill(mem, r, RGB(255,0,255));
  DrawCommand(mem, r, command, Colors().Text);
  SelectObject(mem, old);
  DeleteDC(mem);
  ReleaseDC(window, dc);
  return bitmap;
}

struct CControlRect { HWND Window; int X, Y, Width, Height; };
struct CDialogLayout
{
  std::vector<CControlRect> Controls;
  int ContentHeight, Scroll;
  bool LayingOut;
  bool Compress;
  CDialogLayout(): ContentHeight(0), Scroll(0), LayingOut(false), Compress(false) {}
};
static const wchar_t kLayout[] = L"Arkvello.DialogLayout";

static bool IsFooter(HWND control)
{
  const int id = GetDlgCtrlID(control);
  return id == IDOK || id == IDCANCEL || id == IDHELP;
}

static void Arrange(HWND window)
{
  CDialogLayout *layout = (CDialogLayout *)GetPropW(window, kLayout);
  if (!layout || layout->LayingOut) return;
  layout->LayingOut = true;
  RECT client;
  GetClientRect(window, &client);
  const int footer = Scale(window, 64);
  const int available = std::max(0L, client.bottom - footer);
  int dipWidth = MulDiv(client.right, 96, Scale(window,96));
  bool stacked = layout->Compress && dipWidth < 880;
  const int contentHeight = layout->ContentHeight + (stacked ? 536 : 0);
  SCROLLINFO si = {sizeof(si), SIF_RANGE | SIF_PAGE | SIF_POS, 0,
      Scale(window, contentHeight) - 1, (UINT)available, layout->Scroll, 0};
  SetScrollInfo(window, SB_VERT, &si, TRUE);
  GetScrollInfo(window, SB_VERT, &si);
  layout->Scroll = si.nPos;
  GetClientRect(window, &client); // scrollbar can have changed the client width
  dipWidth = MulDiv(client.right, 96, Scale(window,96));
  const bool nowStacked = layout->Compress && dipWidth < 880;
  if (nowStacked != stacked)
  {
    stacked = nowStacked;
    si.fMask = SIF_RANGE | SIF_PAGE | SIF_POS;
    si.nMax = Scale(window, layout->ContentHeight + (stacked ? 536 : 0)) - 1;
    SetScrollInfo(window, SB_VERT, &si, TRUE);
    GetScrollInfo(window, SB_VERT, &si); layout->Scroll = si.nPos;
  }
  for (size_t i = 0; i < layout->Controls.size(); i++)
  {
    const CControlRect &c = layout->Controls[i];
    const bool right = stacked && c.X >= 460 && c.Y >= 80;
    const int top = Scale(window, c.Y + (right ? 536 : 0)) - layout->Scroll;
    const int height = Scale(window, c.Height);
    int x = c.X - (right ? 440 : 0), width = c.Width;
    const int id = GetDlgCtrlID(c.Window);
    if (layout->Compress && id == IDC_COMPRESS_ARCHIVE) width = dipWidth - 220;
    if (layout->Compress && id == IDB_COMPRESS_SET_ARCHIVE) x = dipWidth - 52;
    if (layout->Compress && id == IDT_COMPRESS_ARCHIVE_FOLDER) width = dipWidth - 180;
    width = std::min(width, dipWidth - x - 20);
    MoveWindow(c.Window, Scale(window, x), top, Scale(window, std::max(width,20)), height, TRUE);
    RECT r;
    GetWindowRect(c.Window, &r);
    const int actualHeight = r.bottom - r.top; // combo height includes its popup in MoveWindow
    HRGN clip = CreateRectRgn(0, std::max(0, -top), Scale(window, std::max(width,20)),
        std::max(0, std::min(actualHeight, available - top)));
    if (!SetWindowRgn(c.Window, clip, TRUE)) DeleteObject(clip);
  }
  const int ids[] = {IDHELP, IDCANCEL, IDOK};
  for (unsigned i = 0; i < 3; i++)
    SetWindowPos(GetDlgItem(window, ids[i]), HWND_TOP,
        client.right - Scale(window, 20 + 96 * (int)(i + 1) - 8),
        client.bottom - Scale(window, 46), Scale(window, 88), Scale(window, 28), SWP_SHOWWINDOW);
  layout->LayingOut = false;
  InvalidateRect(window, NULL, TRUE);
}

static void Place(HWND window, int id, int x, int y, int width, int height)
{
  HWND control = GetDlgItem(window, id);
  if (!control) return;
  CControlRect c = {control, x, y, width, height};
  ((CDialogLayout *)GetPropW(window, kLayout))->Controls.push_back(c);
}

static void SizeDialog(HWND window, int width, int height)
{
  MONITORINFO mi = {sizeof(mi), {0,0,0,0}, {0,0,0,0}, 0};
  GetMonitorInfoW(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), &mi);
  RECT r = {0, 0, Scale(window, width), Scale(window, height)};
  AdjustWindowRectEx(&r, (DWORD)GetWindowLongPtrW(window, GWL_STYLE), FALSE,
      (DWORD)GetWindowLongPtrW(window, GWL_EXSTYLE));
  const int w = std::min(r.right - r.left, mi.rcWork.right - mi.rcWork.left);
  const int h = std::min(r.bottom - r.top, mi.rcWork.bottom - mi.rcWork.top);
  SetWindowPos(window, NULL, mi.rcWork.left + (mi.rcWork.right - mi.rcWork.left - w) / 2,
      mi.rcWork.top + (mi.rcWork.bottom - mi.rcWork.top - h) / 2, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
  Arrange(window);
}

void LayoutCompressDialog(HWND window)
{
  CDialogLayout *layout = new CDialogLayout;
  layout->ContentHeight = 600;
  layout->Compress = true;
  SetPropW(window, kLayout, layout);
  Place(window, IDT_COMPRESS_ARCHIVE_FOLDER, 160, 12, 720, 20);
  Place(window, IDT_COMPRESS_ARCHIVE, 20, 42, 132, 22);
  Place(window, IDC_COMPRESS_ARCHIVE, 160, 36, 680, 300);
  Place(window, IDB_COMPRESS_SET_ARCHIVE, 848, 36, 32, 28);
  const int labels[] = {IDT_COMPRESS_FORMAT, IDT_COMPRESS_LEVEL, IDT_COMPRESS_METHOD,
      IDT_COMPRESS_DICTIONARY, IDT_COMPRESS_ORDER, IDT_COMPRESS_SOLID, IDT_COMPRESS_THREADS};
  const int controls[] = {IDC_COMPRESS_FORMAT, IDC_COMPRESS_LEVEL, IDC_COMPRESS_METHOD,
      IDC_COMPRESS_DICTIONARY, IDC_COMPRESS_ORDER, IDC_COMPRESS_SOLID, IDC_COMPRESS_THREADS};
  for (unsigned i = 0; i < 7; i++)
  {
    Place(window, labels[i], 20, 84 + 34 * (int)i, 218, 26);
    Place(window, controls[i], 246, 80 + 34 * (int)i, i == 6 ? 114 : 178, 280);
  }
  Place(window, IDT_COMPRESS_HARDWARE_THREADS, 364, 288, 60, 22);
  Place(window, IDT_COMPRESS_MEMORY, 20, 324, 290, 22);
  Place(window, IDT_COMPRESS_MEMORY_VALUE, 20, 346, 290, 22);
  Place(window, IDC_COMPRESS_MEM_USE, 324, 330, 100, 280);
  Place(window, IDT_COMPRESS_MEMORY_DE, 20, 380, 304, 22);
  Place(window, IDT_COMPRESS_MEMORY_DE_VALUE, 324, 380, 100, 22);
  Place(window, IDT_SPLIT_TO_VOLUMES, 20, 420, 404, 22);
  Place(window, IDC_COMPRESS_VOLUME, 20, 444, 404, 200);
  Place(window, IDT_COMPRESS_PARAMETERS, 20, 486, 404, 22);
  Place(window, IDE_COMPRESS_PARAMETERS, 20, 510, 404, 28);
  Place(window, IDB_COMPRESS_OPTIONS, 20, 554, 100, 28);
  Place(window, IDT_COMPRESS_OPTIONS, 132, 552, 292, 40);
  Place(window, IDT_COMPRESS_UPDATE_MODE, 460, 84, 132, 26);
  Place(window, IDC_COMPRESS_UPDATE_MODE, 600, 80, 280, 220);
  Place(window, IDT_COMPRESS_PATH_MODE, 460, 118, 132, 26);
  Place(window, IDC_COMPRESS_PATH_MODE, 600, 114, 280, 220);
  Place(window, IDG_COMPRESS_OPTIONS, 460, 162, 420, 26);
  Place(window, IDX_COMPRESS_SFX, 460, 196, 420, 26);
  Place(window, IDX_COMPRESS_SHARED, 460, 226, 420, 26);
  Place(window, IDX_COMPRESS_DEL, 460, 256, 420, 26);
  Place(window, IDG_COMPRESS_ENCRYPTION, 460, 304, 420, 26);
  Place(window, IDT_PASSWORD_ENTER, 460, 340, 420, 22);
  Place(window, IDE_COMPRESS_PASSWORD1, 460, 364, 420, 28);
  Place(window, IDT_PASSWORD_REENTER, 460, 402, 420, 22);
  Place(window, IDE_COMPRESS_PASSWORD2, 460, 426, 420, 28);
  Place(window, IDX_PASSWORD_SHOW, 460, 466, 420, 26);
  Place(window, IDT_COMPRESS_ENCRYPTION_METHOD, 460, 512, 222, 26);
  Place(window, IDC_COMPRESS_ENCRYPTION_METHOD, 690, 508, 190, 240);
  Place(window, IDX_COMPRESS_ENCRYPT_FILE_NAMES, 460, 548, 420, 26);
  SizeDialog(window, 900, 664);
}

void LayoutCompressOptions(HWND window)
{
  CDialogLayout *layout = new CDialogLayout;
  layout->ContentHeight = 482;
  SetPropW(window, kLayout, layout);
  Place(window, IDG_COMPRESS_NTFS, 20, 16, 560, 26);
  const int ntfs[] = {IDX_COMPRESS_NT_SYM_LINKS, IDX_COMPRESS_NT_HARD_LINKS,
      IDX_COMPRESS_NT_ALT_STREAMS, IDX_COMPRESS_NT_SECUR};
  for (unsigned i = 0; i < 4; i++) Place(window, ntfs[i], 20, 48 + 30 * (int)i, 560, 26);
  Place(window, IDT_COMPRESS_TIME_INFO, 20, 174, 560, 22);
  Place(window, IDG_COMPRESS_TIME, 20, 204, 560, 26);
  Place(window, IDX_COMPRESS_PREC_SET, 20, 240, 26, 26);
  Place(window, IDT_COMPRESS_TIME_PREC, 54, 244, 310, 22);
  Place(window, IDC_COMPRESS_TIME_PREC, 372, 240, 208, 220);
  const int overrides[] = {IDX_COMPRESS_MTIME_SET, IDX_COMPRESS_CTIME_SET,
      IDX_COMPRESS_ATIME_SET, IDX_COMPRESS_ZTIME_SET};
  const int values[] = {IDX_COMPRESS_MTIME, IDX_COMPRESS_CTIME, IDX_COMPRESS_ATIME, IDX_COMPRESS_ZTIME};
  for (unsigned i = 0; i < 4; i++)
  {
    Place(window, overrides[i], 20, 278 + 34 * (int)i, 26, 28);
    Place(window, values[i], 54, 278 + 34 * (int)i, 526, 28);
  }
  Place(window, IDX_COMPRESS_PRESERVE_ATIME, 20, 430, 560, 42);
  const bool hasNtfs = (GetWindowLongPtrW(GetDlgItem(window, IDG_COMPRESS_NTFS), GWL_STYLE) & WS_VISIBLE) != 0;
  if (!hasNtfs)
  {
    layout->ContentHeight -= 160;
    for (size_t i = 0; i < layout->Controls.size(); i++)
      if (layout->Controls[i].Y >= 174) layout->Controls[i].Y -= 160;
  }
  SizeDialog(window, 600, hasNtfs ? 546 : 386);
}

static bool ClassIs(HWND window, const wchar_t *name)
{
  wchar_t text[80] = {};
  GetClassNameW(window, text, 80);
  return lstrcmpiW(text, name) == 0;
}

static bool IsPath(HWND window)
{
  HWND p = window;
  for (unsigned i = 0; p && i < 4; i++, p = GetParent(p))
    if (ClassIs(p, WC_COMBOBOXEXW)) return true;
  return false;
}

static void PaintButton(HWND window, HDC dc)
{
  const CColors &p = Colors();
  RECT r; GetClientRect(window, &r);
  const LONG_PTR style = GetWindowLongPtrW(window, GWL_STYLE);
  const unsigned type = (unsigned)style & BS_TYPEMASK;
  const bool enabled = IsWindowEnabled(window) != FALSE;
  const UINT state = (UINT)SendMessageW(window, BM_GETSTATE, 0, 0);
  const bool focused = GetFocus() == window;
  const bool hot = GetPropW(window, L"Arkvello.Hot") != NULL;
  const bool check = type == BS_CHECKBOX || type == BS_AUTOCHECKBOX || type == BS_3STATE || type == BS_AUTO3STATE;
  const bool radio = type == BS_RADIOBUTTON || type == BS_AUTORADIOBUTTON;
  const bool group = type == BS_GROUPBOX;
  const bool primary = type == BS_DEFPUSHBUTTON && enabled;
  Fill(dc, r, (check || radio || group) ? p.Frame : primary ? p.Accent :
      (state & BST_PUSHED) ? p.Pressed : hot ? p.Hover : p.Panel);
  SetBkMode(dc, TRANSPARENT);
  SelectObject(dc, Font(window));
  SetTextColor(dc, !enabled ? p.Disabled : primary ? p.OnAccent : p.Text);
  wchar_t text[1024]; GetWindowTextW(window, text, 1024);
  UINT flags = DT_VCENTER | DT_SINGLELINE;
  if (SendMessageW(window, WM_QUERYUISTATE, 0, 0) & UISF_HIDEACCEL) flags |= DT_HIDEPREFIX;
  if (check || radio)
  {
    RECT box = {0, (r.bottom - Scale(window,16)) / 2, Scale(window,16), (r.bottom + Scale(window,16)) / 2};
    Fill(dc, box, enabled && (state & 3) ? p.Accent : p.Panel);
    Outline(dc, box, enabled ? p.Edge : p.Disabled);
    if (state & 3)
    {
      const int saved = SaveDC(dc);
      HPEN pen = CreatePen(PS_SOLID, Scale(window,2), enabled ? p.OnAccent : p.Disabled);
      SelectObject(dc, pen);
      const int x = box.left, y = box.top;
      if ((state & 3) == BST_INDETERMINATE || radio)
      { MoveToEx(dc, x + Scale(window,4), y + Scale(window,8), NULL); LineTo(dc, x + Scale(window,12), y + Scale(window,8)); }
      else
      { MoveToEx(dc, x + Scale(window,3), y + Scale(window,8), NULL); LineTo(dc, x + Scale(window,7), y + Scale(window,12)); LineTo(dc, x + Scale(window,13), y + Scale(window,4)); }
      RestoreDC(dc, saved); DeleteObject(pen);
    }
    r.left += Scale(window,24);
    flags = (flags & ~DT_SINGLELINE) | DT_WORDBREAK;
  }
  else if (!group)
  {
    Outline(dc, r, primary ? p.Accent : p.Edge);
    flags |= DT_CENTER;
    InflateRect(&r, -Scale(window,6), 0);
  }
  DrawTextW(dc, text, -1, &r, flags);
  if (focused && !(SendMessageW(window, WM_QUERYUISTATE, 0, 0) & UISF_HIDEFOCUS))
  { GetClientRect(window, &r); InflateRect(&r, -Scale(window,3), -Scale(window,3)); Outline(dc, r, primary ? p.OnAccent : p.Accent, Scale(window,2)); }
}

static LRESULT CALLBACK ControlProc(HWND, UINT, WPARAM, LPARAM, UINT_PTR, DWORD_PTR);
static LRESULT CALLBACK RootProc(HWND, UINT, WPARAM, LPARAM, UINT_PTR, DWORD_PTR);

static BOOL CALLBACK ApplyChild(HWND window, LPARAM)
{
  const CColors &p = Colors();
  const bool list = ClassIs(window, WC_LISTVIEWW);
  SendMessageW(window, WM_SETFONT, (WPARAM)Font(window), FALSE);
  SetWindowSubclass(window, ControlProc, 1, 0);
  if (list)
  {
    SetWindowTheme(window, p.HighContrast ? NULL : L"", NULL);
    ListView_SetBkColor(window, p.Panel);
    ListView_SetTextBkColor(window, p.Panel);
    ListView_SetTextColor(window, p.Text);
    SetWindowLongPtrW(window, GWL_EXSTYLE, GetWindowLongPtrW(window, GWL_EXSTYLE) & ~WS_EX_CLIENTEDGE);
    SetWindowPos(window, NULL, 0,0,0,0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE);
  }
  else if (ClassIs(window, WC_HEADERW) || ClassIs(window, L"Button") || ClassIs(window, L"ComboBox")
      || ClassIs(window, L"Edit") || ClassIs(window, TOOLBARCLASSNAMEW) || ClassIs(window, STATUSCLASSNAMEW))
    SetWindowTheme(window, p.HighContrast ? NULL : L"", NULL);
  if (ClassIs(window, L"ComboBox"))
  {
    SendMessageW(window, CB_SETITEMHEIGHT, (WPARAM)-1, Scale(window, IsPath(window) ? 26 : 22));
    COMBOBOXINFO info = {sizeof(info), {0,0,0,0}, {0,0,0,0}, 0, NULL, NULL, NULL};
    if (GetComboBoxInfo(window, &info) && info.hwndList)
    {
      SetWindowTheme(info.hwndList, p.HighContrast ? NULL : L"", NULL);
      SendMessageW(info.hwndList, WM_SETFONT, (WPARAM)Font(window), FALSE);
    }
  }
  if (ClassIs(window, L"Edit") && ClassIs(GetParent(window), L"#32770"))
  {
    SetWindowLongPtrW(window, GWL_EXSTYLE, GetWindowLongPtrW(window, GWL_EXSTYLE) & ~WS_EX_CLIENTEDGE);
    SetWindowLongPtrW(window, GWL_STYLE, GetWindowLongPtrW(window, GWL_STYLE) | WS_BORDER);
    SendMessageW(window, EM_SETMARGINS, EC_LEFTMARGIN | EC_RIGHTMARGIN, MAKELPARAM(Scale(window,8), Scale(window,8)));
    SetWindowPos(window, NULL, 0,0,0,0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE);
  }
  if ((ClassIs(window, L"Edit") && (ClassIs(GetParent(window), L"ComboBox") || ClassIs(GetParent(window), WC_COMBOBOXEXW)))
      || ClassIs(window, WC_COMBOBOXEXW))
  {
    // The outer combo draws the boundary; an inner edit must not add a second
    // classic white edge. Native caret, selection and edit behavior are kept.
    SetWindowLongPtrW(window, GWL_EXSTYLE, GetWindowLongPtrW(window, GWL_EXSTYLE) & ~WS_EX_CLIENTEDGE);
    SetWindowLongPtrW(window, GWL_STYLE, GetWindowLongPtrW(window, GWL_STYLE) & ~WS_BORDER);
    SetWindowPos(window, NULL, 0,0,0,0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE);
  }
  if (ClassIs(window, STATUSCLASSNAMEW)) SendMessageW(window, SB_SETMINHEIGHT, Scale(window,26), 0);
  if (ClassIs(window, REBARCLASSNAMEW))
  { SendMessageW(window, RB_SETBKCOLOR, 0, p.Navigation); SendMessageW(window, RB_SETTEXTCOLOR, 0, p.Text); }
  if (ClassIs(window, TOOLTIPS_CLASSW))
  { SetWindowTheme(window, L"", NULL); SendMessageW(window, TTM_SETTIPBKCOLOR, p.Panel, 0); SendMessageW(window, TTM_SETTIPTEXTCOLOR, p.Text, 0); }
  return TRUE;
}

struct CMenuLabels
{
  std::vector<MSAAMENUINFO *> Items;
  ~CMenuLabels()
  {
    for (size_t i = 0; i < Items.size(); i++)
    { delete [] Items[i]->pszWText; delete Items[i]; }
  }
};
static const wchar_t kMenuLabels[] = L"Arkvello.AccessibleMenuLabels";

static const wchar_t *MenuLabel(HWND window, ULONG_PTR data)
{
  CMenuLabels *labels = (CMenuLabels *)GetPropW(window, kMenuLabels);
  if (labels)
    for (size_t i = 0; i < labels->Items.size(); i++)
      if ((ULONG_PTR)labels->Items[i] == data) return labels->Items[i]->pszWText;
  return NULL;
}

void StyleMenu(HWND window)
{
  DWORD_PTR data;
  if (!GetWindowSubclass(window, RootProc, 2, &data)) return;
  HMENU menu = GetMenu(window);
  if (!menu) return;
  CMenuLabels *previous = (CMenuLabels *)GetPropW(window, kMenuLabels);
  CMenuLabels *labels = new CMenuLabels;
  SetPropW(window, kMenuLabels, labels);
  MENUINFO info = {sizeof(info), MIM_BACKGROUND, 0, 0, g_Frame, 0, 0};
  SetMenuInfo(menu, &info);
  for (int i = 0; i < GetMenuItemCount(menu); i++)
  {
    MENUITEMINFOW item = {};
    item.cbSize = sizeof(item); item.fMask = MIIM_FTYPE | MIIM_DATA;
    GetMenuItemInfoW(menu, (UINT)i, TRUE, &item);
    item.fType = Colors().HighContrast ? item.fType & ~MFT_OWNERDRAW : item.fType | MFT_OWNERDRAW;
    item.dwItemData = 0;
    if (!Colors().HighContrast)
    {
      MSAAMENUINFO *label = new MSAAMENUINFO;
      label->dwMSAASignature = MSAA_MENU_SIG;
      label->pszWText = new wchar_t[256];
      label->cchWText = (DWORD)GetMenuStringW(menu, (UINT)i, label->pszWText, 256, MF_BYPOSITION);
      labels->Items.push_back(label);
      item.dwItemData = (ULONG_PTR)label;
    }
    SetMenuItemInfoW(menu, (UINT)i, TRUE, &item);
  }
  delete previous;
  DrawMenuBar(window);
}

void Apply(HWND window)
{
  Colors();
  const CColors &p = g_Colors;
  const BOOL dark = p.Dark && !p.HighContrast;
  DwmSetWindowAttribute(window, (DWMWINDOWATTRIBUTE)20, &dark, sizeof(dark));
  // Windows 11 caption color is supplementary; older systems reject it safely.
  const COLORREF caption = p.HighContrast ? 0xffffffff : p.Navigation;
  const COLORREF text = p.HighContrast ? 0xffffffff : p.Text;
  DwmSetWindowAttribute(window, (DWMWINDOWATTRIBUTE)35, &caption, sizeof(caption));
  DwmSetWindowAttribute(window, (DWMWINDOWATTRIBUTE)36, &text, sizeof(text));
  if (ClassIs(window, L"7-Zip::Panel")) ApplyChild(window, 0);
  EnumChildWindows(window, ApplyChild, 0);
  StyleMenu(window);
  RedrawWindow(window, NULL, NULL, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_FRAME);
}

static bool PaintNotify(HWND window, NMHDR *header, LRESULT &result)
{
  if (header->code == NM_CUSTOMDRAW && ClassIs(header->hwndFrom, TOOLBARCLASSNAMEW))
  {
    NMTBCUSTOMDRAW *draw = (NMTBCUSTOMDRAW *)header;
    if (draw->nmcd.dwDrawStage == CDDS_PREPAINT) { result = CDRF_NOTIFYITEMDRAW; return true; }
    if (draw->nmcd.dwDrawStage == CDDS_ITEMPREPAINT)
    {
      const CColors &p = Colors();
      const bool hot = (draw->nmcd.uItemState & CDIS_HOT) != 0;
      const bool pressed = (draw->nmcd.uItemState & CDIS_SELECTED) != 0;
      RECT r = draw->nmcd.rc;
      Fill(draw->nmcd.hdc, r, pressed ? p.Pressed : hot ? p.Hover : p.Frame);
      TBBUTTONINFO tb = {}; tb.cbSize = sizeof(tb); tb.dwMask = TBIF_TEXT;
      wchar_t text[512] = {}; tb.pszText = text; tb.cchText = 512;
      SendMessageW(header->hwndFrom, TB_GETBUTTONINFOW, draw->nmcd.dwItemSpec, (LPARAM)&tb);
      const COLORREF color = (draw->nmcd.uItemState & CDIS_DISABLED) ? p.Disabled : p.Text;
      int glyph = Scale(window,20), imageHeight = glyph;
      HIMAGELIST images = (HIMAGELIST)SendMessageW(header->hwndFrom, TB_GETIMAGELIST, 0, 0);
      if (images) ImageList_GetIconSize(images, &glyph, &imageHeight);
      const unsigned command = (unsigned)draw->nmcd.dwItemSpec;
      if (command != kMenuCmdID_Toolbar_Add && command != kMenuCmdID_Toolbar_Extract
          && command != kMenuCmdID_Toolbar_Test && command != IDM_COPY_TO && command != IDM_MOVE_TO
          && command != IDM_DELETE && command != IDM_PROPERTIES)
        glyph = Scale(window,20);
      RECT icon = {r.left + Scale(window,10), r.top + (r.bottom - r.top - glyph) / 2, 0, 0};
      icon.right = icon.left + glyph; icon.bottom = icon.top + glyph;
      DrawCommand(draw->nmcd.hdc, icon, (unsigned)draw->nmcd.dwItemSpec, color);
      r.left = icon.right + Scale(window,9); r.right -= Scale(window,10);
      SetBkMode(draw->nmcd.hdc, TRANSPARENT); SetTextColor(draw->nmcd.hdc, color);
      SelectObject(draw->nmcd.hdc, Font(window));
      DrawTextW(draw->nmcd.hdc, text, -1, &r, DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
      if (draw->nmcd.uItemState & CDIS_FOCUS) Outline(draw->nmcd.hdc, draw->nmcd.rc, p.Accent, Scale(window,2));
      result = CDRF_SKIPDEFAULT; return true;
    }
  }
  if (header->code == NM_CUSTOMDRAW && ClassIs(header->hwndFrom, WC_HEADERW))
  {
    NMCUSTOMDRAW *draw = (NMCUSTOMDRAW *)header;
    if (draw->dwDrawStage == CDDS_PREPAINT)
    { RECT r; GetClientRect(header->hwndFrom, &r); Fill(draw->hdc, r, Colors().Panel); result = CDRF_NOTIFYITEMDRAW; return true; }
    if (draw->dwDrawStage == CDDS_ITEMPREPAINT)
    {
      const CColors &p = Colors();
      RECT r = draw->rc; Fill(draw->hdc, r, p.Panel);
      wchar_t text[512] = {};
      HDITEMW item = {}; item.mask = HDI_TEXT | HDI_FORMAT; item.pszText = text; item.cchTextMax = 512;
      SendMessageW(header->hwndFrom, HDM_GETITEMW, draw->dwItemSpec, (LPARAM)&item);
      r.left += Scale(window,12); r.right -= Scale(window,12);
      SetBkMode(draw->hdc, TRANSPARENT); SetTextColor(draw->hdc, p.Secondary); SelectObject(draw->hdc, Font(window));
      DrawTextW(draw->hdc, text, -1, &r, DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS |
          ((item.fmt & HDF_RIGHT) ? DT_RIGHT : DT_LEFT));
      RECT line = draw->rc; line.left = line.right - 1; Fill(draw->hdc, line, p.Divider);
      if (item.fmt & (HDF_SORTUP | HDF_SORTDOWN))
      {
        HPEN pen = CreatePen(PS_SOLID, 1, p.Secondary); HGDIOBJ old = SelectObject(draw->hdc, pen);
        const int x = (r.left + r.right) / 2, y = r.top + Scale(window,3);
        MoveToEx(draw->hdc, x - Scale(window,3), y + ((item.fmt & HDF_SORTUP) ? Scale(window,3) : 0), NULL);
        LineTo(draw->hdc, x, y + ((item.fmt & HDF_SORTUP) ? 0 : Scale(window,3)));
        LineTo(draw->hdc, x + Scale(window,3), y + ((item.fmt & HDF_SORTUP) ? Scale(window,3) : 0));
        SelectObject(draw->hdc, old); DeleteObject(pen);
      }
      result = CDRF_SKIPDEFAULT; return true;
    }
  }
  return false;
}

static LRESULT CALLBACK ControlProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR, DWORD_PTR)
{
  const CColors &p = Colors();
  if (message >= WM_CTLCOLORMSGBOX && message <= WM_CTLCOLORSTATIC)
  {
    HDC dc = (HDC)wParam;
    const bool path = IsPath((HWND)lParam);
    const bool input = message == WM_CTLCOLOREDIT || message == WM_CTLCOLORLISTBOX || ClassIs((HWND)lParam, L"Edit");
    SetTextColor(dc, IsWindowEnabled((HWND)lParam) ? p.Text : p.Disabled);
    SetBkColor(dc, path ? p.Path : input ? p.Panel : p.Frame);
    return (LRESULT)(path ? g_Path : input ? g_Panel : g_Frame);
  }
  switch (message)
  {
    case WM_ERASEBKGND:
      if (ClassIs(window, TOOLBARCLASSNAMEW) || ClassIs(window, REBARCLASSNAMEW) || ClassIs(window, L"7-Zip::Panel") || ClassIs(window, WC_HEADERW))
      { RECT r; GetClientRect(window, &r); Fill((HDC)wParam, r, ClassIs(window, WC_HEADERW) ? p.Panel : ClassIs(window, TOOLBARCLASSNAMEW) ? p.Frame : p.Navigation); return 1; }
      break;
    case HDM_LAYOUT:
      if (ClassIs(window, WC_HEADERW))
      {
        const LRESULT result = DefSubclassProc(window, message, wParam, lParam);
        HDLAYOUT *header = (HDLAYOUT *)lParam;
        if (result && header && header->pwpos && header->prc)
        { const int height = Scale(window,32); header->prc->top += height - header->pwpos->cy; header->pwpos->cy = height; }
        return result;
      }
      break;
    case WM_NOTIFY:
    {
      LRESULT result;
      if (!p.HighContrast && PaintNotify(window, (NMHDR *)lParam, result)) return result;
      break;
    }
    case WM_MOUSEMOVE:
      if (ClassIs(window, L"Button") && !GetPropW(window, L"Arkvello.Hot"))
      {
        SetPropW(window, L"Arkvello.Hot", (HANDLE)1);
        TRACKMOUSEEVENT track = {sizeof(track), TME_LEAVE, window, 0}; TrackMouseEvent(&track);
        InvalidateRect(window, NULL, FALSE);
      }
      break;
    case WM_MOUSELEAVE:
      RemovePropW(window, L"Arkvello.Hot"); InvalidateRect(window, NULL, FALSE); break;
    case WM_PAINT:
    case WM_PRINTCLIENT:
      if (!p.HighContrast && ClassIs(window, WC_HEADERW))
      {
        PAINTSTRUCT ps = {}; HDC dc = message == WM_PAINT ? BeginPaint(window, &ps) : (HDC)wParam;
        RECT r; GetClientRect(window, &r); Fill(dc, r, p.Panel);
        const int count = Header_GetItemCount(window);
        for (int i = 0; i < count; i++)
        {
          NMCUSTOMDRAW draw = {}; draw.hdr.hwndFrom = window; draw.hdr.code = NM_CUSTOMDRAW;
          draw.dwDrawStage = CDDS_ITEMPREPAINT; draw.dwItemSpec = (DWORD_PTR)i; draw.hdc = dc;
          Header_GetItemRect(window, i, &draw.rc);
          LRESULT ignored; PaintNotify(window, &draw.hdr, ignored);
        }
        if (message == WM_PAINT) EndPaint(window, &ps);
        return 0;
      }
      if (!p.HighContrast && ClassIs(window, L"Button"))
      {
        PAINTSTRUCT ps = {}; HDC dc = message == WM_PAINT ? BeginPaint(window, &ps) : (HDC)wParam;
        PaintButton(window, dc);
        if (message == WM_PAINT) EndPaint(window, &ps);
        return 0;
      }
      if (!p.HighContrast && ClassIs(window, STATUSCLASSNAMEW))
      {
        PAINTSTRUCT ps = {}; HDC dc = message == WM_PAINT ? BeginPaint(window, &ps) : (HDC)wParam;
        RECT r; GetClientRect(window, &r); Fill(dc, r, p.Frame);
        const int count = (int)SendMessageW(window, SB_GETPARTS, 0, 0);
        SelectObject(dc, Font(window)); SetBkMode(dc, TRANSPARENT); SetTextColor(dc, p.Secondary);
        for (int i = 0; i < count; i++)
        {
          RECT part; SendMessageW(window, SB_GETRECT, (WPARAM)i, (LPARAM)&part);
          wchar_t text[1024] = {};
          const LRESULT len = SendMessageW(window, SB_GETTEXTLENGTHW, (WPARAM)i, 0);
          if (LOWORD(len) < 1024 && !(HIWORD(len) & SBT_OWNERDRAW))
          { SendMessageW(window, SB_GETTEXTW, (WPARAM)i, (LPARAM)text); part.left += Scale(window,8); DrawTextW(dc, text, -1, &part, DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS); }
        }
        if (message == WM_PAINT) EndPaint(window, &ps);
        return 0;
      }
      break;
    case WM_SETFOCUS:
    case WM_KILLFOCUS:
      // Keep a focused field visible when Tab enters the scrolled part of a form.
      if (message == WM_SETFOCUS && GetPropW(GetParent(window), kLayout))
      {
        HWND parent = GetParent(window);
        CDialogLayout *layout = (CDialogLayout *)GetPropW(parent, kLayout);
        RECT r, client; GetWindowRect(window, &r); MapWindowPoints(NULL, parent, (POINT *)&r, 2); GetClientRect(parent, &client);
        const int bottom = client.bottom - Scale(parent,64);
        if (!IsFooter(window) && (r.top < 0 || r.bottom > bottom))
        { layout->Scroll += r.top < 0 ? r.top - Scale(parent,8) : r.bottom - bottom + Scale(parent,8); Arrange(parent); }
      }
      RedrawWindow(window, NULL, NULL, RDW_INVALIDATE | RDW_FRAME);
      if (ClassIs(window, L"Edit") && (ClassIs(GetParent(window), L"ComboBox") || ClassIs(GetParent(window), WC_COMBOBOXEXW)))
        RedrawWindow(GetParent(window), NULL, NULL, RDW_INVALIDATE | RDW_FRAME);
      break;
    case WM_NCDESTROY:
      RemovePropW(window, L"Arkvello.Hot"); RemoveWindowSubclass(window, ControlProc, 1); break;
  }
  const LRESULT result = DefSubclassProc(window, message, wParam, lParam);
  if (!p.HighContrast && (message == WM_PAINT || message == WM_NCPAINT || message == WM_PRINTCLIENT))
  {
    if (ClassIs(window, L"Edit") && ClassIs(GetParent(window), L"#32770"))
    {
      HDC dc = GetWindowDC(window); RECT r; GetWindowRect(window, &r);
      r.right -= r.left; r.bottom -= r.top; r.left = r.top = 0;
      const bool focused = GetFocus() == window || IsChild(window, GetFocus());
      Outline(dc, r, focused ? p.Accent : p.Edge, focused ? Scale(window,2) : 1);
      if (GetFocus() == window)
      { RECT line = r; line.top = line.bottom - Scale(window,2); Fill(dc, line, p.Accent); }
      ReleaseDC(window, dc);
    }
    if (ClassIs(window, L"ComboBox"))
    {
      HDC dc = message == WM_PRINTCLIENT ? (HDC)wParam : GetDC(window);
      RECT r; GetClientRect(window, &r);
      COMBOBOXINFO info = {sizeof(info), {0,0,0,0}, {0,0,0,0}, 0, NULL, NULL, NULL};
      if (GetComboBoxInfo(window, &info))
      {
        if ((GetWindowLongPtrW(window, GWL_STYLE) & 3) == CBS_DROPDOWNLIST)
        {
          Fill(dc, r, IsPath(window) ? p.Path : p.Panel);
          wchar_t text[1024]; GetWindowTextW(window, text, 1024);
          RECT textRect = info.rcItem; textRect.left += Scale(window,8);
          SetTextColor(dc, IsWindowEnabled(window) ? p.Text : p.Disabled); SetBkMode(dc, TRANSPARENT); SelectObject(dc, Font(window));
          DrawTextW(dc, text, -1, &textRect, DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | DT_END_ELLIPSIS);
        }
        else
        {
          // Classic editable combos paint an inset edge themselves even when
          // their edit child has no border styles. Replace that edge too.
          RECT edge = info.rcItem;
          const int border = GetSystemMetrics(SM_CXEDGE);
          InflateRect(&edge, border, GetSystemMetrics(SM_CYEDGE));
          Outline(dc, edge, IsPath(window) ? p.Path : p.Panel, border);
        }
        Fill(dc, info.rcButton, IsPath(window) ? p.Path : p.Panel);
        RECT arrow = info.rcButton;
        HPEN pen = CreatePen(PS_SOLID, Scale(window,1), IsWindowEnabled(window) ? p.Text : p.Disabled);
        HGDIOBJ old = SelectObject(dc, pen);
        const int x = (arrow.left + arrow.right) / 2, y = (arrow.top + arrow.bottom) / 2;
        MoveToEx(dc, x - Scale(window,4), y - Scale(window,2), NULL); LineTo(dc, x, y + Scale(window,2)); LineTo(dc, x + Scale(window,4), y - Scale(window,2));
        SelectObject(dc, old); DeleteObject(pen);
      }
      const bool focused = GetFocus() == window || IsChild(window, GetFocus());
      Outline(dc, r, focused ? p.Accent : p.Edge, focused ? Scale(window,2) : 1);
      if (message != WM_PRINTCLIENT) ReleaseDC(window, dc);
    }
  }
  return result;
}

static LRESULT CALLBACK RootProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam, UINT_PTR, DWORD_PTR)
{
  if (message == kChanged || message == WM_SETTINGCHANGE || message == WM_SYSCOLORCHANGE
      || message == WM_THEMECHANGED || message == WM_DWMCOLORIZATIONCOLORCHANGED)
  {
    Refresh(); Apply(window); return 0;
  }
  const CColors &p = Colors();
  CDialogLayout *layout = (CDialogLayout *)GetPropW(window, kLayout);
  switch (message)
  {
    case WM_ERASEBKGND:
    { RECT r; GetClientRect(window, &r); Fill((HDC)wParam, r, p.Frame); return 1; }
    case WM_CTLCOLORDLG: return (LRESULT)g_Frame;
    case WM_SIZE: if (layout) Arrange(window); break;
    case WM_MOUSEWHEEL:
      if (layout) { layout->Scroll -= (short)HIWORD(wParam) * Scale(window,34) / WHEEL_DELTA; Arrange(window); return 0; }
      break;
    case WM_VSCROLL:
      if (layout && !lParam)
      {
        SCROLLINFO si = {sizeof(si), SIF_ALL, 0, 0, 0, 0, 0}; GetScrollInfo(window, SB_VERT, &si);
        switch (LOWORD(wParam))
        {
          case SB_LINEUP: layout->Scroll -= Scale(window,34); break;
          case SB_LINEDOWN: layout->Scroll += Scale(window,34); break;
          case SB_PAGEUP: layout->Scroll -= (int)si.nPage; break;
          case SB_PAGEDOWN: layout->Scroll += (int)si.nPage; break;
          case SB_THUMBTRACK: case SB_THUMBPOSITION: layout->Scroll = si.nTrackPos; break;
          case SB_TOP: layout->Scroll = 0; break;
          case SB_BOTTOM: layout->Scroll = si.nMax; break;
        }
        Arrange(window); return 0;
      }
      break;
    case WM_MEASUREITEM:
      if (!p.HighContrast && wParam == 0 && ((MEASUREITEMSTRUCT *)lParam)->CtlType == ODT_MENU)
      {
        MEASUREITEMSTRUCT *m = (MEASUREITEMSTRUCT *)lParam;
        const wchar_t *text = MenuLabel(window, m->itemData);
        if (!text) break; // foreign shell owner-draw items keep their own handler
        HDC dc = GetDC(window); HGDIOBJ old = SelectObject(dc, Font(window)); SIZE size; GetTextExtentPoint32W(dc, text, lstrlenW(text), &size);
        SelectObject(dc, old); ReleaseDC(window, dc);
        m->itemWidth = (UINT)(size.cx + Scale(window,12)); m->itemHeight = (UINT)Scale(window,24); return TRUE;
      }
      break;
    case WM_DRAWITEM:
      if (!p.HighContrast && wParam == 0 && ((DRAWITEMSTRUCT *)lParam)->CtlType == ODT_MENU)
      {
        DRAWITEMSTRUCT *d = (DRAWITEMSTRUCT *)lParam;
        const wchar_t *text = MenuLabel(window, d->itemData);
        if (!text) break;
        Fill(d->hDC, d->rcItem, (d->itemState & (ODS_HOTLIGHT | ODS_SELECTED)) ? p.Hover : p.Frame);
        SetBkMode(d->hDC, TRANSPARENT); SetTextColor(d->hDC, p.Text); SelectObject(d->hDC, Font(window));
        RECT r = d->rcItem; DrawTextW(d->hDC, text, -1, &r, DT_CENTER | DT_VCENTER | DT_SINGLELINE | ((d->itemState & ODS_NOACCEL) ? DT_HIDEPREFIX : 0)); return TRUE;
      }
      break;
    case WM_INITMENUPOPUP:
      CheckMenuRadioItem((HMENU)wParam, IDM_ARKVELLO_THEME_SYSTEM, IDM_ARKVELLO_THEME_DARK,
          IDM_ARKVELLO_THEME_SYSTEM + Preference(), MF_BYCOMMAND);
      break;
    case WM_COMMAND:
      if (!lParam && LOWORD(wParam) >= IDM_ARKVELLO_THEME_SYSTEM && LOWORD(wParam) <= IDM_ARKVELLO_THEME_DARK)
      { SetPreference(LOWORD(wParam) - IDM_ARKVELLO_THEME_SYSTEM); return 0; }
      break;
    case WM_NCDESTROY:
      if (layout) { RemovePropW(window, kLayout); delete layout; }
      delete (CMenuLabels *)RemovePropW(window, kMenuLabels);
      RemoveWindowSubclass(window, RootProc, 2); break;
  }
  // CTLCOLOR / custom drawing must be returned as a real LRESULT, not the
  // BOOL returned by the original dialog procedure.
  if ((message >= WM_CTLCOLORMSGBOX && message <= WM_CTLCOLORSTATIC) || message == WM_NOTIFY)
  {
    if (message != WM_NOTIFY) return ControlProc(window, message, wParam, lParam, 1, 0);
    LRESULT result;
    if (!p.HighContrast && PaintNotify(window, (NMHDR *)lParam, result)) return result;
  }
  return DefSubclassProc(window, message, wParam, lParam);
}

void Attach(HWND window)
{
  Colors();
  SetWindowSubclass(window, RootProc, 2, 0);
  Apply(window);
}

void SetPreference(unsigned preference)
{
  if (preference > 2) return;
  HKEY key;
  if (RegCreateKeyExW(HKEY_CURRENT_USER, kAppearanceKey, 0, NULL, 0, KEY_SET_VALUE, NULL, &key, NULL) == ERROR_SUCCESS)
  {
    const DWORD value = preference;
    RegSetValueExW(key, L"Theme", 0, REG_DWORD, (const BYTE *)&value, sizeof(value));
    RegCloseKey(key);
  }
  PostMessageW(HWND_BROADCAST, kChanged, 0, 0);
}

}
