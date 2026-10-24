using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mosaic.Core.Models;
using Mosaic.Host.Services;

namespace Mosaic.Host.Controls;

public sealed class HotkeyInput : TextBox
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(ShortcutBinding), typeof(HotkeyInput),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyInput)d).Refresh()));
    public ShortcutBinding Value { get => (ShortcutBinding)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public HotkeyInput()
    {
        SetResourceReference(StyleProperty, typeof(TextBox));
        IsReadOnly = true; IsReadOnlyCaretVisible = false;
        ToolTip = "点击后按下组合键；Backspace 清除绑定；Tab 移到下一项。";
        PreviewKeyDown += Capture;
    }
    private void Refresh() => Text = Value is null ? "点击录入快捷键" : NativeShell.DescribeHotkey(Value);
    private void Capture(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape) return;
        e.Handled = true;
        if (key is Key.Back or Key.Delete) { Value = new() { Enabled = false, Key = Value?.Key ?? 0x41, Modifiers = Value?.Modifiers ?? 3 }; return; }
        var modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Windows) != 0) { Text = "请使用 Ctrl / Alt / Shift 组合"; return; }
        uint flags = ((modifiers & ModifierKeys.Control) != 0 ? 2u : 0) | ((modifiers & ModifierKeys.Alt) != 0 ? 1u : 0) | ((modifiers & ModifierKeys.Shift) != 0 ? 4u : 0);
        var value = new ShortcutBinding { Modifiers = flags, Key = (uint)KeyInterop.VirtualKeyFromKey(key) };
        try { value.Validate(); Value = value; }
        catch (InvalidDataException) { if (key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)) Text = "请按组合键 + 字母 / 数字 / F1–F12"; }
    }
}
