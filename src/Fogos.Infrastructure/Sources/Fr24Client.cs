using System.Net.Http.Headers;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Fogos.Infrastructure.Sources;

/// <summary>
/// Tracks Flightradar24 credit spend in a per-month Redis counter (<c>fr24:credits:month:{yyyy-MM}</c>)
/// and enforces the shared-budget guard (legacy 95%). The FR24 budget is shared with the live
/// platform, so the guard keeps this stack from exhausting it before switchover.
/// </summary>
public sealed class Fr24CreditMeter(IConnectionMultiplexer redis, IClock clock, IOptions<FogosSourcesOptions> options)
{
    private Fr24Options Options => options.Value.Fr24;

    private string Key => $"fr24:credits:month:{clock.UtcNow:yyyy-MM}";

    /// <summary>Credits already spent this month.</summary>
    public async Task<long> CurrentAsync()
    {
        var value = await redis.GetDatabase().StringGetAsync(Key);
        return value.HasValue ? (long)value : 0;
    }

    /// <summary>True while spend is below the guard threshold (budget × guard fraction).</summary>
    public async Task<bool> HasBudgetAsync()
    {
        if (Options.MonthlyBudget <= 0)
            return true; // 0 = budget disabled

        var current = await CurrentAsync();
        return current < Options.MonthlyBudget * Options.BudgetGuardFraction;
    }

    /// <summary>
    /// Reserves <paramref name="credits"/> if the guard allows it; returns false (and reserves nothing)
    /// once spend has reached the 95% threshold.
    /// </summary>
    public async Task<bool> TryConsumeAsync(int credits = 1)
    {
        if (!await HasBudgetAsync())
            return false;

        var db = redis.GetDatabase();
        var updated = await db.StringIncrementAsync(Key, credits);
        if (updated == credits) // first write this month — set a safety expiry
            await db.KeyExpireAsync(Key, TimeSpan.FromDays(35));
        return true;
    }
}

/// <summary>
/// Flightradar24 client (shell): live-positions-light by registration, Bearer-authenticated. Returns
/// raw JSON; callers gate on <see cref="Fr24CreditMeter"/> before spending.
/// </summary>
public sealed class Fr24Client(HttpClient http, IOptions<FogosSourcesOptions> options)
{
    public const string HttpClientName = "fr24";

    private Fr24Options Options => options.Value.Fr24;

    public async Task<string> GetLivePositionsLightAsync(string registration, CancellationToken ct = default)
    {
        var url = $"{Options.BaseUrl}/api/live/flight-positions/light?registrations={Uri.EscapeDataString(registration)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
