using System.Text.Json.Serialization;

namespace WhoCarried.Core;

/// <summary>What <c>POST /api/runs</c> answers with. Camel case, same as <see cref="RecapView"/>.</summary>
public sealed record ShareResult(string Id, string Url);

/// <summary>
/// JSON for the shared recap. Separate from <see cref="WhoCarriedJson"/> so run saves keep their own property names.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RecapView))]
[JsonSerializable(typeof(ShareResult))]
internal partial class RecapJson : JsonSerializerContext
{
}
