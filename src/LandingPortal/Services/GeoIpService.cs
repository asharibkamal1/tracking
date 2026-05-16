using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
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
    private static readonly GeoLookupResult Empty = new(null, null, null, null, null);
    private readonly DatabaseReader? _reader;
    private readonly bool _enabled;
    private readonly ILogger<GeoIpService> _logger;

    public GeoIpService(IOptions<GeoIpOptions> options, ILogger<GeoIpService> logger)
    {
        _logger = logger;
        _enabled = options.Value.Enabled;
        if (_enabled && File.Exists(options.Value.DbPath))
        {
            _reader = new DatabaseReader(options.Value.DbPath);
            _logger.LogInformation("GeoIP enabled with database at {Path}", options.Value.DbPath);
        }
        else
        {
            _enabled = false;
            _logger.LogInformation("GeoIP disabled (db missing or feature off)");
        }
    }

    public GeoLookupResult Lookup(string? ip)
    {
        if (!_enabled || _reader is null || string.IsNullOrWhiteSpace(ip))
            return Empty;
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
        catch (AddressNotFoundException)
        {
            // Common for loopback / RFC1918 addresses — not worth a log line.
            return Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GeoIP lookup failed for {Ip}", ip);
            return Empty;
        }
    }

    public void Dispose() => _reader?.Dispose();
}
