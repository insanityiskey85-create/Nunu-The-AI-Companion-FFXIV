using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.Services;

/// <summary>
/// Voice service that *optionally* uses System.Speech at runtime via reflection.
/// If the assembly isn't available, it gracefully no-ops (still compiles).
/// </summary>
public sealed class VoiceService : IDisposable
{
    private readonly IPluginLog _log;
    private readonly Configuration _cfg;

    // Runtime SpeechSynthesizer and cached members (via reflection)
    private object? _tts;
    private PropertyInfo? _propVolume;
    private PropertyInfo? _propRate;
    private PropertyInfo? _propVoice;
    private MethodInfo? _methSelectVoice;
    private MethodInfo? _methSpeak;
    private MethodInfo? _methGetInstalledVoices;
    private MethodInfo? _methSetOutputToDefaultAudioDevice;

    private readonly object _gate = new();
    private readonly Queue<string> _queue = new();
    private Thread? _bg;
    private volatile bool _running;
    private volatile bool _available; // true if reflection setup succeeded

    public VoiceService(Configuration cfg, IPluginLog log)
    {
        _cfg = cfg;
        _log = log;

        try
        {
            // Try to load System.Speech at runtime
            // 1) Already loaded?
            var asm = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "System.Speech", StringComparison.OrdinalIgnoreCase));

            // 2) Try by name if not loaded yet
            asm ??= Assembly.Load("System.Speech");

            var tSynth = asm.GetType("System.Speech.Synthesis.SpeechSynthesizer", throwOnError: true)!;

            _tts = Activator.CreateInstance(tSynth);

            // Cache members
            _propVolume = tSynth.GetProperty("Volume");
            _propRate = tSynth.GetProperty("Rate");
            _methSelectVoice = tSynth.GetMethod("SelectVoice", new[] { typeof(string) });
            _methSpeak = tSynth.GetMethod("Speak", new[] { typeof(string) });
            _methGetInstalledVoices = tSynth.GetMethod("GetInstalledVoices", Type.EmptyTypes);

            // Optional method present on SpeechSynthesizer
            _methSetOutputToDefaultAudioDevice = tSynth.GetMethod("SetOutputToDefaultAudioDevice", Type.EmptyTypes);

            // Apply initial settings
            SafeSetInt(_propVolume, Math.Clamp(_cfg.VoiceVolume, 0, 100));
            SafeSetInt(_propRate, Math.Clamp(_cfg.VoiceRate, -10, 10));

            if (!string.IsNullOrWhiteSpace(_cfg.VoiceName))
                _methSelectVoice?.Invoke(_tts, new object[] { _cfg.VoiceName });

            _available = true;
            _log.Info("[Voice] System.Speech loaded via reflection.");
        }
        catch (Exception ex)
        {
            _available = false;
            _tts = null;
            _log.Warning(ex, "[Voice] System.Speech not available. Voice output disabled (will no-op).");
        }

        _running = true;
        _bg = new Thread(Worker) { IsBackground = true, Name = "Nunu.Voice" };
        _bg.Start();
    }

    public void Dispose()
    {
        _running = false;
        lock (_gate) Monitor.PulseAll(_gate);
        try { _bg?.Join(1000); } catch { /* ignore */ }

        // SpeechSynthesizer implements IDisposable; dispose if we have it
        try { (_tts as IDisposable)?.Dispose(); } catch { }
    }

    public void Speak(string text, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!_available && !force) return;                  // no-op if not available
        if (!_cfg.VoiceSpeakEnabled && !force) return;
        if (_cfg.VoiceOnlyWhenWindowFocused && !(PluginMain.Instance?.ChatWindow?.IsOpen ?? false))
            return;

        lock (_gate)
        {
            _queue.Enqueue(text);
            Monitor.Pulse(_gate);
        }
    }

    public IReadOnlyList<string> ListVoices()
    {
        if (!_available || _tts is null || _methGetInstalledVoices is null)
            return Array.Empty<string>();

        try
        {
            var voices = _methGetInstalledVoices.Invoke(_tts, null);
            if (voices is System.Collections.IEnumerable e)
            {
                var list = new List<string>();
                foreach (var v in e)
                {
                    // v.VoiceInfo.Name
                    var voiceInfoProp = v.GetType().GetProperty("VoiceInfo");
                    var voiceInfo = voiceInfoProp?.GetValue(v);
                    var nameProp = voiceInfo?.GetType().GetProperty("Name");
                    var name = nameProp?.GetValue(voiceInfo) as string;
                    if (!string.IsNullOrWhiteSpace(name))
                        list.Add(name);
                }
                return list.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[Voice] listing installed voices failed");
        }
        return Array.Empty<string>();
    }

    public bool TrySelectVoice(string name)
    {
        if (!_available || _tts is null || _methSelectVoice is null)
            return false;

        try
        {
            _methSelectVoice.Invoke(_tts, new object[] { name });
            _cfg.VoiceName = name;
            _cfg.Save();
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[Voice] select voice failed for {Name}", name);
            return false;
        }
    }

    private void Worker()
    {
        while (_running)
        {
            string? text = null;
            lock (_gate)
            {
                if (_queue.Count == 0)
                {
                    Monitor.Wait(_gate, 250);
                    continue;
                }
                text = _queue.Dequeue();
            }

            if (string.IsNullOrWhiteSpace(text) || !_available || _tts is null || _methSpeak is null)
                continue;

            try
            {
                // Apply latest config
                SafeSetInt(_propVolume, Math.Clamp(_cfg.VoiceVolume, 0, 100));
                SafeSetInt(_propRate, Math.Clamp(_cfg.VoiceRate, -10, 10));

                if (!string.IsNullOrWhiteSpace(_cfg.VoiceName))
                {
                    try { _methSelectVoice?.Invoke(_tts, new object[] { _cfg.VoiceName }); }
                    catch { /* ignore voice not found */ }
                }

                // Ensure output target
                try { _methSetOutputToDefaultAudioDevice?.Invoke(_tts, null); } catch { /* older builds may not need this */ }

                _methSpeak.Invoke(_tts, new object[] { text });
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[Voice] speak failed");
            }
        }
    }

    private void SafeSetInt(PropertyInfo? pi, int value)
    {
        if (pi == null || _tts == null) return;
        try { pi.SetValue(_tts, value); } catch { /* ignore */ }
    }
}
