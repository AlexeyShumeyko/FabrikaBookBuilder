// Copyright (c) 2024-2026 Alexey Shumeyko. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhotoBook.Presentation;
using Xunit;

namespace PhotoBook.Application.Tests;

public class CommandTypeHelperTests
{
    [Fact]
    public void GetExpectedParameterType_RelayCommandOfT_ReturnsT()
    {
        var command = new RelayCommand<string>(_ => { });

        var result = CommandTypeHelper.GetExpectedParameterType(command);

        Assert.Equal(typeof(string), result);
    }

    [Fact]
    public void GetExpectedParameterType_AsyncRelayCommandOfT_ReturnsT()
    {
        var command = new AsyncRelayCommand<int>(async _ => await Task.CompletedTask);

        var result = CommandTypeHelper.GetExpectedParameterType(command);

        Assert.Equal(typeof(int), result);
    }

    [Fact]
    public void GetExpectedParameterType_NonGenericRelayCommand_ReturnsNull()
    {
        var command = new RelayCommand(() => { });

        var result = CommandTypeHelper.GetExpectedParameterType(command);

        Assert.Null(result);
    }

    [Fact]
    public void GetExpectedParameterType_NonGenericAsyncRelayCommand_ReturnsNull()
    {
        var command = new AsyncRelayCommand(async () => await Task.CompletedTask);

        var result = CommandTypeHelper.GetExpectedParameterType(command);

        Assert.Null(result);
    }

    [Fact]
    public void GetExpectedParameterType_NullCommand_ReturnsNull()
    {
        var result = CommandTypeHelper.GetExpectedParameterType(null!);

        Assert.Null(result);
    }

    [Fact]
    public void IsParameterCompatible_TypedCommandWithCorrectType_ReturnsTrue()
    {
        var command = new RelayCommand<string>(_ => { });

        var result = CommandTypeHelper.IsParameterCompatible(command, "test");

        Assert.True(result);
    }

    [Fact]
    public void IsParameterCompatible_TypedCommandWithWrongType_ReturnsFalse()
    {
        var command = new RelayCommand<string>(_ => { });

        var result = CommandTypeHelper.IsParameterCompatible(command, 123);

        Assert.False(result);
    }

    [Fact]
    public void IsParameterCompatible_NonGenericCommand_ReturnsTrue()
    {
        var command = new RelayCommand(() => { });

        var result = CommandTypeHelper.IsParameterCompatible(command, "anything");

        Assert.True(result);
    }

    [Fact]
    public void IsParameterCompatible_TypedCommandWithNull_ReferenceType_ReturnsTrue()
    {
        var command = new RelayCommand<string>(_ => { });

        var result = CommandTypeHelper.IsParameterCompatible(command, null);

        Assert.True(result);
    }

    [Fact]
    public void IsParameterCompatible_TypedCommandWithNull_ValueType_ReturnsFalse()
    {
        var command = new RelayCommand<int>(_ => { });

        var result = CommandTypeHelper.IsParameterCompatible(command, null);

        Assert.False(result);
    }

    [Fact]
    public void IsParameterCompatible_TypedCommandWithNull_NullableValueType_ReturnsTrue()
    {
        var command = new RelayCommand<int?>(_ => { });

        var result = CommandTypeHelper.IsParameterCompatible(command, null);

        Assert.True(result);
    }

    [Fact]
    public void TryExecute_TypedCommandWithCorrectType_ExecutesAndReturnsTrue()
    {
        var executed = false;
        var command = new RelayCommand<string>(s => executed = true);

        var result = CommandTypeHelper.TryExecute(command, "test");

        Assert.True(result);
        Assert.True(executed);
    }

    [Fact]
    public void TryExecute_TypedCommandWithWrongType_DoesNotExecuteAndReturnsFalse()
    {
        var executed = false;
        var command = new RelayCommand<string>(s => executed = true);

        var result = CommandTypeHelper.TryExecute(command, 123);

        Assert.False(result);
        Assert.False(executed);
    }

    [Fact]
    public void TryExecute_NonGenericCommand_ExecutesAndReturnsTrue()
    {
        var executed = false;
        var command = new RelayCommand(() => executed = true);

        var result = CommandTypeHelper.TryExecute(command, "ignored");

        Assert.True(result);
        Assert.True(executed);
    }

    [Fact]
    public void TryExecute_CanExecuteReturnsFalse_DoesNotExecuteAndReturnsFalse()
    {
        var executed = false;
        var command = new RelayCommand<string>(s => executed = true, _ => false);

        var result = CommandTypeHelper.TryExecute(command, "test");

        Assert.False(result);
        Assert.False(executed);
    }

    [Fact]
    public void TryExecute_AsyncRelayCommandWithCorrectType_ExecutesAndReturnsTrue()
    {
        var executed = false;
        var command = new AsyncRelayCommand<string>(async s =>
        {
            await Task.CompletedTask;
            executed = true;
        });

        var result = CommandTypeHelper.TryExecute(command, "test");

        Assert.True(result);
        Assert.True(executed);
    }

    [Fact]
    public void TryExecute_AsyncRelayCommandWithWrongType_DoesNotExecuteAndReturnsFalse()
    {
        var executed = false;
        var command = new AsyncRelayCommand<int>(async i =>
        {
            await Task.CompletedTask;
            executed = true;
        });

        var result = CommandTypeHelper.TryExecute(command, "wrong");

        Assert.False(result);
        Assert.False(executed);
    }
}
