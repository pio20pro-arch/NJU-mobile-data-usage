using System.Text.Json.Serialization;

namespace NjuTrayApp.Models;

public sealed class GroupResponse
{
    [JsonPropertyName("members")]
    public List<GroupMember> Members { get; set; } = [];
}

public sealed class GroupMember
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("primaryMsisdn")]
    public string PrimaryMsisdn { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public sealed class MemberUsage
{
    public required string UserId { get; init; }
    public required string PhoneNumber { get; init; }
    public required decimal RemainingMb { get; init; }
    public required decimal RoamingMb { get; init; }
}
