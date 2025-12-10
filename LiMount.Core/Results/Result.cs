namespace LiMount.Core.Results;

/// <summary>
/// Represents the outcome of an operation that can succeed with a value or fail with an error.
/// This is a discriminated union pattern for explicit error handling.
/// </summary>
/// <typeparam name="TValue">The type of the success value.</typeparam>
public readonly struct Result<TValue>
{
    /// <summary>
    /// Gets whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the success value. Only valid when <see cref="IsSuccess"/> is true.
    /// </summary>
    public TValue? Value { get; }

    /// <summary>
    /// Gets the error message when the operation failed.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets the step that failed in multi-step operations.
    /// </summary>
    public string? FailedStep { get; }

    /// <summary>
    /// Gets the timestamp when the result was created.
    /// </summary>
    public DateTime Timestamp { get; }

    private Result(bool isSuccess, TValue? value, string? errorMessage, string? failedStep)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
        FailedStep = failedStep;
        Timestamp = DateTime.Now;
    }

    /// <summary>
    /// Creates a successful result with the specified value.
    /// </summary>
    public static Result<TValue> Success(TValue value)
        => new(true, value, null, null);

    /// <summary>
    /// Creates a failed result with the specified error message.
    /// </summary>
    /// <param name="errorMessage">The error message describing the failure.</param>
    /// <param name="failedStep">Optional step identifier for multi-step operations.</param>
    public static Result<TValue> Failure(string errorMessage, string? failedStep = null)
        => new(false, default, errorMessage, failedStep);

    /// <summary>
    /// Pattern matches on the result, executing one of two functions based on success or failure.
    /// </summary>
    /// <typeparam name="TResult">The return type of both functions.</typeparam>
    /// <param name="onSuccess">Function to execute if the operation succeeded.</param>
    /// <param name="onFailure">Function to execute if the operation failed.</param>
    /// <returns>The result of the executed function.</returns>
    public TResult Match<TResult>(
        Func<TValue, TResult> onSuccess,
        Func<string, TResult> onFailure)
        => IsSuccess ? onSuccess(Value!) : onFailure(ErrorMessage ?? "Unknown error");

    /// <summary>
    /// Maps the success value to a new type using the specified function.
    /// If the result is a failure, returns a new failure with the same error.
    /// </summary>
    public Result<TNew> Map<TNew>(Func<TValue, TNew> mapper)
        => IsSuccess
            ? Result<TNew>.Success(mapper(Value!))
            : Result<TNew>.Failure(ErrorMessage!, FailedStep);

    /// <summary>
    /// Chains another operation that returns a Result, only if this result is successful.
    /// </summary>
    public Result<TNew> Bind<TNew>(Func<TValue, Result<TNew>> binder)
        => IsSuccess
            ? binder(Value!)
            : Result<TNew>.Failure(ErrorMessage!, FailedStep);

    /// <summary>
    /// Gets the value if successful, or throws an InvalidOperationException if failed.
    /// </summary>
    public TValue GetValueOrThrow()
        => IsSuccess
            ? Value!
            : throw new InvalidOperationException($"Cannot get value from failed result: {ErrorMessage}");

    /// <summary>
    /// Gets the value if successful, or returns the specified default value.
    /// </summary>
    public TValue GetValueOrDefault(TValue defaultValue)
        => IsSuccess ? Value! : defaultValue;

    public override string ToString()
        => IsSuccess
            ? $"Success({Value})"
            : $"Failure({ErrorMessage}{(FailedStep != null ? $" at {FailedStep}" : "")})";
}

/// <summary>
/// Represents the outcome of an operation that can succeed or fail without returning a value.
/// </summary>
public readonly struct Result
{
    /// <summary>
    /// Gets whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the error message when the operation failed.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets the step that failed in multi-step operations.
    /// </summary>
    public string? FailedStep { get; }

    /// <summary>
    /// Gets the timestamp when the result was created.
    /// </summary>
    public DateTime Timestamp { get; }

    private Result(bool isSuccess, string? errorMessage, string? failedStep)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        FailedStep = failedStep;
        Timestamp = DateTime.Now;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result Success() => new(true, null, null);

    /// <summary>
    /// Creates a failed result with the specified error message.
    /// </summary>
    /// <param name="errorMessage">The error message describing the failure.</param>
    /// <param name="failedStep">Optional step identifier for multi-step operations.</param>
    public static Result Failure(string errorMessage, string? failedStep = null)
        => new(false, errorMessage, failedStep);

    /// <summary>
    /// Pattern matches on the result, executing one of two functions based on success or failure.
    /// </summary>
    public TResult Match<TResult>(
        Func<TResult> onSuccess,
        Func<string, TResult> onFailure)
        => IsSuccess ? onSuccess() : onFailure(ErrorMessage ?? "Unknown error");

    /// <summary>
    /// Chains another operation that returns a Result, only if this result is successful.
    /// </summary>
    public Result Bind(Func<Result> binder)
        => IsSuccess ? binder() : this;

    /// <summary>
    /// Chains another operation that returns a Result{T}, only if this result is successful.
    /// </summary>
    public Result<T> Bind<T>(Func<Result<T>> binder)
        => IsSuccess
            ? binder()
            : Result<T>.Failure(ErrorMessage!, FailedStep);

    public override string ToString()
        => IsSuccess
            ? "Success"
            : $"Failure({ErrorMessage}{(FailedStep != null ? $" at {FailedStep}" : "")})";
}
