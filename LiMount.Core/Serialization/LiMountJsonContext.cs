using System.Collections.Generic;
using System.Text.Json.Serialization;
using LiMount.Core.Models;

namespace LiMount.Core.Serialization;

/// <summary>
/// System.Text.Json source-generation context used for AOT/trim friendliness.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<ActiveMount>))]
[JsonSerializable(typeof(List<MountHistoryEntry>))]
[JsonSerializable(typeof(UserSettings))]
internal partial class LiMountJsonContext : JsonSerializerContext
{
}
