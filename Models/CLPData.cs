using System.Text.Json.Serialization;

namespace Models;

public record CLPData(
    [property: JsonPropertyName("Id")] string? Id,
    [property: JsonPropertyName("Name")] string? Name,
    [property: JsonPropertyName("Address")] string? Address,
    [property: JsonPropertyName("Value")] double? Value,
    [property: JsonPropertyName("Type")] string? Type
);
