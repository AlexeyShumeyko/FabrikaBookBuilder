// Copyright (c) 2024-2026 Alexey Shumeyko. All rights reserved.
// Proprietary and confidential. See LICENSE in the project root for the terms.

using System;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace PhotoBook.Presentation;

/// <summary>
/// What kind of argument a command was declared to take, and how to hand it over safely.
/// </summary>
/// <remarks>
/// <para>
/// A command generated from a typed method is not the loose <see cref="ICommand"/> it looks
/// like. Both <c>RelayCommand&lt;Page&gt;.CanExecute(object)</c> and
/// <c>AsyncRelayCommand&lt;Page&gt;.CanExecute(object)</c> throw
/// <see cref="ArgumentException"/> when the argument is not a page. That includes
/// <c>CanExecute</c>, which is the surprising half: asking a command whether it is willing is
/// exactly what one does to avoid the exception, and here the question is what throws.
/// Verified against CommunityToolkit.Mvvm 8.2.2 rather than assumed.
/// </para>
/// <para>
/// So the declared kind is read from the command itself - from the
/// <c>IRelayCommand&lt;T&gt;</c> interface it implements - before either method is called. A
/// screen that gives one handler to many buttons can then hand over whatever the button was
/// pointing at, and a mismatch stays a refusal instead of ending the session.
/// </para>
/// </remarks>
public static class CommandTypeHelper
{
    /// <summary>
    /// The argument type the command was declared with, or <see langword="null"/> for a
    /// command that takes no argument.
    /// </summary>
    /// <remarks>
    /// <c>IAsyncRelayCommand&lt;T&gt;</c> extends <c>IRelayCommand&lt;T&gt;</c>, so one
    /// interface answers for both synchronous and asynchronous commands. A non-generic
    /// command implements neither and is reported as untyped, which it is: it ignores the
    /// argument it is handed.
    /// </remarks>
    public static Type? GetExpectedParameterType(ICommand command)
    {
        if (command is null) return null;

        return command.GetType()
            .GetInterfaces()
            .Where(type => type.IsGenericType)
            .FirstOrDefault(type => type.GetGenericTypeDefinition() == typeof(IRelayCommand<>))
            ?.GetGenericArguments()[0];
    }

    /// <summary>
    /// Whether the command can be handed this argument at all.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> is a valid argument for a reference type and for
    /// <see cref="Nullable{T}"/>, and an invalid one for any other value type - the same rule
    /// the command itself applies before it throws.
    /// </remarks>
    public static bool IsParameterCompatible(ICommand command, object? parameter)
    {
        var expected = GetExpectedParameterType(command);
        if (expected is null) return true;

        if (parameter is null)
        {
            return !expected.IsValueType || Nullable.GetUnderlyingType(expected) is not null;
        }

        return expected.IsInstanceOfType(parameter);
    }

    /// <summary>
    /// Runs the command with the argument if it accepts it, and answers whether it ran.
    /// </summary>
    /// <remarks>
    /// A typed command asked about an argument of the wrong kind raises instead of answering,
    /// so the kind is settled first, and <c>CanExecute</c> is asked only once it is known to
    /// be safe to ask.
    /// </remarks>
    public static bool TryExecute(ICommand command, object? parameter)
    {
        if (command is null) return false;
        if (!IsParameterCompatible(command, parameter)) return false;
        if (!command.CanExecute(parameter)) return false;

        command.Execute(parameter);
        return true;
    }
}
