

using System.Collections.ObjectModel;

namespace Kaenx.Creator.Models
{
    public class TranslationExport
    {
        public string Id { get; set; } = string.Empty;
        public int Tab { get; set; } = -5;
        public ObservableCollection<Translation> Text { get; set; } = new();
    }
}