using System.IO.Enumeration;
using Arkvello.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Arkvello;

public sealed partial class MainWindow
{
    private bool SelectionReady => !busy && !configuring && !closing && renameBox == null;

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        if (SelectionReady) { list.SelectAll(); FocusFileList(); }
    }

    private void OnClearSelection(object sender, RoutedEventArgs e)
    {
        if (SelectionReady) { list.SelectedItems.Clear(); FocusFileList(); }
    }

    private void OnInvertSelection(object sender, RoutedEventArgs e)
    {
        if (SelectionReady) ChangeSelection(null, selected => !selected);
    }

    private async void OnSelectionMask(object sender, RoutedEventArgs e) =>
        await SelectByMask(((MenuFlyoutItem)sender).Tag is not "Deselect");

    private void OnSelectionType(object sender, RoutedEventArgs e) =>
        SelectByType(((MenuFlyoutItem)sender).Tag is not "Deselect");

    private async Task SelectByMask(bool select)
    {
        if (!SelectionReady || entries.Count == 0) return;
        var mask = new TextBox { Header = "Маска имени", Text = "*", MaxLength = 1024 };
        var dialog = NewDialog(select ? "Выделить по маске" : "Снять выделение по маске", mask, "Применить", "Отмена");
        dialog.Opened += (_, _) => { mask.Focus(FocusState.Programmatic); mask.SelectAll(); };
        configuring = true;
        ContentDialogResult answer;
        try { answer = await dialog.ShowAsync(); }
        finally { configuring = false; }
        if (!closing && answer == ContentDialogResult.Primary) ApplySelectionMask(mask.Text, select);
        else if (!closing) FocusFileList();
    }

    private void ApplySelectionMask(string mask, bool select) =>
        ChangeSelection(item => FileSystemName.MatchesSimpleExpression(mask, item.Name), _ => select);

    private void SelectByType(bool select)
    {
        if (!SelectionReady || CurrentItem() is not { } current) return;
        // 7zFM uses the last dot even for dotfiles and names ending in a dot.
        static string Suffix(string name) => name.LastIndexOf('.') is int dot && dot >= 0 ? name[dot..] : "";
        string suffix = Suffix(current.Name);
        ChangeSelection(item => item.IsDirectory == current.IsDirectory &&
            (item.IsDirectory || string.Equals(Suffix(item.Name), suffix, StringComparison.OrdinalIgnoreCase)), _ => select);
    }

    private void ChangeSelection(Predicate<FileEntry>? matches, Func<bool, bool> transform)
    {
        var pane = activePanel;
        pane.Current = CurrentItem();
        var previous = list.SelectedItems.Cast<FileEntry>().Select(item => item.Identity).ToHashSet();
        pane.UpdatingSelection = true;
        try
        {
            ApplySelectionRanges(list, entries, item =>
            {
                if (matches != null && !matches(item)) return null;
                bool selected = previous.Contains(item.Identity);
                bool desired = transform(selected);
                return desired != selected ? desired : null;
            });
        }
        finally { pane.UpdatingSelection = false; }
        UpdateStatus();
        FocusFileList();
    }

    private static void ApplySelectionRanges(ListViewBase target, IReadOnlyList<FileEntry> items, Func<FileEntry, bool?> change)
    {
        int first = 0;
        bool? pending = null;
        for (int i = 0; i <= items.Count; i++)
        {
            bool? next = i < items.Count ? change(items[i]) : null;
            if (next == pending) continue;
            if (pending is { } select)
            {
                var range = new ItemIndexRange(first, (uint)(i - first));
                if (select) target.SelectRange(range);
                else target.DeselectRange(range);
            }
            first = i;
            pending = next;
        }
    }

    private void ConfigureSelectionInput(PanelState pane, ListViewBase target)
    {
        string prefix = "";
        long lastCharacter = 0;
        bool? maskOnRelease = null;
        target.PreviewKeyDown += (_, e) =>
        {
            if (e.Handled || !SelectionReady || pane != activePanel || !ReferenceEquals(target, list)) return;
            bool alt = KeyDown(VirtualKey.Menu), shift = KeyDown(VirtualKey.Shift), ctrl = KeyDown(VirtualKey.Control);
            if (ctrl && !alt && !shift && e.Key == VirtualKey.A)
            {
                e.Handled = true;
                if (!e.KeyStatus.WasKeyDown) OnSelectAll(this, e);
            }
            else if (e.Key is VirtualKey.Add or VirtualKey.Subtract or VirtualKey.Multiply)
            {
                e.Handled = !ctrl;
                if (ctrl || e.KeyStatus.WasKeyDown) return;
                bool select = e.Key == VirtualKey.Add;
                if (e.Key == VirtualKey.Multiply) OnInvertSelection(this, e);
                else if (alt) SelectByType(select);
                else if (shift) { if (select) OnSelectAll(this, e); else OnClearSelection(this, e); }
                else maskOnRelease = select;
            }
        };
        target.PreviewKeyUp += async (_, e) =>
        {
            if (e.Key is not (VirtualKey.Add or VirtualKey.Subtract) || maskOnRelease is not { } select) return;
            maskOnRelease = null;
            e.Handled = true;
            if (pane == activePanel && ReferenceEquals(target, list)) await SelectByMask(select);
        };
        target.LostFocus += (_, _) => maskOnRelease = null;
        target.CharacterReceived += (_, e) =>
        {
            if (!SelectionReady || pane != activePanel || !ReferenceEquals(target, list) ||
                KeyDown(VirtualKey.Control) || KeyDown(VirtualKey.Menu) || char.IsControl(e.Character) ||
                e.Character is ' ' or '+' or '-' or '*') return;
            long now = Environment.TickCount64;
            if (now - lastCharacter > 900) prefix = "";
            lastCharacter = now;
            prefix = FindByTyping(prefix, e.Character);
            e.Handled = true;
        };
    }

    private string FindByTyping(string prefix, char character)
    {
        int count = entries.Count;
        if (count == 0) return "";
        int current = CurrentItem() is { } item ? entries.IndexOf(item) : -1;
        string pattern = prefix + character;
        FileEntry? Find(string text, bool includeCurrent)
        {
            int start = current + (includeCurrent && current >= 0 ? 0 : 1);
            for (int i = 0; i < count; i++)
            {
                var candidate = entries[(start + i) % count];
                if (candidate.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            return null;
        }
        var match = Find(pattern, prefix.Length > 0);
        if (match == null && prefix.Length > 0)
        {
            pattern = character.ToString();
            match = Find(pattern, includeCurrent: false);
        }
        if (match == null) return "";
        lastCurrentItem = match;
        if (!markMode)
        {
            list.SelectedItems.Clear();
            list.SelectedItems.Add(match);
        }
        FocusFileList();
        return pattern;
    }
}
