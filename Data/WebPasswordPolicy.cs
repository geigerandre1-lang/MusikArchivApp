using System.Linq;

namespace MusikArchivApp.Data
{
    public static class WebPasswordPolicy
    {
        public const int MinLength = 14;

        public static bool TryValidate(string? password, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            {
                error = $"Das Web-Passwort muss mindestens {MinLength} Zeichen haben.";
                return false;
            }

            if (!password.Any(char.IsUpper))
            {
                error = "Das Web-Passwort braucht mindestens einen Großbuchstaben.";
                return false;
            }

            if (!password.Any(char.IsLower))
            {
                error = "Das Web-Passwort braucht mindestens einen Kleinbuchstaben.";
                return false;
            }

            if (!password.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                error = "Das Web-Passwort braucht mindestens ein Sonderzeichen.";
                return false;
            }

            return true;
        }
    }
}
