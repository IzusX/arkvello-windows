// Presentation only; shared by the file manager and its GUI helper.
#ifndef ZIP7_INC_ARKVELLO_THEME_H
#define ZIP7_INC_ARKVELLO_THEME_H

#include <windows.h>

namespace NArkvello {

struct CColors
{
  bool Dark;
  bool HighContrast;
  COLORREF Frame, Navigation, Path, Panel, Hover, Pressed;
  COLORREF Divider, Edge, Text, Secondary, Disabled;
  COLORREF Accent, OnAccent, Selection, InactiveSelection, Focus, Error;
};

const CColors &Colors();
int Scale(HWND window, int dip);
HFONT Font(HWND window);
void Attach(HWND window); // main window or explicitly opted-in dialog
void Apply(HWND window);  // new/recreated child controls
void StyleMenu(HWND window);
void SetPreference(unsigned preference); // 0 system, 1 light, 2 dark
unsigned Preference();
void LayoutCompressDialog(HWND window);
void LayoutCompressOptions(HWND window);
HBITMAP CommandBitmap(HWND window, unsigned command, int size);
void DrawCommand(HDC dc, const RECT &rect, unsigned command, COLORREF color);

}
#endif
