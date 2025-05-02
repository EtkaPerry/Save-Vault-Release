using System;
using System.ComponentModel;
using ReactiveUI;

namespace SaveVaultApp.Utilities
{
    public static class ReactiveExtensions
    {
        /// <summary>
        /// Helper method to raise property changed notifications for ReactiveObject derivatives
        /// </summary>
        public static void NotifyPropertyChanged<T>(this T instance, string propertyName) where T : ReactiveObject
        {
            ((IReactiveObject)instance).RaisePropertyChanged(new PropertyChangedEventArgs(propertyName));
        }
    }
}
