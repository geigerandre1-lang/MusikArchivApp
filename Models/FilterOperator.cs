namespace MusikArchivApp.Models
{
    /// <summary>
    /// Vergleichsoperator für Text-Filterkriterien (ähnlich Excel-Autofilter).
    /// </summary>
    public enum FilterOperator
    {
        Contains,
        NotContains,
        StartsWith,
        EndsWith,
        Equals
    }
}
