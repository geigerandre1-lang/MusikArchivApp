using System.Reflection;

namespace MusikArchivApp.Data
{
    public static class AppVersion
    {
        public const string Value = "1.1.3";

        public static string Current
        {
            get
            {
                var informational = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;
                if (string.IsNullOrWhiteSpace(informational))
                {
                    return Value;
                }

                var plus = informational.IndexOf('+');
                return plus >= 0 ? informational[..plus] : informational;
            }
        }
    }
}
