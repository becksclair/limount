# WinUI 3 Specifics

This doc covers key differences between WinUI 3 (`LiMount.WinUI`) and WPF (`LiMount.App`).

## Platform Requirements

- Windows 11 only
- .NET 10 (preview)
- Windows App SDK

## Key Differences from WPF

| Concept | WPF | WinUI 3 |
|---------|-----|---------|
| Thread dispatch | `Dispatcher.Invoke` | `IUiDispatcher` abstraction (DI) |
| Dialog root | Implicit | Requires `XamlRoot` via `IXamlRootProvider` |
| Bool→Visibility | Built-in converter | Custom `BooleanToVisibilityConverter` |
| String formatting | `StringFormat` in binding | `IValueConverter` |

## WinUI-Specific Services

These services exist only in `LiMount.WinUI`:

- **`IUiDispatcher`** - Thread marshaling abstraction (like WPF's Dispatcher)
- **`IXamlRootProvider`** - Provides `XamlRoot` for `ContentDialog` (set after window loads)
- **`DialogService`** - Uses `ContentDialog` instead of WPF's `MessageBox`

## Converters

Located in `LiMount.WinUI/Converters/`:
- `BooleanToVisibilityConverter` - Must be marked `partial` for WinRT interop

## Binding Patterns

Prefer `x:Bind` over `{Binding}` for:
- Compile-time validation
- Better AOT compatibility
- Improved performance
