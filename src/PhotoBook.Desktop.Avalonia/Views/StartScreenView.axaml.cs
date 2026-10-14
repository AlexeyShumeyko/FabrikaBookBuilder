using System;
using Avalonia.Controls;
using PhotoBook.Core;

namespace PhotoBook.Shell.Views;

/// <summary>
/// The screen that asks which of the two modes a new project is.
/// </summary>
/// <remarks>
/// It raises an event and does not decide anything. What happens to a chosen mode belongs to
/// the navigator, which is the only thing that knows how a project is started - a view that
/// called into it itself would be a second place that decides where a screen comes from.
/// </remarks>
public partial class StartScreenView : UserControl
{
    public StartScreenView()
    {
        InitializeComponent();

        UniqueCard.Click += (_, _) => Chosen?.Invoke(AppMode.UniqueFolders);
        CombinedCard.Click += (_, _) => Chosen?.Invoke(AppMode.Combined);
    }

    /// <summary>Raised with the mode the user picked.</summary>
    public event Action<AppMode>? Chosen;
}
