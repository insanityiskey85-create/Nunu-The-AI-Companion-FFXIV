#nullable enable
using Dalamud.Game.ClientState;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Text;

namespace NunuTheAICompanion.Services
{
    /// <summary>
    /// Reads current territory, weather, and place names without ever using RowRef&lt;&gt;,
    /// so you don't need a direct reference to Lumina.dll.
    /// </summary>
    public sealed class EnvironmentService
    {
        private readonly IClientState _clientState;
        private readonly IDataManager _data;

        public EnvironmentService(IClientState clientState, IDataManager data)
        {
            _clientState = clientState;
            _data = data;
        }

        public uint CurrentTerritoryId => _clientState.TerritoryType;

        public string CurrentTerritoryName =>
            GetTerritoryName(_clientState.TerritoryType) ?? $"Territory#{_clientState.TerritoryType}";

        public string? GetTerritoryName(uint territoryId)
        {
            var sheet = _data.GetExcelSheet<TerritoryType>();
            var row = sheet?.GetRow(territoryId);
            if (row == null)
                return null;

            // TerritoryType.PlaceName is a row ID; resolve to PlaceName
            var placeNameText = ResolvePlaceName(row.PlaceName);
            if (!string.IsNullOrEmpty(placeNameText))
                return placeNameText;

            // Fallbacks
            if (!string.IsNullOrEmpty(row.Name?.ToString()))
                return row.Name.ToString();

            return null;
        }

        public string? ResolvePlaceName(uint placeNameRowId)
        {
            if (placeNameRowId == 0)
                return null;

            var sheet = _data.GetExcelSheet<PlaceName>();
            var row = sheet?.GetRow(placeNameRowId);
            return row?.Name?.ToString();
        }

        public string? ResolvePlaceName(ushort placeNameRowId)
            => ResolvePlaceName((uint)placeNameRowId);

        public string? GetWeatherName(uint weatherRowId)
        {
            if (weatherRowId == 0)
                return null;

            var sheet = _data.GetExcelSheet<Weather>();
            var row = sheet?.GetRow(weatherRowId);
            return row?.Name?.ToString();
        }

        /// <summary>
        /// Build a short human string like "Gridania (Clear Skies)".
        /// </summary>
        public string DescribeLocation(uint? territoryIdOverride = null, uint? weatherIdOverride = null)
        {
            var terrId = territoryIdOverride ?? CurrentTerritoryId;
            var terr = GetTerritoryName(terrId) ?? $"Territory#{terrId}";

            string weather = "Unknown";
            if (weatherIdOverride.HasValue)
            {
                weather = GetWeatherName(weatherIdOverride.Value) ?? "Unknown";
            }
            else
            {
                // If you have your own weather tracker that yields a row id, call GetWeatherName(id) here.
                // Otherwise leave as Unknown.
            }

            return $"{terr} ({weather})";
        }

        // Optional: util to get UTF-8 plain text with emojis stripped (if you need it).
        public static string CleanSeString(SeString? s)
            => s?.ToString() ?? string.Empty;
    }
}
