using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using MongoDB.Driver;

namespace Fogos.Api.GraphQL.Filters;

/// <summary>
/// Hand-mapped incident filter (no generic <c>[UseFiltering]</c> on the public API).
/// Every field maps to an explicit Mongo predicate in <see cref="IncidentFilterMapper"/>.
/// </summary>
public sealed class IncidentFilter
{
    /// <summary>Lisbon calendar day over occurredAt.</summary>
    public DateOnly? Day { get; set; }

    /// <summary>occurredAt before the end of this Lisbon day (inclusive of the day).</summary>
    public DateOnly? Before { get; set; }

    /// <summary>occurredAt at/after the start of this Lisbon day.</summary>
    public DateOnly? After { get; set; }

    /// <summary>
    /// Applies to <c>updatedAt</c> (any change, incl. enrichment), unlike
    /// <c>after</c>/<c>before</c>/<c>day</c> which target <c>occurredAt</c>:
    /// updatedAt at/after the start of this Lisbon day.
    /// </summary>
    public DateOnly? UpdatedAfter { get; set; }

    public string? Concelho { get; set; }
    public string? District { get; set; }
    public string? Dico { get; set; }
    public IReadOnlyList<IncidentKind>? Kind { get; set; }

    /// <summary>Match incidents whose <c>status.code</c> is in this list. Empty/null = no constraint.</summary>
    public IReadOnlyList<int>? StatusCodes { get; set; }

    public string? NaturezaCode { get; set; }
    public string? SubRegion { get; set; }
    public bool? Active { get; set; }

    /// <summary>Remove the default fire-only restriction (legacy <c>all=1</c>).</summary>
    public bool? All { get; set; }
}

/// <summary>Names the input type exactly <c>IncidentFilter</c> (no auto "Input" suffix).</summary>
public sealed class IncidentFilterType : HotChocolate.Types.InputObjectType<IncidentFilter>
{
    protected override void Configure(HotChocolate.Types.IInputObjectTypeDescriptor<IncidentFilter> descriptor) =>
        descriptor.Name("IncidentFilter");
}

/// <summary>Translates <see cref="IncidentFilter"/> into a Mongo <see cref="FilterDefinition{Incident}"/>.</summary>
public static class IncidentFilterMapper
{
    public static FilterDefinition<Incident> Build(IncidentFilter? filter, IClock clock)
    {
        var fb = Builders<Incident>.Filter;
        var conds = new List<FilterDefinition<Incident>>();

        // Default: fire-only unless explicit kinds given or all=true.
        var all = filter?.All == true;
        if (filter?.Kind is { Count: > 0 } kinds)
            conds.Add(fb.In(x => x.Kind, kinds));
        else if (!all)
            conds.Add(fb.Eq(x => x.Kind, IncidentKind.Fire));

        if (filter?.Day is { } day)
        {
            conds.Add(fb.Gte(x => x.OccurredAt, DayStart(day, clock)));
            conds.Add(fb.Lt(x => x.OccurredAt, DayStart(day.AddDays(1), clock)));
        }

        if (filter?.StatusCodes is { Count: > 0 } statusCodes)
            conds.Add(fb.In(x => x.Status.Code, statusCodes));

        if (filter?.After is { } after)
            conds.Add(fb.Gte(x => x.OccurredAt, DayStart(after, clock)));

        if (filter?.UpdatedAfter is { } updatedAfter)
            conds.Add(fb.Gte(x => x.UpdatedAt, DayStart(updatedAfter, clock)));

        // "before" is inclusive of the named calendar day.
        if (filter?.Before is { } before)
            conds.Add(fb.Lt(x => x.OccurredAt, DayStart(before.AddDays(1), clock)));

        AddEq(conds, fb, x => x.Concelho, filter?.Concelho);
        AddEq(conds, fb, x => x.District, filter?.District);
        AddEq(conds, fb, x => x.Dico, filter?.Dico);
        AddEq(conds, fb, x => x.NaturezaCode, filter?.NaturezaCode);
        AddEq(conds, fb, x => x.SubRegion, filter?.SubRegion);

        if (filter?.Active is { } active)
            conds.Add(fb.Eq(x => x.Active, active));

        return conds.Count == 0 ? fb.Empty : fb.And(conds);
    }

    private static void AddEq(
        List<FilterDefinition<Incident>> conds,
        FilterDefinitionBuilder<Incident> fb,
        System.Linq.Expressions.Expression<Func<Incident, string?>> field,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            conds.Add(fb.Eq(field, value));
    }

    private static DateTimeOffset DayStart(DateOnly day, IClock clock) =>
        clock.FromLisbon(day.ToDateTime(TimeOnly.MinValue));
}
