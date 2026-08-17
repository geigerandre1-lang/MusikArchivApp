namespace MusikArchivApp.Models
{
    /// <summary>
    /// Ein einzelnes Filterkriterium für ein Textfeld: welches Feld, welcher Operator, welcher Wert.
    /// </summary>
    public class FilterCriterion
    {
        public string Field { get; set; } = string.Empty;
        public FilterOperator Operator { get; set; } = FilterOperator.Contains;
        public string Value { get; set; } = string.Empty;
    }
}
