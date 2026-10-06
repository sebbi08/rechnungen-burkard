using System;
using Burkard.Invoicing.Api.Services;

namespace Burkard.Invoicing.IntegrationTests;

/// <summary>A clock that stands still. The UTC moment can differ from the local
/// one, for tests about the turn of a year.</summary>
public sealed class FixedClock : IClock
{
    private readonly DateTime _local;
    private readonly DateTime _utc;

    public FixedClock(DateTime local, DateTime? utc = null)
    {
        _local = DateTime.SpecifyKind(local, DateTimeKind.Local);
        _utc = DateTime.SpecifyKind(utc ?? local, DateTimeKind.Utc);
    }

    public DateTime GetLocalNow() => _local;

    public DateTime GetUtcNow() => _utc;
}
