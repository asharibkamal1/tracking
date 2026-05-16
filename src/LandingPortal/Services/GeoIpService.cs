using MaxMind.GeoIP2;
using Microsoft.Extensions.Options;
using TaxpayerAnalytics.Shared.Configuration;

namespace TaxpayerAnalytics.LandingPortal.Services;

public interface IGeoIpService
{
    GeoLookupResult Lookup(string? ipAddress);
}

public sealed record GeoLookupResult(string? Country, string? City, string? Region, double? Latitude, double? Longitude);

public sealed class GeoIpService : IGeoIpService, IDisposable
{
    private readonly DatabaseReader? _reader;
    private readonly bool _enabled;

    public GeoIpService(IOptions<GeoIpOptions> options, ILogger<GeoIpService> logger)
    {
        _enabled = options.Value.Enabled;
        if (_enabled && File.Exists(options.Value.DbPath))
        {
            _reader = new DatabaseReader(options.Value.DbPath);
        }
        else
        {
            _enabled = false;
            logger.LogInformation("GeoIP disabled (db missing or feature off)");
        }
    }

    public GeoLookupResult Lookup(string? ip)
    {
        if (!_enabled || _reader is null || string.IsNullOrWhiteSpace(ip))
            return new GeoLookupResult(null, null, null, null, null);
        try
        {
            var r = _reader.City(ip);
            return new GeoLookupResult(
                r.Country?.Name,
                r.City?.Name,
                r.MostSpecificSubdivision?.Name,
                r.Location?.Latitude,
                r.Location?.Longitude);
        }
        catch
        {
            return new GeoLookupResult(null, null, null, null, null);
        }
    }

    public void Dispose() => _reader?.Dispose();
}
