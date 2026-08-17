using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MusikArchivApp.Localization
{
    /// <summary>
    /// Zentrale Ressourcenklasse für alle UI-Texte.
    /// Unterstützt Sprachumschaltung zur Laufzeit.
    /// Neue Sprachen: Dictionary-Eintrag in <see cref="Strings"/> ergänzen.
    /// </summary>
    public sealed class AppResources : INotifyPropertyChanged
    {
        public static AppResources Current { get; } = new AppResources();

        private AppResources() { }

        // ── Verfügbare Sprachen ──────────────────────────────────────────────
        public static IReadOnlyList<LanguageOption> AvailableLanguages { get; } = new[]
        {
            new LanguageOption("de", "Deutsch"),
            new LanguageOption("en", "English")
        };

        private string language = "de";
        public string Language
        {
            get => language;
            set
            {
                if (language == value) return;
                language = value;
                // Alle gebundenen Properties neu auslösen
                OnPropertyChanged(string.Empty);
            }
        }

        // ── Hilfsmethode ─────────────────────────────────────────────────────
        private string T(string de, string en) => language == "en" ? en : de;

        // ── Filter-Operatoren ─────────────────────────────────────────────────
        public string FilterOp_Contains    => T("enthält",       "contains");
        public string FilterOp_NotContains => T("enthält nicht", "does not contain");
        public string FilterOp_StartsWith  => T("beginnt mit",   "starts with");
        public string FilterOp_EndsWith    => T("endet mit",     "ends with");
        public string FilterOp_Equals      => T("exakt",         "exact");

        // ── Allgemeine Buttons / Labels ───────────────────────────────────────
        public string Label_Filter         => T("Filtern",       "Filter");
        public string Label_Reset          => T("Zurücksetzen",  "Reset");
        public string Label_Settings       => T("Einstellungen", "Settings");
        public string Label_Language       => T("Sprache",       "Language");

        // ── INotifyPropertyChanged ────────────────────────────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public record LanguageOption(string Code, string DisplayName);
}
