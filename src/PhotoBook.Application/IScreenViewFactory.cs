using PhotoBook.Core;

namespace PhotoBook.Application
{
    /// <summary>
    /// Puts a screen on the window for a given mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A navigator knows which screen is next; only a shell knows what a screen is made of.
    /// The two were the same object until Avalonia made them different engines, and a
    /// navigator that names a window cannot be used by any other one.
    /// </para>
    /// <para>
    /// The return type is <see cref="object"/> because a screen is whatever the shell
    /// builds - a <c>UserControl</c> here, something else there - and naming either of them
    /// here would bind this contract to one engine just as firmly as the <c>new</c> it
    /// replaces. What the shell must honour is only that the object can be displayed.
    /// </para>
    /// <para>
    /// The view model is passed in rather than resolved, because the navigator owns the
    /// view models and the shell owns the views. The navigator decides who is on screen;
    /// this decides what that means in pixels.
    /// </para>
    /// </remarks>
    public interface IScreenViewFactory
    {
        /// <summary>
        /// Builds the screen for <paramref name="mode"/> around <paramref name="viewModel"/>.
        /// </summary>
        object CreateScreen(AppMode mode, object viewModel);
    }
}
