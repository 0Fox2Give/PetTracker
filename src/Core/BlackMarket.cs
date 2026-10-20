using System.Buffers.Binary;
using System.Text.Json;

namespace PetTracker;

public sealed record MarketItem(int ItemId, int PetId, string SourceUrl);
public sealed record MarketOffer(int OfferId, int ItemId, int PetId, long Price);
public sealed record MarketStock(int Tab, DateTimeOffset SeenUtc, DateTimeOffset RefreshUtc, IReadOnlyList<MarketOffer> Offers)
{
    public string TabName => Tab == 101 ? "Special" : "Pets";
    // Stock is a recent observation, not a background inventory service. A purchase
    // or refresh can change it; never carry it across a connection/account boundary.
    public bool Fresh(DateTimeOffset now) => now >= SeenUtc && now - SeenUtc < TimeSpan.FromMinutes(2) && now < RefreshUtc;
}

public static class BlackMarket
{
    public static IReadOnlyList<MarketItem> Items { get; } = Load();
    static IReadOnlyList<MarketItem> Load()
    {
        using var s = typeof(BlackMarket).Assembly.GetManifestResourceStream("PetTracker.MarketItems.json")!;
        return JsonSerializer.Deserialize<MarketItem[]>(s)!;
    }
    static readonly IReadOnlyDictionary<int, MarketItem> byItem = Items.ToDictionary(i => i.ItemId);
    // Explicitly audited current-release soul-source coverage is required. A null
    // or absent entry means unknown coverage, not proof of a shop-only pet.
    public static bool WatchPet(PetSearchResult pet) => pet.Location?.SoulSources is { Count: 0 } &&
     pet.Acquisition?.Aliases.Contains("Black Cloud Merchants") == true && Items.Any(i => i.PetId == pet.Pet.Id);
    public static IEnumerable<PetSearchResult> Watchlist(string query, Profile profile) => PetFinder.Search(query, profile)
     .Where(WatchPet).Where(p => !profile.Pets.TryGetValue(p.Pet.Id, out var progress) || !progress.Max);
    public static bool NeedsPet(Profile profile, int id) => profile.Pets.TryGetValue(id, out var pet) && pet.LevelVerified &&
     !pet.ProgressNeedsSync && pet.Level is >= 0 and < 3;
    public static IEnumerable<MarketOffer> Recommendations(MarketStock stock, Profile profile, bool synced, DateTimeOffset now) =>
     synced && stock.Fresh(now) ? stock.Offers.Where(o => NeedsPet(profile, o.PetId) && PetFinder.Entries.Any(p => p.Pet.Id == o.PetId && WatchPet(p))) : Array.Empty<MarketOffer>();

    // Two captured US responses (Special and Pets, 2026-10-19), all ten prices
    // checked against the game. Accept only this observed layout. The inter-entry
    // marker bytes are opaque: do not extrapolate their meaning or scan for IDs.
    // Unsupported batches fail as a whole instead of guessing stock or prices.
    public static MarketStock? Decode(byte op, byte family, ReadOnlySpan<byte> body, DateTimeOffset now)
    {
        if (op != 0x91 || family != 0x56 || body.Length != 357 || body[0] is not (101 or 104) || body[1] != 10)
            return null;
        if (body[36] != 0x55 || body[173] != 0x55 || body[310] != 5 || BinaryPrimitives.ReadInt32LittleEndian(body[353..]) != 0)
            return null;
        long expiry = BinaryPrimitives.ReadInt64LittleEndian(body[345..]);
        if (expiry <= now.ToUnixTimeMilliseconds() || expiry > now.AddHours(2).ToUnixTimeMilliseconds())
            return null;
        var offers = new List<MarketOffer>();
        var ids = new HashSet<int>();
        int offset = 2;
        for (int i = 0; i < 10; i++)
        {
            if (i is 1 or 5 or 9)
                offset++;
            var entry = body.Slice(offset, 34);
            offset += 34;
            int offer = BinaryPrimitives.ReadInt32LittleEndian(entry), item = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
            long quantity = BinaryPrimitives.ReadInt64LittleEndian(entry[8..]), original = BinaryPrimitives.ReadInt64LittleEndian(entry[18..]), price = BinaryPrimitives.ReadInt64LittleEndian(entry[26..]);
            int discount = entry[16] switch
            {
                0 => 90,
                1 => 80,
                2 => 70,
                3 => 50,
                _ => 0
            };
            if (offer <= 0 || item <= 0 || !ids.Add(offer) || quantity != 1 || entry[17] != 0x5d || discount == 0 || original is <= 0 or > 1_000_000_000 || price <= 0 || price != original * discount / 100)
                return null;
            if (byItem.TryGetValue(item, out var mapped))
                offers.Add(new(offer, item, mapped.PetId, price));
        }
        return new(body[0], now, DateTimeOffset.FromUnixTimeMilliseconds(expiry), offers);
    }
}

