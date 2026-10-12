using System;
using System.Globalization;
using System.Linq;
using System.Speech.Synthesis;

namespace User.Pitwall.Runtime
{
    /// <summary>
    /// Reads the engineer messages aloud using the Windows speech engine, preferring a voice
    /// that matches the language of the phrasebook. Everything is asynchronous, because the
    /// telemetry loop must never block.
    /// </summary>
    public sealed class RadioVoice : IDisposable
    {
        private SpeechSynthesizer _synth;
        private bool _failed;

        public bool Enabled { get; set; } = true;

        /// <summary>Speaking rate, -10 slow to 10 fast. A race engineer talks briskly.</summary>
        public int Rate { get; set; } = 2;

        /// <summary>Volume, 0 to 100.</summary>
        public int Volume { get; set; } = 90;

        public string Language { get; set; } = "it";

        /// <summary>Name of the voice actually selected, for display in the settings screen.</summary>
        public string SelectedVoice { get; private set; } = "";

        public string[] AvailableVoices
        {
            get
            {
                try
                {
                    using (var probe = new SpeechSynthesizer())
                        return probe.GetInstalledVoices()
                                    .Where(v => v.Enabled)
                                    .Select(v => v.VoiceInfo.Name)
                                    .ToArray();
                }
                catch (Exception)
                {
                    return new string[0];
                }
            }
        }

        /// <summary>Voice to use, or empty to pick the best match for the language.</summary>
        public string PreferredVoice { get; set; } = "";

        private SpeechSynthesizer Synth
        {
            get
            {
                if (_synth != null || _failed) return _synth;
                try
                {
                    _synth = new SpeechSynthesizer();
                    SelectVoice(_synth);
                    _synth.SetOutputToDefaultAudioDevice();
                }
                catch (Exception)
                {
                    _failed = true;
                    _synth = null;
                }
                return _synth;
            }
        }

        private void SelectVoice(SpeechSynthesizer synth)
        {
            var voices = synth.GetInstalledVoices().Where(v => v.Enabled).ToList();
            if (voices.Count == 0) return;

            if (!string.IsNullOrWhiteSpace(PreferredVoice))
            {
                var wanted = voices.FirstOrDefault(v =>
                    string.Equals(v.VoiceInfo.Name, PreferredVoice, StringComparison.OrdinalIgnoreCase));
                if (wanted != null)
                {
                    synth.SelectVoice(wanted.VoiceInfo.Name);
                    SelectedVoice = wanted.VoiceInfo.Name;
                    return;
                }
            }

            string tag = (Language ?? "it").Substring(0, 2).ToLowerInvariant();
            var match = voices.FirstOrDefault(v =>
                v.VoiceInfo.Culture != null &&
                v.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals(tag, StringComparison.OrdinalIgnoreCase));

            var chosen = match ?? voices[0];
            synth.SelectVoice(chosen.VoiceInfo.Name);
            SelectedVoice = chosen.VoiceInfo.Name;
        }

        /// <summary>Speaks a line. Urgent calls interrupt whatever is being said.</summary>
        public void Speak(string text, bool urgent = false)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(text)) return;
            var synth = Synth;
            if (synth == null) return;

            try
            {
                if (urgent) synth.SpeakAsyncCancelAll();
                synth.Rate = Math.Max(-10, Math.Min(10, Rate));
                synth.Volume = Math.Max(0, Math.Min(100, Volume));
                synth.SpeakAsync(text);
            }
            catch (Exception)
            {
                // A missing or busy audio device must never take the telemetry loop down.
            }
        }

        /// <summary>Re-reads the voice list, for example after the language was changed.</summary>
        public void Rebuild()
        {
            Dispose();
            _failed = false;
        }

        public void Dispose()
        {
            try
            {
                if (_synth != null)
                {
                    _synth.SpeakAsyncCancelAll();
                    _synth.Dispose();
                }
            }
            catch (Exception)
            {
            }
            _synth = null;
        }
    }
}
