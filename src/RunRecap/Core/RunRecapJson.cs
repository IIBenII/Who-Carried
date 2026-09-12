using System.Text.Json.Serialization;

namespace RunRecap.Core;

/// <summary>Compile-time JSON serializer (no reflection needed at runtime).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(RunStats))]
internal partial class RunRecapJson : JsonSerializerContext
{
}
