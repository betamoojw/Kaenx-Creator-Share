using System.ComponentModel;

namespace Kaenx.Creator.Models
{
    public class Options : INotifyPropertyChanged
    {
        private bool _supportsExtendedMemoryServices = false;
        public bool SupportsExtendedMemoryServices
        {
            get { return _supportsExtendedMemoryServices; }
            set
            {
                _supportsExtendedMemoryServices = value;
                OnPropertyChanged(nameof(SupportsExtendedMemoryServices));
            }
        }

        private bool _supportsExtendedPropertyServices = false;
        public bool SupportsExtendedPropertyServices
        {
            get { return _supportsExtendedPropertyServices; }
            set
            {
                _supportsExtendedPropertyServices = value;
                OnPropertyChanged(nameof(SupportsExtendedPropertyServices));
            }
        }

        private bool _supportsIPSystemBroadcast = false;
        public bool SupportsIPSystemBroadcast
        {
            get { return _supportsIPSystemBroadcast; }
            set
            {
                _supportsIPSystemBroadcast = value;
                OnPropertyChanged(nameof(SupportsIPSystemBroadcast));
            }
        }

        private bool _promptBeforeFullDownload = false;
        public bool PromptBeforeFullDownload
        {
            get { return _promptBeforeFullDownload; }
            set
            {
                _promptBeforeFullDownload = value;
                OnPropertyChanged(nameof(PromptBeforeFullDownload));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}