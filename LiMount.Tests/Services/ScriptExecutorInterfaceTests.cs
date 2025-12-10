using FluentAssertions;
using Microsoft.Extensions.Options;
using LiMount.Core.Configuration;
using LiMount.Core.Interfaces;
using LiMount.Core.Services;

namespace LiMount.Tests.Services;

/// <summary>
/// Interface contract tests verifying ScriptExecutor correctly implements all focused interfaces.
/// These tests ensure the Interface Segregation Principle (ISP) migration maintains correctness.
/// </summary>
public class ScriptExecutorInterfaceTests
{
    private readonly ScriptExecutor _executor;

    public ScriptExecutorInterfaceTests()
    {
        var config = new LiMountConfiguration
        {
            ScriptExecution = new ScriptExecutionConfig
            {
                TempFilePollingTimeoutSeconds = 30,
                PollingIntervalMs = 100
            }
        };
        var options = Options.Create(config);

        // Use a non-existent scripts path for interface tests (we're testing casting, not execution)
        _executor = new ScriptExecutor(options, scriptsPath: "C:\\nonexistent");
    }

    [Fact]
    public void ScriptExecutor_ImplementsIMountScriptService()
    {
        // Act & Assert
        _executor.Should().BeAssignableTo<IMountScriptService>();

        var service = _executor as IMountScriptService;
        service.Should().NotBeNull();
    }

    [Fact]
    public void ScriptExecutor_ImplementsIDriveMappingService()
    {
        // Act & Assert
        _executor.Should().BeAssignableTo<IDriveMappingService>();

        var service = _executor as IDriveMappingService;
        service.Should().NotBeNull();
    }

    [Fact]
    public void ScriptExecutor_ImplementsIFilesystemDetectionService()
    {
        // Act & Assert
        _executor.Should().BeAssignableTo<IFilesystemDetectionService>();

        var service = _executor as IFilesystemDetectionService;
        service.Should().NotBeNull();
    }

    [Fact]
    public void ScriptExecutor_ImplementsIScriptExecutor()
    {
        // Act & Assert - IScriptExecutor is obsolete but should still work for backward compatibility
        #pragma warning disable CS0618 // IScriptExecutor is obsolete
        _executor.Should().BeAssignableTo<IScriptExecutor>();

        var service = _executor as IScriptExecutor;
        service.Should().NotBeNull();
        #pragma warning restore CS0618
    }

    [Fact]
    public void ScriptExecutor_CanBeCastToAllInterfaces()
    {
        // Arrange
        object instance = _executor;

        // Act & Assert - All casts should succeed
        var mountService = instance as IMountScriptService;
        var mappingService = instance as IDriveMappingService;
        var detectionService = instance as IFilesystemDetectionService;

        #pragma warning disable CS0618 // IScriptExecutor is obsolete
        var legacyService = instance as IScriptExecutor;
        #pragma warning restore CS0618

        mountService.Should().NotBeNull("ScriptExecutor should be castable to IMountScriptService");
        mappingService.Should().NotBeNull("ScriptExecutor should be castable to IDriveMappingService");
        detectionService.Should().NotBeNull("ScriptExecutor should be castable to IFilesystemDetectionService");
        legacyService.Should().NotBeNull("ScriptExecutor should be castable to IScriptExecutor for backward compatibility");
    }

    [Fact]
    public void ScriptExecutor_AllInterfacesCastToSameInstance()
    {
        // Arrange
        object instance = _executor;

        // Act
        var mountService = instance as IMountScriptService;
        var mappingService = instance as IDriveMappingService;
        var detectionService = instance as IFilesystemDetectionService;

        #pragma warning disable CS0618 // IScriptExecutor is obsolete
        var legacyService = instance as IScriptExecutor;
        #pragma warning restore CS0618

        // Assert - All references point to the same object
        ReferenceEquals(mountService, mappingService).Should().BeTrue("All interfaces should reference the same instance");
        ReferenceEquals(mappingService, detectionService).Should().BeTrue("All interfaces should reference the same instance");
        ReferenceEquals(detectionService, legacyService).Should().BeTrue("All interfaces should reference the same instance");
    }

    [Fact]
    public async Task ScriptExecutor_ValidationErrorsConsistentAcrossInterfaces()
    {
        // Arrange - Get references through different interfaces
        IMountScriptService mountService = _executor;
        IDriveMappingService mappingService = _executor;

        // Act - Call with invalid parameters through different interfaces
        var mountResult = await mountService.ExecuteMountScriptAsync(-1, 1, "ext4");
        var mappingResult = await mappingService.ExecuteMappingScriptAsync('1', ""); // '1' is not a letter

        // Assert - Both should fail validation with appropriate messages
        mountResult.Success.Should().BeFalse();
        mountResult.ErrorMessage.Should().Contain("non-negative");

        mappingResult.Success.Should().BeFalse();
        mappingResult.ErrorMessage.Should().Contain("valid letter");
    }

    [Fact]
    public async Task ScriptExecutor_SameValidationThroughIScriptExecutorAndFocusedInterfaces()
    {
        // Arrange
        IMountScriptService focusedService = _executor;

        #pragma warning disable CS0618 // IScriptExecutor is obsolete
        IScriptExecutor legacyService = _executor;
        #pragma warning restore CS0618

        // Act - Same call through both interfaces
        var focusedResult = await focusedService.ExecuteMountScriptAsync(-1, 1, "ext4");

        #pragma warning disable CS0618 // IScriptExecutor is obsolete
        var legacyResult = await legacyService.ExecuteMountScriptAsync(-1, 1, "ext4");
        #pragma warning restore CS0618

        // Assert - Results should be equivalent
        focusedResult.Success.Should().Be(legacyResult.Success);
        focusedResult.ErrorMessage.Should().Be(legacyResult.ErrorMessage);
    }
}
