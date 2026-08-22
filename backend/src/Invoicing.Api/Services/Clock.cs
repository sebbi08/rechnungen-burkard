namespace Burkard.Invoicing.Api.Services;

public interface IClock
{
    DateTime GetLocalNow();
    DateTime GetUtcNow();
}

public sealed class SystemClock : IClock
{
    public DateTime GetLocalNow() => TimeProvider.System.GetLocalNow().LocalDateTime;
    public DateTime GetUtcNow() => DateTime.UtcNow;
}