public sealed record MerchantRecommendation(MarketStock Stock, MarketOffer Offer);
public sealed record MerchantDisplayRow(MarketStock Stock, MarketOffer Offer, bool Updated);

// Keep fulfilled recommendations briefly as feedback, then suppress them for
// this stock cycle. A soul reward is evidence of progress, not a purchase receipt.
public sealed class MerchantDisplay
{
    sealed record Entry(MerchantRecommendation Recommendation, DateTimeOffset ObservedUtc, DateTimeOffset? UpdatedUtc = null);
    static readonly PetNames names = new();
    readonly Dictionary<int, Entry> entries = new();
    (string?, string, int?, DateTimeOffset?)? context;
    public IReadOnlyList<MerchantDisplayRow> Select(WorkerView? view, DateTimeOffset now)
    {
        if (view == null || !view.Ready || !view.CollectionSynced || !view.Settings.BlackMarketAlerts || string.IsNullOrEmpty(view.Selected))
        {
            entries.Clear();
            context = null;
            return Array.Empty<MerchantDisplayRow>();
        }
        var next = (view.Selected, view.Character, view.WorkerProcessId, view.LastSync);
        if (context != next)
        {
            entries.Clear();
            context = next;
        }
        foreach (int id in entries.Where(p => p.Value.Recommendation.Stock.RefreshUtc <= now).Select(p => p.Key).ToArray())
            entries.Remove(id);
        var current = MarketOverlay.Select(view, now).ToDictionary(r => r.Offer.PetId);
        foreach (var pair in current)
        {
            if (!entries.TryGetValue(pair.Key, out var prior) || prior.Recommendation.Stock.RefreshUtc != pair.Value.Stock.RefreshUtc)
                entries[pair.Key] = new(pair.Value, pair.Value.Stock.SeenUtc);
            else if (prior.UpdatedUtc == null)
                entries[pair.Key] = prior with
                {
                    Recommendation = pair.Value
                };
        }
        foreach (var pair in entries.ToArray())
        {
            var entry = pair.Value;
            var rewarded = view.Recent.FirstOrDefault(r => r.PetId == pair.Key)?.SoulRewardUtc;
            if (entry.UpdatedUtc == null && rewarded is { } at && at <= now && at > entry.ObservedUtc && at - entry.ObservedUtc < TimeSpan.FromMinutes(2) && at < entry.Recommendation.Stock.RefreshUtc)
                entries[pair.Key] = entry with
                {
                    UpdatedUtc = at
                };
        }
        return entries.Where(p => view.Profile.Pets.ContainsKey(p.Key) &&
          (p.Value.UpdatedUtc is { } at ? now >= at && now - at < TimeSpan.FromSeconds(20) : current.ContainsKey(p.Key)))
         .Select(p => new MerchantDisplayRow(p.Value.Recommendation.Stock, p.Value.Recommendation.Offer, p.Value.UpdatedUtc.HasValue))
         .OrderBy(r => names.Resolve(view.Profile.Pets[r.Offer.PetId]), StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
public static class MarketOverlay
{
    // Keep the serialized preference name for existing installations. It now
    // controls the overlay shopping list rather than a desktop notification.
    public static IReadOnlyList<MerchantRecommendation> Select(WorkerView? view, DateTimeOffset now)
    {
        if (view == null || !view.Settings.BlackMarketAlerts || !view.Ready || !view.CollectionSynced || string.IsNullOrEmpty(view.Selected))
            return Array.Empty<MerchantRecommendation>();
        return (view.Market ?? Array.Empty<MarketStock>())
         .SelectMany(stock => BlackMarket.Recommendations(stock, view.Profile, true, now).Select(offer => new MerchantRecommendation(stock, offer)))
         .GroupBy(row => row.Offer.PetId)
         .Select(group => group.OrderBy(row => row.Offer.Price).ThenBy(row => row.Stock.Tab).First())
         .OrderBy(row => Catalog.Entries.Single(p => p.Id == row.Offer.PetId).Name, StringComparer.Ordinal)
         .ToArray();
    }
}
