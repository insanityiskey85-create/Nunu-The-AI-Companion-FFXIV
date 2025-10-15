using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using System;
using System.Text;

namespace NunuTheAICompanion.Services
{
    public sealed class EnvironmentService : IDisposable
    {
        private readonly IFramework _framework;
        private readonly IClientState _client;
        private readonly ICondition _cond;
        private readonly IPluginLog _log;
        private readonly Configuration _cfg;

        private DateTime _lastTick = DateTime.MinValue;
        private EnvironmentSnapshot _current = new();
        private EnvironmentSnapshot _previous = new();
        private bool _disposed;

        public EnvironmentService(
            IFramework framework,
            IClientState client,
            ICondition condition,
            IPluginLog log,
            Configuration cfg)
        {
            _framework = framework;
            _client = client;
            _cond = condition;
            _log = log;
            _cfg = cfg;

            _framework.Update += OnFrameworkUpdate;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _framework.Update -= OnFrameworkUpdate; } catch { }
        }

        public EnvironmentSnapshot Snapshot() => _current;

        public string BuildSystemLine()
        {
            if (!_cfg.EnvironmentEnabled) return string.Empty;
            var s = _current;
            var parts = new StringBuilder();

            if (_cfg.EnvIncludeZone)
            {
                parts.Append("ZoneId: ").Append(s.TerritoryId).Append(". ");
            }

            if (_cfg.EnvIncludeTime)
            {
                parts.Append("Time: ")
                     .Append(s.LocalTime.ToString("HH:mm"))
                     .Append(" LT; ")
                     .Append($"{s.EorzeaHour:D2}:{s.EorzeaMinute:D2} ET. ");
            }

            if (_cfg.EnvIncludeDuty)
            {
                if (s.InDuty) parts.Append("In Duty. ");
                if (s.InCombat) parts.Append("In Combat. ");
            }

            if (_cfg.EnvIncludeCoords && s.HasCoords)
            {
                parts.Append($"Coords: {s.PosX:0.0}, {s.PosY:0.0}, {s.PosZ:0.0}. ");
            }

            return parts.ToString().Trim();
        }

        private void OnFrameworkUpdate(IFramework _)
        {
            if ((DateTime.UtcNow - _lastTick).TotalSeconds < Math.Max(1, _cfg.EnvTickSeconds))
                return;
            _lastTick = DateTime.UtcNow;

            try
            {
                Recompute();
                MaybeAnnounceChange();
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[Env] update failed");
            }
        }

        private void Recompute()
        {
            _previous = _current;

            var snap = new EnvironmentSnapshot
            {
                LocalTime = DateTime.Now,
                EorzeaHour = GetEorzeaHour(),
                EorzeaMinute = GetEorzeaMinute(),
                InDuty = _cond[ConditionFlag.BoundByDuty],
                InCombat = _cond[ConditionFlag.InCombat],
            };

            if (_client.IsLoggedIn)
            {
                snap.PlayerName = _client.LocalPlayer?.Name.TextValue ?? string.Empty;
                snap.TerritoryId = _client.TerritoryType;

                var pos = _client.LocalPlayer?.Position;
                if (pos != null)
                {
                    snap.HasCoords = true;
                    snap.PosX = pos.Value.X;
                    snap.PosY = pos.Value.Y;
                    snap.PosZ = pos.Value.Z;
                }
            }

            _current = snap;
        }

        private void MaybeAnnounceChange()
        {
            if (!_cfg.EnvAnnounceOnChange) return;

            bool zoneChanged = _previous.TerritoryId != _current.TerritoryId;
            bool dutyChanged = _previous.InDuty != _current.InDuty;

            if (!zoneChanged && !dutyChanged) return;

            var msg = zoneChanged
                ? $"[Env] Territory changed → #{_current.TerritoryId}"
                : _current.InDuty ? "[Env] Entered Duty." : "[Env] Left Duty.";

            LastAnnouncement = msg;
        }

        public string? LastAnnouncement { get; private set; }
        public string? ConsumeAnnouncement()
        {
            var s = LastAnnouncement;
            LastAnnouncement = null;
            return s;
        }

        // ===== Eorzea Time helpers =====
        private static int GetEorzeaTotalSeconds()
        {
            var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            const double Rate = 20.5714285714;
            var et = (long)((unix * Rate) % 86400);
            if (et < 0) et += 86400;
            return (int)et;
        }

        private static int GetEorzeaHour() => (GetEorzeaTotalSeconds() / 3600) % 24;
        private static int GetEorzeaMinute() => (GetEorzeaTotalSeconds() / 60) % 60;
    }

    public struct EnvironmentSnapshot
    {
        public DateTime LocalTime;
        public int EorzeaHour;
        public int EorzeaMinute;

        public int TerritoryId;
        public string PlayerName;

        public bool InDuty;
        public bool InCombat;

        public bool HasCoords;
        public float PosX, PosY, PosZ;
    }
}
