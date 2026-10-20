using System.Text.Json;

namespace PetTracker;

public sealed record DiagnosticEvent(DateTimeOffset Time, string Kind, string Message);
public sealed record ConnectionDiagnostics(
    long Payloads,
    long Frames,
    long Rewards,
    long DecoderFaults,
    DateTimeOffset? LastPayload,
    DateTimeOffset? LastIdentity,
    DateTimeOffset? LastReward,
    string LastInterruption,
    IReadOnlyList<DiagnosticEvent> Events,
    DecodeFault? LastFault,
    ProtocolActivityView? Activity = null,
    long ActiveInterruptions = 0,
    long BackgroundInterruptions = 0,
    long RetiredFlows = 0);
public sealed record CaptureAdapterOption(string Id, string Label);
public record WorkerView(
    int Protocol,
    Profile Profile,
    bool Ready,
    string Status,
    string Detail,
    string Character,
    List<LocalProfileBinding> Profiles,
    string? Selected,
    List<RecentPetActivity> Recent,
    PetRankIncrease? Increase,
    DateTimeOffset? LastSync,
    int Devices,
    long Received,
    long Dropped,
    long Gaps,
    AppSettings Settings,
    string? Error = null,
    string? DetectedCharacter = null,
    ConnectionDiagnostics? Health = null,
    int WorkerProcessId = 0,
    IReadOnlyList<NearbyPet>? Nearby = null,
    IReadOnlyList<CaptureAdapterOption>? Adapters = null,
    IReadOnlyList<MarketStock>? Market = null,
    bool CollectionSynced = false);
public record Command(
    string Action,
    string? Key = null,
    string? Name = null,
    string? Realm = null,
    AppSettings? Settings = null,
    Profile? Profile = null,
    List<LocalProfileBinding>? Bindings = null,
    Dictionary<string, Profile>? Imports = null,
    string? AccountName = null,
    Dictionary<string, JsonElement>? Patch = null,
    int? PetId = null);
