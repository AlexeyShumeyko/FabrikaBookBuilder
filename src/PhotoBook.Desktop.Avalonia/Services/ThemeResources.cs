using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhotoBook.Application;

namespace PhotoBook.Shell.Services;

/// <summary>
/// A colour from the theme, for the places markup cannot reach.
/// </summary>
/// <remarks>
/// <para>
/// Markup asks for a token by name and Avalonia re-reads it when the variant changes. Code
/// cannot do that: a converter runs once, and a window built in code is built once, so a
/// brush read at that moment would keep the theme it was read in - a dialog opened in the
/// dark theme, left open, switched back, and still dark.
/// </para>
/// <para>
/// So the brushes are made once per token and kept. When the variant changes every kept
/// brush is given the new colour, and because a brush is a mutable object rather than a
/// copied value, every control already holding one repaints. One table, one subscription,
/// and nothing outside <c>Tokens.axaml</c> holds a colour of its own.
/// </para>
/// </remarks>
public static class ThemeResources
{
    private static readonly Dictionary<string, SolidColorBrush> Kept = new(StringComparer.Ordinal);

    private static bool _watching;

    /// <summary>
    /// The brush the theme names <paramref name="token"/>.
    /// </summary>
    /// <remarks>
    /// The same instance comes back for the same token, which is what makes the repaint on a
    /// theme switch work. A token the theme does not define is a mistake in the caller: it is
    /// written down and drawn as nothing, because a dialog that fails to paint one border is
    /// still a dialog and an exception here is not.
    /// </remarks>
    public static IBrush Brush(string token)
    {
        if (Kept.TryGetValue(token, out var known)) return known;

        var brush = new SolidColorBrush(Read(token));
        Kept[token] = brush;
        Watch();
        return brush;
    }

    /// <summary>The shadow the theme names, for a window built in code.</summary>
    public static BoxShadows Shadow(string token)
    {
        // Spelled out in full: inside a namespace starting with PhotoBook the bare word
        // Application resolves to that namespace, not to Avalonia's class.
        if (Avalonia.Application.Current is { } application
            && application.TryFindResource(token, application.ActualThemeVariant, out var value))
        {
            if (value is BoxShadows shadows) return shadows;
        }

        Journal.Write(JournalLevel.Warning, "theme", $"В теме нет тени «{token}»");
        return default;
    }

    private static Color Read(string token)
    {
        // The variant is passed in on purpose. Without it the lookup answers from the
        // "Default" dictionary, which is the light theme, and every dialog opened in the
        // dark one came out light.
        if (Avalonia.Application.Current is { } application
            && application.TryFindResource(token, application.ActualThemeVariant, out var value))
        {
            switch (value)
            {
                case Color color:
                    return color;

                case ISolidColorBrush brush:
                    return brush.Color;
            }
        }

        Journal.Write(JournalLevel.Warning, "theme", $"В теме нет цвета «{token}»");
        return Colors.Transparent;
    }

    private static void Watch()
    {
        if (_watching || Avalonia.Application.Current is not { } application) return;

        _watching = true;
        application.ActualThemeVariantChanged += OnVariantChanged;
    }

    private static void OnVariantChanged(object? sender, EventArgs e)
    {
        foreach (var pair in Kept)
            pair.Value.Color = Read(pair.Key);
    }
}
