using Dalamud.IoC;
using Dalamud.Plugin.Services;
using NunuTheAICompanion.Services;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NunuTheAICompanion
{
    public sealed partial class PluginMain
    {
        [PluginService] internal static IClientState ClientState { get; private set; } = null!;
        [PluginService] internal static ICondition Condition { get; private set; } = null!;

        private EnvironmentService? _env;

        private void InitializeEnvironmentAwareness()
        {
            try
            {
                // matches EnvironmentService ctor: (Framework, ClientState, Condition, Log, Config)
                _env = new EnvironmentService(Framework, ClientState, Condition, Log, Config);
                Log.Info("[Env] Environment awareness initialized (no Lumina).");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Env] init failed; continuing without.");
            }
        }

        private List<(string role, string content)> AugmentWithEnvironment(List<(string role, string content)> baseContext)
        {
            if (_env == null || !Config.EnvironmentEnabled)
                return baseContext;

            var sys = _env.BuildSystemLine();
            if (string.IsNullOrWhiteSpace(sys))
                return baseContext;

            var ctx = new List<(string role, string content)>(baseContext.Count + 1);
            ctx.Add(("system", sys));   // explicit Add avoids nullable tuple inference weirdness
            ctx.AddRange(baseContext);
            return ctx;
        }

        private void TryAnnounceEnvironmentChange()
        {
            if (_env == null) return;
            var msg = _env.ConsumeAnnouncement();
            if (string.IsNullOrEmpty(msg)) return;

            try
            {
                ChatWindow.AddSystemLine(msg);
                if (_broadcaster?.Enabled == true)
                    _broadcaster.Enqueue(_echoChannel, msg);
            }
            catch { }
        }

        private void CmdEnv(List<string> parts)
        {
            if (parts.Count == 1)
            {
                var on = Config.EnvironmentEnabled;
                var inclZone = Config.EnvIncludeZone;
                var inclTime = Config.EnvIncludeTime;
                var inclDuty = Config.EnvIncludeDuty;
                var inclCoords = Config.EnvIncludeCoords;

                ChatWindow.AppendSystem(
                    $"Env: on={on} zone={inclZone} time={inclTime} duty={inclDuty} coords={inclCoords} tick={Config.EnvTickSeconds}s announce={Config.EnvAnnounceOnChange}");
                if (_env != null)
                    ChatWindow.AppendSystem("EnvLine: " + _env.BuildSystemLine());
                return;
            }

            var sub = parts[1].ToLowerInvariant();
            if (sub is "on" or "off")
            {
                Config.EnvironmentEnabled = sub == "on"; Config.Save();
                ChatWindow.AppendSystem($"Env = {Config.EnvironmentEnabled}");
                return;
            }

            if (sub == "tick" && parts.Count >= 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            {
                Config.EnvTickSeconds = Math.Max(1, seconds); Config.Save();
                ChatWindow.AppendSystem($"Env tick = {Config.EnvTickSeconds}s");
                return;
            }

            if (sub == "announce" && parts.Count >= 3 && bool.TryParse(parts[2], out var announceFlag))
            {
                Config.EnvAnnounceOnChange = announceFlag; Config.Save();
                ChatWindow.AppendSystem($"Env announce = {announceFlag}");
                return;
            }

            if (sub == "zone" && parts.Count >= 3 && bool.TryParse(parts[2], out var zoneFlag))
            {
                Config.EnvIncludeZone = zoneFlag; Config.Save();
                ChatWindow.AppendSystem($"Env include zone = {zoneFlag}");
                return;
            }

            if (sub == "time" && parts.Count >= 3 && bool.TryParse(parts[2], out var timeFlag))
            {
                Config.EnvIncludeTime = timeFlag; Config.Save();
                ChatWindow.AppendSystem($"Env include time = {timeFlag}");
                return;
            }

            if (sub == "duty" && parts.Count >= 3 && bool.TryParse(parts[2], out var dutyFlag))
            {
                Config.EnvIncludeDuty = dutyFlag; Config.Save();
                ChatWindow.AppendSystem($"Env include duty = {dutyFlag}");
                return;
            }

            if (sub == "coords" && parts.Count >= 3 && bool.TryParse(parts[2], out var coordsFlag))
            {
                Config.EnvIncludeCoords = coordsFlag; Config.Save();
                ChatWindow.AppendSystem($"Env include coords = {coordsFlag}");
                return;
            }

            ChatWindow.AppendSystem("env | env on|off | env tick <sec> | env announce <true|false> | env zone|time|duty|coords <true|false>");
        }

        private void DrawUi_EnvGlue()
        {
            TryAnnounceEnvironmentChange();
        }
    }
}
