# AOT and Trimming Constraints

This doc covers Native AOT and IL trimming considerations for `LiMount.WinUI`.

## Current Configuration

From `LiMount.WinUI.csproj`:
- `PublishAot`: `false` (not enabled by default)
- `PublishReadyToRun`: `true` (faster startup without full AOT)
- `TrimmerRootAssembly`: Serilog (preserves logging under trimming)

## AOT Compatibility Rules

### 1. Mark WinRT-interop classes as `partial`

```csharp
// Required for CsWinRT1028 - WinRT types need partial
public sealed partial class BooleanToVisibilityConverter : IValueConverter
```

### 2. Avoid reflection-based patterns

- No `Type.GetType()` with dynamic strings
- No `Activator.CreateInstance()` without `[DynamicallyAccessedMembers]`
- Prefer `x:Bind` over `{Binding}` for compile-time resolution

### 3. Configuration binding requires pragmas

```csharp
#pragma warning disable IL2026 // Trimming
#pragma warning disable IL3050 // AOT
services.Configure<LiMountConfiguration>(config.GetSection(...));
#pragma warning restore IL3050
#pragma warning restore IL2026
```

### 4. Preserve assemblies for trimming

In `.csproj`:
```xml
<ItemGroup Condition="'$(PublishTrimmed)' == 'true' or '$(PublishAot)' == 'true'">
    <TrimmerRootAssembly Include="Serilog" />
</ItemGroup>
```

## Testing AOT Compatibility

```bash
dotnet publish LiMount.WinUI -c Release -p:PublishAot=true
```

This surfaces AOT issues early even if you don't ship with full AOT.

## Known Limitations

- **XAML compiler** uses net472 `XamlCompiler.exe` (COM interop limitation)
- **Full AOT not production-ready** for WinUI - some components have gaps
- **File logging** requires explicit Serilog.Sinks.File reference
