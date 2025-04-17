using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Kaenx.Creator.Models
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class TranslationItem
    {
        public int UId { get; set; } = -5;
        public string Name { get; set; }
        public string Group { get; set; }
        public string SubGroup { get; set; }
        public ObservableCollection<Translation> Text { get; set; }

        public string Id { get; set; }

        public TranslationItem(int uid, string name, string group, string subgroup, ObservableCollection<Translation> text)
        {
            UId = uid;
            Name = name;
            Group = group;
            SubGroup = subgroup;
            Text = text;

            Id = $"{group}-{subgroup}-{uid}-{name}";
        }
    }
}