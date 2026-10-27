using System.Text.Json.Serialization;
using Mixdeck.Core.Model;
using Mixdeck.Core.Settings;
namespace Mixdeck.Core.Serialization;
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Profile))]
[JsonSerializable(typeof(Rule))]
[JsonSerializable(typeof(List<Profile>))]
[JsonSerializable(typeof(List<Rule>))]
public partial class MixdeckJsonContext : JsonSerializerContext;
