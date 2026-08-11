using Buddy.App.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using WinUiCornerRadius = Microsoft.UI.Xaml.CornerRadius;
using WinUiSolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using WinUiThickness = Microsoft.UI.Xaml.Thickness;

namespace Buddy.App.Platforms.Windows;

internal static class FramedInputChrome
{
    private const string EntryMappingName = "BuddyFramedEntryChrome";
    private const string EditorMappingName = "BuddyFramedEditorChrome";
    private const string PickerMappingName = "BuddyFramedPickerChrome";

    private static bool _isRegistered;

    public static void Register()
    {
        if (_isRegistered)
        {
            return;
        }

        _isRegistered = true;

        EntryHandler.Mapper.AppendToMapping(
            EntryMappingName,
            static (handler, view) =>
            {
                if (view is FramedEntry framedEntry)
                {
                    ApplyTextControlChrome(
                        handler.PlatformView,
                        framedEntry.BackgroundColor,
                        new WinUiThickness(14, 0, 12, 0));
                }
                else if (view is BorderlessEntry)
                {
                    ApplyBorderlessChrome(handler.PlatformView);
                }
            });

        EditorHandler.Mapper.AppendToMapping(
            EditorMappingName,
            static (handler, view) =>
            {
                if (view is FramedEditor framedEditor)
                {
                    ApplyTextControlChrome(
                        handler.PlatformView,
                        framedEditor.BackgroundColor,
                        new WinUiThickness(12, 9, 12, 9));
                }
                else if (view is BorderlessEditor)
                {
                    ApplyBorderlessChrome(handler.PlatformView);
                }
            });

        PickerHandler.Mapper.AppendToMapping(
            PickerMappingName,
            static (handler, view) =>
            {
                if (view is FramedPicker framedPicker)
                {
                    ApplyPickerChrome(
                        handler.PlatformView,
                        framedPicker.BackgroundColor);
                }
            });
    }

    private static void ApplyTextControlChrome(
        TextBox textBox,
        Microsoft.Maui.Graphics.Color? backgroundColor,
        WinUiThickness padding)
    {
        WinUiSolidColorBrush background = Brush(
            backgroundColor ?? Microsoft.Maui.Graphics.Colors.White);
        ApplyCommonChrome(textBox, background);
        textBox.Padding = padding;

        textBox.Resources["TextControlBackground"] = background;
        SetBrush(textBox, "TextControlBackgroundPointerOver", 0xFF, 0xFB, 0xFB, 0xFE);
        textBox.Resources["TextControlBackgroundFocused"] = background;
        SetBrush(textBox, "TextControlBackgroundDisabled", 0xFF, 0xF3, 0xF4, 0xF8);
        SetBrush(textBox, "TextControlBorderBrush", 0xFF, 0xC5, 0xC9, 0xD7);
        SetBrush(textBox, "TextControlBorderBrushPointerOver", 0xFF, 0x92, 0x98, 0xAD);
        SetBrush(textBox, "TextControlBorderBrushFocused", 0xFF, 0x5B, 0x5C, 0xE2);
        SetBrush(textBox, "TextControlBorderBrushDisabled", 0xFF, 0xDD, 0xE0, 0xE8);
    }

    private static void ApplyPickerChrome(
        ComboBox comboBox,
        Microsoft.Maui.Graphics.Color? backgroundColor)
    {
        WinUiSolidColorBrush background = Brush(
            backgroundColor ?? Microsoft.Maui.Graphics.Colors.White);
        ApplyCommonChrome(comboBox, background);
        comboBox.Padding = new WinUiThickness(14, 0, 10, 0);

        comboBox.Resources["ComboBoxBackground"] = background;
        SetBrush(comboBox, "ComboBoxBackgroundPointerOver", 0xFF, 0xFB, 0xFB, 0xFE);
        SetBrush(comboBox, "ComboBoxBackgroundPressed", 0xFF, 0xF7, 0xF7, 0xFD);
        SetBrush(comboBox, "ComboBoxBackgroundDisabled", 0xFF, 0xF3, 0xF4, 0xF8);
        SetBrush(comboBox, "ComboBoxBorderBrush", 0xFF, 0xC5, 0xC9, 0xD7);
        SetBrush(comboBox, "ComboBoxBorderBrushPointerOver", 0xFF, 0x92, 0x98, 0xAD);
        SetBrush(comboBox, "ComboBoxBorderBrushPressed", 0xFF, 0x5B, 0x5C, 0xE2);
        SetBrush(comboBox, "ComboBoxBorderBrushDisabled", 0xFF, 0xDD, 0xE0, 0xE8);
    }

    private static void ApplyCommonChrome(
        Control control,
        WinUiSolidColorBrush background)
    {
        control.MinHeight = 46;
        control.CornerRadius = new WinUiCornerRadius(10);
        control.BorderThickness = new WinUiThickness(1);
        control.Background = background;
        control.BorderBrush = Brush(0xFF, 0xC5, 0xC9, 0xD7);
        control.Foreground = Brush(0xFF, 0x22, 0x25, 0x38);
    }

    private static void ApplyBorderlessChrome(TextBox textBox)
    {
        WinUiSolidColorBrush transparent = Brush(0x00, 0x00, 0x00, 0x00);
        textBox.CornerRadius = new WinUiCornerRadius(0);
        textBox.BorderThickness = new WinUiThickness(0);
        textBox.Padding = new WinUiThickness(0);
        textBox.Background = transparent;
        textBox.BorderBrush = transparent;
        textBox.UseSystemFocusVisuals = false;

        foreach (string key in new[]
        {
            "TextControlBackground",
            "TextControlBackgroundPointerOver",
            "TextControlBackgroundFocused",
            "TextControlBackgroundDisabled",
            "TextControlBorderBrush",
            "TextControlBorderBrushPointerOver",
            "TextControlBorderBrushFocused",
            "TextControlBorderBrushDisabled",
        })
        {
            textBox.Resources[key] = transparent;
        }
    }

    private static void SetBrush(
        Control control,
        string key,
        byte alpha,
        byte red,
        byte green,
        byte blue) =>
        control.Resources[key] = Brush(alpha, red, green, blue);

    private static WinUiSolidColorBrush Brush(
        byte alpha,
        byte red,
        byte green,
        byte blue) =>
        new(ColorHelper.FromArgb(alpha, red, green, blue));

    private static WinUiSolidColorBrush Brush(
        Microsoft.Maui.Graphics.Color color) =>
        Brush(
            (byte)Math.Round(color.Alpha * byte.MaxValue),
            (byte)Math.Round(color.Red * byte.MaxValue),
            (byte)Math.Round(color.Green * byte.MaxValue),
            (byte)Math.Round(color.Blue * byte.MaxValue));
}
