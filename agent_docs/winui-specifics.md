# WinUI 3 Specifics

This doc covers key technical details for the WinUI 3 implementation (`LiMount.WinUI`).

## Platform Requirements

- Windows 11 (Build 22000+)
- .NET 10
- Windows App SDK

## WinUI-Specific Services

These services exist in `LiMount.WinUI/Services/`:

- **`IUiDispatcher`** - Thread marshaling abstraction for UI thread operations
- **`IXamlRootProvider`** - Provides `XamlRoot` for `ContentDialog` (set after window loads)
- **`DialogService`** - Uses `ContentDialog` for user prompts

## Converters

Located in `LiMount.WinUI/Converters/`:
- `BooleanToVisibilityConverter` - Maps bool to Visibility (WinUI doesn't have built-in)
- `InverseBooleanConverter` - Inverts boolean values
- `CharToFormattedStringConverter` - Formats drive letters as "X:"
- `SuccessToBrushConverter` - Maps success status to themed colors

## Binding Patterns

Prefer `x:Bind` over `{Binding}` for:
- Compile-time validation
- Better AOT compatibility
- Improved performance

## Architecture Notes

- **Page-based composition**: `MainWindow` hosts `MainPage`, `HistoryWindow` hosts `HistoryPage`
- **Generic Host**: Uses `Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()` for DI
- **Async dialogs**: All dialogs are async via `ContentDialog.ShowAsync()`
