#nullable enable
using Dalamud.Plugin.Services;
namespace NunuTheAICompanion.Services
{
    /// <summary>
    /// Environment snapshot that works even if Lumina.GeneratedSheets is not referenced.
    /// If you later add the GeneratedSheets reference, define HAVE_LUMINA_GEN at the top of this file
    /// (or as a project conditional symbol) to enable richer names.
    /// </summary>
    public sealed class EnvironmentService
    {
        private readonly IClientState _clientState;
        private readonly IDataManager _data;
        private readonly IPluginLog _log;

        public EnvironmentService(IClientState clientState, IDataManager data, IPluginLog log)
        {
            _clientState = clientState;
            _data = data;
            _log = log;
        }

        public record Snapshot(
            uint TerritoryId,
            string TerritoryName,
            string PlaceName,
            string ZoneName,
            string WeatherName
        );

        public Snapshot GetSnapshot()
        {
            var terrId = _clientState.TerritoryType;

#if HAVE_LUMINA_GEN
            try
            {
                // If you define HAVE_LUMINA_GEN and reference Lumina.Excel.GeneratedSheets,
                // this richer path will be compiled.
                var terr = _data.GetExcelSheet<Lumina.Excel.GeneratedSheets.TerritoryType>()?.GetRow(terrId);

                string? TerritoryName() =>
                    terr?.Name?.ToString()?.Trim() ??
                    terr?.PlaceName.Value?.Name?.ToString()?.Trim();

                string? PlaceName() =>
                    terr?.PlaceName.Value?.Name?.ToString()?.Trim();

                string? ZoneName() =>
                    terr?.PlaceNameRegion.Value?.Name?.ToString()?.Trim()
                    ?? terr?.PlaceNameZone.Value?.Name?.ToString()?.Trim()
                    ?? PlaceName();

                string WeatherName()
                {
                    try
                    {
                        var rate = terr?.WeatherRate.Value;
                        var w = rate?.Weather[0].Value;
                        return w?.Name?.ToString()?.Trim() ?? "Unknown Weather";
                    }
                    catch
                    {
                        return "Unknown Weather";
                    }
                }

                return new Snapshot(
                    TerritoryId: terrId,
                    TerritoryName: TerritoryName() ?? "Unknown Territory",
                    PlaceName:     PlaceName()     ?? "Unknown Place",
                    ZoneName:      ZoneName()      ?? "Unknown Zone",
                    WeatherName:   WeatherName()
                );
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Rich environment read failed; falling back to minimal data.");
            }
#endif

            // Minimal, dependency-free snapshot (always available)
            return new Snapshot(
                TerritoryId: terrId,
                TerritoryName: $"Territory #{terrId}",
                PlaceName: "Unknown Place",
                ZoneName: "Unknown Zone",
                WeatherName: "Unknown Weather"
            );
        }
    }
}
