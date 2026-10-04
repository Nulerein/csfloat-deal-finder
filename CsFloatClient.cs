using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CSFloatDealFinder;

internal enum DealSort
{
    HighestDiscount,
    LowestPrice
}

internal sealed record SearchOptions(
    string ApiKey,
    string MarketHashName,
    long? MinPriceCents,
    long? MaxPriceCents,
    double MinDiscountPercent,
    int Pages,
    DealSort Sort);

internal sealed record Deal(
    string ListingId,
    string Name,
    long PriceCents,
    long BasePriceCents,
    double DiscountPercent,
    double? FloatValue);

internal sealed record SearchResult(
    IReadOnlyList<Deal> Deals,
    int LoadedCount,
    int PagesFetched,
    string? RateLimitWarning);

internal sealed class CsFloatRateLimitException(string message) : Exception(message);

internal sealed class CsFloatClient
{
    private const string ApiUrl = "https://csfloat.com/api/v1/listings";
    private const int PageLimit = 50;
    private const int MinimumReferenceQuantity = 10;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<SearchResult> FindDealsAsync(
        SearchOptions options,
        IProgress<(int Done, int Total)>? progress,
        CancellationToken cancellationToken)
    {
        string apiSort = options.Sort == DealSort.LowestPrice ||
                         !string.IsNullOrWhiteSpace(options.MarketHashName)
            ? "lowest_price"
            : "highest_discount";

        var baseParameters = new List<KeyValuePair<string, string>>
        {
            new("limit", PageLimit.ToString(CultureInfo.InvariantCulture)),
            new("sort_by", apiSort),
            new("type", "buy_now")
        };

        if (!string.IsNullOrWhiteSpace(options.MarketHashName))
            baseParameters.Add(new("market_hash_name", options.MarketHashName.Trim()));
        if (options.MaxPriceCents is long maxPrice)
            baseParameters.Add(new("max_price", maxPrice.ToString(CultureInfo.InvariantCulture)));
        if (options.MinPriceCents is long minPrice)
            baseParameters.Add(new("min_price", minPrice.ToString(CultureInfo.InvariantCulture)));

        var found = new List<Deal>();
        int loadedCount = 0;
        int pagesFetched = 0;
        string? cursor = null;
        string? rateLimitWarning = null;

        for (int page = 0; page < options.Pages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (page > 0)
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            var pageParameters = new List<KeyValuePair<string, string>>(baseParameters);
            if (!string.IsNullOrWhiteSpace(cursor))
                pageParameters.Add(new("cursor", cursor));

            string url = ApiUrl + "?" + string.Join("&", pageParameters.Select(
                pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("CSFloatDealFinder/1.0");
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
                request.Headers.TryAddWithoutValidation("Authorization", options.ApiKey.Trim());

            HttpResponseMessage response;
            try
            {
                response = await Http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (HttpRequestException)
            {
                throw new InvalidOperationException("Failed to connect to csfloat.com. Check your internet connection.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("CSFloat did not respond in time.");
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    string retryText = response.Headers.RetryAfter?.Delta is TimeSpan retry
                        ? $" Please try again in {Math.Ceiling(retry.TotalSeconds).ToString(CultureInfo.InvariantCulture)} seconds."
                        : " Please try again later.";
                    string message = "CSFloat request limit reached (HTTP 429)." + retryText;
                    if (pagesFetched == 0)
                        throw new CsFloatRateLimitException(message);

                    rateLimitWarning = message;
                    break;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new InvalidOperationException(
                        "CSFloat rejected the request (HTTP 401). Check the API key in the field above.");
                if (response.StatusCode == HttpStatusCode.Forbidden)
                    throw new InvalidOperationException(
                        "CSFloat denied the request (HTTP 403). Check API access.");
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"CSFloat returned HTTP {(int)response.StatusCode}.");

                JsonDocument document;
                try
                {
                    await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException("CSFloat returned invalid JSON.");
                }

                using (document)
                {
                    JsonElement payload = document.RootElement;
                    JsonElement batchElement;
                    cursor = null;

                    if (payload.ValueKind == JsonValueKind.Array)
                    {
                        batchElement = payload;
                    }
                    else if (payload.ValueKind == JsonValueKind.Object)
                    {
                        if (TryGetProperty(payload, "message", out JsonElement messageElement) &&
                            !TryGetProperty(payload, "data", out _) &&
                            !TryGetProperty(payload, "listings", out _))
                        {
                            string message = ReadString(messageElement) ?? "Unknown API error.";
                            throw new InvalidOperationException("CSFloat: " + message);
                        }

                        if (!TryGetProperty(payload, "data", out batchElement) &&
                            !TryGetProperty(payload, "listings", out batchElement))
                            throw new InvalidOperationException(
                                "CSFloat returned a list of offers in an unknown format.");

                        if (TryGetProperty(payload, "cursor", out JsonElement cursorElement))
                            cursor = ReadString(cursorElement);
                    }
                    else
                    {
                        throw new InvalidOperationException("Unexpected CSFloat API response format.");
                    }

                    if (batchElement.ValueKind != JsonValueKind.Array)
                        throw new InvalidOperationException(
                            "CSFloat returned a list of offers in an unknown format.");

                    pagesFetched++;
                    int batchCount = batchElement.GetArrayLength();
                    loadedCount += batchCount;
                    progress?.Report((pagesFetched, options.Pages));

                    foreach (JsonElement listing in batchElement.EnumerateArray())
                    {
                        if (TryCreateDeal(listing, options, out Deal? deal) && deal is not null)
                            found.Add(deal);
                    }

                    if (string.IsNullOrWhiteSpace(cursor) || batchCount == 0)
                        break;
                }
            }
        }

        if (options.Sort == DealSort.LowestPrice)
            found.Sort((left, right) => left.PriceCents.CompareTo(right.PriceCents));
        else
            found.Sort((left, right) => right.DiscountPercent.CompareTo(left.DiscountPercent));

        return new SearchResult(found, loadedCount, pagesFetched, rateLimitWarning);
    }

    private static bool TryCreateDeal(
        JsonElement listing,
        SearchOptions options,
        out Deal? deal)
    {
        deal = null;
        if (listing.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(listing, "item", out JsonElement item) ||
            item.ValueKind != JsonValueKind.Object)
            return false;

        long? price = TryGetProperty(listing, "price", out JsonElement priceElement)
            ? ReadInt64(priceElement)
            : null;
        if (price is null || price <= 0)
            return false;
        if (options.MaxPriceCents is long maxPrice && price.Value > maxPrice)
            return false;
        if (options.MinPriceCents is long minPrice && price.Value < minPrice)
            return false;

        string name = TryGetProperty(item, "market_hash_name", out JsonElement nameElement)
            ? ReadString(nameElement) ?? string.Empty
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(options.MarketHashName) &&
            !string.Equals(name, options.MarketHashName.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (HasUnmodeledOverpay(listing, item))
            return false;

        JsonElement reference;
        if (!TryGetProperty(listing, "reference", out reference) ||
            reference.ValueKind != JsonValueKind.Object)
        {
            if (!TryGetProperty(item, "reference", out reference) ||
                reference.ValueKind != JsonValueKind.Object)
                return false;
        }

        long? basePrice = TryGetProperty(reference, "base_price", out JsonElement basePriceElement)
            ? ReadInt64(basePriceElement)
            : null;
        long? quantity = TryGetProperty(reference, "quantity", out JsonElement quantityElement)
            ? ReadInt64(quantityElement)
            : null;
        if (basePrice is null || basePrice <= 0 ||
            quantity is null || quantity.Value < MinimumReferenceQuantity)
            return false;

        double discount = (basePrice.Value - price.Value) / (double)basePrice.Value * 100.0;
        if (discount < options.MinDiscountPercent)
            return false;

        string listingId = TryGetProperty(listing, "id", out JsonElement idElement)
            ? ReadString(idElement) ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(listingId))
            return false;

        double? floatValue = TryGetProperty(item, "float_value", out JsonElement floatElement)
            ? ReadDouble(floatElement)
            : null;
        deal = new Deal(listingId, name, price.Value, basePrice.Value, discount, floatValue);
        return true;
    }

    private static bool HasUnmodeledOverpay(JsonElement listing, JsonElement item)
    {
        if (TryGetProperty(item, "stickers", out JsonElement stickers) && HasNonEmptyValue(stickers))
            return true;

        JsonElement badges = default;
        if (!TryGetProperty(item, "badges", out badges) || !HasNonEmptyValue(badges))
            TryGetProperty(listing, "badges", out badges);
        if (ContainsBadge(badges, "rare_sticker") || ContainsBadge(badges, "rare_pattern"))
            return true;

        string name = TryGetProperty(item, "market_hash_name", out JsonElement nameElement)
            ? ReadString(nameElement) ?? string.Empty
            : string.Empty;
        string normalizedName = name.ToLowerInvariant();
        return normalizedName.Contains("doppler", StringComparison.Ordinal) ||
               normalizedName.Contains("case hardened", StringComparison.Ordinal) ||
               normalizedName.Contains("fade", StringComparison.Ordinal);
    }

    private static bool ContainsBadge(JsonElement badges, string marker)
    {
        if (badges.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement badge in badges.EnumerateArray())
            {
                if ((ReadString(badge) ?? badge.ToString())
                    .Equals(marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        return (ReadString(badges) ?? string.Empty)
            .Equals(marker, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasNonEmptyValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Object => value.EnumerateObject().Any(),
        JsonValueKind.String => !string.IsNullOrEmpty(value.GetString()),
        JsonValueKind.True => true,
        _ => false
    };

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out value))
            return true;

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.ToString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };

    private static long? ReadInt64(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt64(out long integer))
                return integer;
            if (element.TryGetDecimal(out decimal decimalValue))
                return (long)decimal.Truncate(decimalValue);
        }

        string? text = ReadString(element);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;
    }

    private static double? ReadDouble(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double number))
            return number;
        return double.TryParse(
            ReadString(element),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double parsed)
            ? parsed
            : null;
    }
}
