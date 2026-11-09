using System;

namespace PhotoBook.Application
{
    /// <summary>
    /// An outcome that carries the reason it failed, for failures the caller is expected to
    /// handle rather than to be surprised by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The product had three conventions for the same thing. A method returned
    /// <c>bool</c> and the caller guessed at the reason - the export dialog told the
    /// photographer "check that the folder is writable" whatever had actually gone wrong, and
    /// threw the real message away. Another threw, for a failure that happens every day
    /// ("these folders hold no photographs"). A third carried a bespoke result class. Three
    /// conventions means a reader has to learn which is which before he can follow a failure.
    /// </para>
    /// <para>
    /// One convention: the answer comes back, and the reason comes with it.
    /// </para>
    /// </remarks>
    public readonly struct Result
    {
        private Result(bool isSuccess, string? error)
        {
            IsSuccess = isSuccess;
            Error = error;
        }

        /// <summary>True when the work was done.</summary>
        public bool IsSuccess { get; }

        /// <summary>Why it was not done, in words a person can act on. Null on success.</summary>
        public string? Error { get; }

        public static Result Ok() => new(true, null);

        /// <summary>A failure with nothing better to say than this.</summary>
        public static Result Fail(string reason) =>
            new(false, string.IsNullOrWhiteSpace(reason) ? "Неизвестная ошибка" : reason);
    }

    /// <summary>
    /// The same, for a method that also produces something when it works.
    /// </summary>
    /// <typeparam name="T">What the method produces on success.</typeparam>
    public readonly struct Result<T>
    {
        private Result(bool isSuccess, T? value, string? error)
        {
            IsSuccess = isSuccess;
            Value = value;
            Error = error;
        }

        public bool IsSuccess { get; }

        /// <summary>What was produced. Meaningless when <see cref="IsSuccess"/> is false.</summary>
        public T? Value { get; }

        /// <summary>Why it was not done, in words a person can act on. Null on success.</summary>
        public string? Error { get; }

        public static Result<T> Ok(T value) => new(true, value, null);

        public static Result<T> Fail(string reason) =>
            new(false, default, string.IsNullOrWhiteSpace(reason) ? "Неизвестная ошибка" : reason);

        /// <summary>
        /// The value, or the reason. For a caller that genuinely has nothing to do with the
        /// value beyond passing it on.
        /// </summary>
        public T ValueOrThrow() => IsSuccess && Value is not null
            ? Value
            : throw new InvalidOperationException(Error ?? "Неизвестная ошибка");
    }
}
