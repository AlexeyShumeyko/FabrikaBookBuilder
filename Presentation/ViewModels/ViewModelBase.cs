using PhotoBook.Core;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    /// <summary>
    /// Base for the view models of the shell. The notification itself comes from
    /// <see cref="PhotoBook.Core.ObservableObject"/> in the domain assembly, which is what
    /// the entities use too, so a bound object behaves the same whether it is an entity or
    /// a view model.
    /// </summary>
    public abstract class ViewModelBase : ObservableObject
    {
    }
}
