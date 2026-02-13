using FluentAssertions;
using LiMount.Core.Models;
using LiMount.Core.Results;

namespace LiMount.Tests.Results;

/// <summary>
/// Tests for the generic Result{T} and Result types.
/// </summary>
public class ResultTests
{
    #region Result<T> Success Tests

    [Fact]
    public void Result_Success_IsSuccessTrue()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(42);
        result.ErrorMessage.Should().BeNull();
        result.FailedStep.Should().BeNull();
    }

    [Fact]
    public void Result_Success_WithComplexType_ReturnsValue()
    {
        var mountData = new MountData(1, 2, 'Z', "Ubuntu", "/mnt/wsl/test", @"\\wsl$\Ubuntu\mnt\wsl\test");

        var result = Result<MountData>.Success(mountData);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(mountData);
        result.Value!.DiskIndex.Should().Be(1);
        result.Value.Partition.Should().Be(2);
        result.Value.DriveLetter.Should().Be('Z');
    }

    [Fact]
    public void Result_Success_HasTimestamp()
    {
        var before = DateTime.Now;
        var result = Result<int>.Success(1);
        var after = DateTime.Now;

        result.Timestamp.Should().BeOnOrAfter(before);
        result.Timestamp.Should().BeOnOrBefore(after);
    }

    #endregion

    #region Result<T> Failure Tests

    [Fact]
    public void Result_Failure_IsFailureTrue()
    {
        var result = Result<int>.Failure("Something went wrong");

        result.IsFailure.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Something went wrong");
        result.Value.Should().Be(default(int));
    }

    [Fact]
    public void Result_Failure_WithFailedStep_IncludesStep()
    {
        var result = Result<int>.Failure("Mount failed", "mount");

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Be("Mount failed");
        result.FailedStep.Should().Be("mount");
    }

    #endregion

    #region Match Tests

    [Fact]
    public void Result_Match_OnSuccess_ExecutesSuccessFunc()
    {
        var result = Result<int>.Success(10);

        var output = result.Match(
            onSuccess: value => $"Got {value}",
            onFailure: error => $"Error: {error}");

        output.Should().Be("Got 10");
    }

    [Fact]
    public void Result_Match_OnFailure_ExecutesFailureFunc()
    {
        var result = Result<int>.Failure("Oops");

        var output = result.Match(
            onSuccess: value => $"Got {value}",
            onFailure: error => $"Error: {error}");

        output.Should().Be("Error: Oops");
    }

    #endregion

    #region Map Tests

    [Fact]
    public void Result_Map_OnSuccess_TransformsValue()
    {
        var result = Result<int>.Success(5);

        var mapped = result.Map(x => x * 2);

        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be(10);
    }

    [Fact]
    public void Result_Map_OnFailure_PropagatesError()
    {
        var result = Result<int>.Failure("Original error", "step1");

        var mapped = result.Map(x => x * 2);

        mapped.IsFailure.Should().BeTrue();
        mapped.ErrorMessage.Should().Be("Original error");
        mapped.FailedStep.Should().Be("step1");
    }

    #endregion

    #region Bind Tests

    [Fact]
    public void Result_Bind_OnSuccess_ChainsOperation()
    {
        var result = Result<int>.Success(5);

        var bound = result.Bind(x => Result<string>.Success($"Value is {x}"));

        bound.IsSuccess.Should().BeTrue();
        bound.Value.Should().Be("Value is 5");
    }

    [Fact]
    public void Result_Bind_OnSuccess_WithFailingChain_ReturnsFailed()
    {
        var result = Result<int>.Success(5);

        var bound = result.Bind(x => Result<string>.Failure("Chain failed"));

        bound.IsFailure.Should().BeTrue();
        bound.ErrorMessage.Should().Be("Chain failed");
    }

    [Fact]
    public void Result_Bind_OnFailure_SkipsChain()
    {
        var result = Result<int>.Failure("Initial error");
        var chainCalled = false;

        var bound = result.Bind(x =>
        {
            chainCalled = true;
            return Result<string>.Success($"Value is {x}");
        });

        chainCalled.Should().BeFalse();
        bound.IsFailure.Should().BeTrue();
        bound.ErrorMessage.Should().Be("Initial error");
    }

    #endregion

    #region GetValueOrThrow Tests

    [Fact]
    public void Result_GetValueOrThrow_OnSuccess_ReturnsValue()
    {
        var result = Result<int>.Success(42);

        var value = result.GetValueOrThrow();

        value.Should().Be(42);
    }

    [Fact]
    public void Result_GetValueOrThrow_OnFailure_ThrowsException()
    {
        var result = Result<int>.Failure("Test error");

        Action act = () => result.GetValueOrThrow();

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Test error*");
    }

    #endregion

    #region GetValueOrDefault Tests

    [Fact]
    public void Result_GetValueOrDefault_OnSuccess_ReturnsValue()
    {
        var result = Result<int>.Success(42);

        result.GetValueOrDefault(0).Should().Be(42);
    }

    [Fact]
    public void Result_GetValueOrDefault_OnFailure_ReturnsDefault()
    {
        var result = Result<int>.Failure("Error");

        result.GetValueOrDefault(99).Should().Be(99);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void Result_ToString_OnSuccess_ShowsValue()
    {
        var result = Result<int>.Success(42);

        result.ToString().Should().Be("Success(42)");
    }

    [Fact]
    public void Result_ToString_OnFailure_ShowsError()
    {
        var result = Result<int>.Failure("Something broke");

        result.ToString().Should().Be("Failure(Something broke)");
    }

    [Fact]
    public void Result_ToString_OnFailure_WithStep_ShowsStep()
    {
        var result = Result<int>.Failure("Something broke", "mount");

        result.ToString().Should().Be("Failure(Something broke at mount)");
    }

    #endregion

    #region Non-Generic Result Tests

    [Fact]
    public void NonGenericResult_Success_IsSuccessTrue()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void NonGenericResult_Failure_HasError()
    {
        var result = Result.Failure("Operation failed", "validation");

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Be("Operation failed");
        result.FailedStep.Should().Be("validation");
    }

    [Fact]
    public void NonGenericResult_Bind_ToGeneric_Works()
    {
        var result = Result.Success();

        var bound = result.Bind(() => Result<int>.Success(42));

        bound.IsSuccess.Should().BeTrue();
        bound.Value.Should().Be(42);
    }

    [Fact]
    public void NonGenericResult_Bind_OnFailure_PropagatesError()
    {
        var result = Result.Failure("Initial failure", "step1");

        var bound = result.Bind(() => Result<int>.Success(42));

        bound.IsFailure.Should().BeTrue();
        bound.ErrorMessage.Should().Be("Initial failure");
        bound.FailedStep.Should().Be("step1");
    }

    #endregion

    #region Implicit Conversion Tests

    [Fact]
    public void MountAndMapResult_ImplicitConversion_Success()
    {
        var legacy = MountAndMapResult.CreateSuccess(
            diskIndex: 1,
            partition: 2,
            accessMode: WindowsAccessMode.DriveLetterLegacy,
            distroName: "Ubuntu",
            mountPathLinux: "/mnt/wsl/PHYSICALDRIVE1p2",
            mountPathUNC: @"\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE1p2",
            driveLetter: 'Z');

        Result<MountData> result = legacy; // Implicit conversion

        result.IsSuccess.Should().BeTrue();
        result.Value!.DiskIndex.Should().Be(1);
        result.Value.Partition.Should().Be(2);
        result.Value.DriveLetter.Should().Be('Z');
        result.Value.DistroName.Should().Be("Ubuntu");
    }

    [Fact]
    public void MountAndMapResult_ImplicitConversion_Failure()
    {
        var legacy = MountAndMapResult.CreateFailure(
            diskIndex: 1,
            partition: 2,
            errorMessage: "Mount failed",
            failedStep: "mount");

        Result<MountData> result = legacy; // Implicit conversion

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Be("Mount failed");
        result.FailedStep.Should().Be("mount");
    }

    [Fact]
    public void UnmountAndUnmapResult_ImplicitConversion_Success()
    {
        var legacy = UnmountAndUnmapResult.CreateSuccess(
            diskIndex: 1,
            accessMode: WindowsAccessMode.DriveLetterLegacy,
            driveLetter: 'Z');

        Result<UnmountData> result = legacy; // Implicit conversion

        result.IsSuccess.Should().BeTrue();
        result.Value!.DiskIndex.Should().Be(1);
        result.Value.DriveLetter.Should().Be('Z');
    }

    [Fact]
    public void UnmountAndUnmapResult_ImplicitConversion_Failure()
    {
        var legacy = UnmountAndUnmapResult.CreateFailure(
            diskIndex: 1,
            errorMessage: "Unmount failed",
            failedStep: "unmount");

        Result<UnmountData> result = legacy; // Implicit conversion

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Be("Unmount failed");
        result.FailedStep.Should().Be("unmount");
    }

    #endregion
}
