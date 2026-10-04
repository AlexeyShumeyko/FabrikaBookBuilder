using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhotoBook.Core
{
    /// <summary>
    /// Property change notification, in fifteen lines.
    ///
    /// <para>
    /// The entities in this assembly used to derive from a view-model base class that came
    /// from an MVVM toolkit, which put a user-interface library inside the domain. The
    /// entities are bound directly by the views, so they do need to raise change
    /// notifications - but not the rest of what a view model needs, and not a dependency
    /// on the toolkit. <c>ViewModelBase</c> in the shell derives from this.
    /// </para>
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Assigns the field and raises a notification when the value actually changed.
        /// Returns whether it changed, so callers can skip work that a repaint would
        /// otherwise repeat.
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
