using FluentAssertions;
using Microsoft.Extensions.Options;
using LiMount.Core.Configuration;
using LiMount.Core.Services;

namespace LiMount.Tests.Services;

/// <summary>
/// Tests for ScriptExecutor input validation, particularly the filesystem type whitelist.
/// </summary>
public class ScriptExecutorValidationTests
{
    private readonly ScriptExecutor _executor;

    public ScriptExecutorValidationTests()
    {
        var config = new LiMountConfiguration
        {
            ScriptExecution = new ScriptExecutionConfig
            {
                TempFilePollingTimeoutSeconds = 5,
                PollingIntervalMs = 100
            }
        };
        var options = Options.Create(config);

        // Use non-existent scripts path - validation tests don't need actual scripts
        _executor = new ScriptExecutor(options, scriptsPath: "C:\\nonexistent");
    }

    #region Valid Filesystem Types

    [Theory]
    [InlineData("ext4")]
    [InlineData("xfs")]
    [InlineData("btrfs")]
    [InlineData("vfat")]
    [InlineData("auto")]
    public async Task ExecuteMountScriptAsync_ValidFilesystemType_DoesNotReturnFilesystemValidationError(string fsType)
    {
        // Act
        var result = await _executor.ExecuteMountScriptAsync(0, 1, fsType);

        // Assert - should fail for "script not found", NOT "unsupported filesystem"
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotContain("Unsupported filesystem type");
        result.ErrorMessage.Should().Contain("script not found", because: "validation passed, failed at script execution");
    }

    #endregion

    #region Invalid Filesystem Types

    [Theory]
    [InlineData("ntfs")]
    [InlineData("fat32")]
    [InlineData("exfat")]
    [InlineData("apfs")]
    [InlineData("hfs+")]
    [InlineData("invalid")]
    [InlineData("unknown")]
    public async Task ExecuteMountScriptAsync_InvalidFilesystemType_ReturnsValidationError(string fsType)
    {
        // Act
        var result = await _executor.ExecuteMountScriptAsync(0, 1, fsType);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported filesystem type");
        result.ErrorMessage.Should().Contain(fsType);
        result.ErrorMessage.Should().Contain("Supported types:");
    }

    [Theory]
    [InlineData("ext4; rm -rf /")]
    [InlineData("ext4 && whoami")]
    [InlineData("ext4 | cat /etc/passwd")]
    [InlineData("$(malicious)")]
    [InlineData("`malicious`")]
    public async Task ExecuteMountScriptAsync_InjectionAttempt_ReturnsValidationError(string fsType)
    {
        // Act
        var result = await _executor.ExecuteMountScriptAsync(0, 1, fsType);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported filesystem type",
            because: "injection attempts should be blocked by whitelist validation");
    }

    #endregion

    #region Case Insensitivity

    [Theory]
    [InlineData("EXT4")]
    [InlineData("Ext4")]
    [InlineData("XFS")]
    [InlineData("Xfs")]
    [InlineData("BTRFS")]
    [InlineData("Btrfs")]
    [InlineData("VFAT")]
    [InlineData("Vfat")]
    [InlineData("AUTO")]
    [InlineData("Auto")]
    public async Task ExecuteMountScriptAsync_FilesystemTypeCaseInsensitive_Accepted(string fsType)
    {
        // Act
        var result = await _executor.ExecuteMountScriptAsync(0, 1, fsType);

        // Assert - should fail for "script not found", NOT "unsupported filesystem"
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotContain("Unsupported filesystem type",
            because: "filesystem type validation should be case-insensitive");
    }

    #endregion

    #region Whitespace Handling

    [Theory]
    [InlineData(" ext4")]
    [InlineData("ext4 ")]
    [InlineData(" ext4 ")]
    [InlineData("  xfs  ")]
    [InlineData("\text4")]
    [InlineData("ext4\t")]
    public async Task ExecuteMountScriptAsync_FilesystemTypeWithWhitespace_TrimmedAndAccepted(string fsType)
    {
        // Act
        var result = await _executor.ExecuteMountScriptAsync(0, 1, fsType);

        // Assert - should fail for "script not found", NOT "unsupported filesystem"
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotContain("Unsupported filesystem type",
            because: "whitespace should be trimmed before validation");
    }

    #endregion

    #region Empty and Null Handling

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task ExecuteMountScriptAsync_EmptyOrWhitespaceFilesystemType_ReturnsValidationError(string fsType)
    {
        // Act
        var result = await _executor.ExecuteMountScriptAsync(0, 1, fsType);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid filesystem type");
    }

    [Fact]
    public async Task ExecuteMountScriptAsync_NullFilesystemType_ReturnsValidationError()
    {
        // Act
        var result = await _executor.ExecuteMountScriptAsync(0, 1, null!);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid filesystem type");
    }

    #endregion

    #region Disk and Partition Validation (existing behavior verification)

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task ExecuteMountScriptAsync_NegativeDiskIndex_ReturnsValidationError(int diskIndex)
    {
        // Act
        var result = await _executor.ExecuteMountScriptAsync(diskIndex, 1, "ext4");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Disk index must be non-negative");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ExecuteMountScriptAsync_InvalidPartition_ReturnsValidationError(int partition)
    {
        // Act
        var result = await _executor.ExecuteMountScriptAsync(0, partition, "ext4");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Partition number must be greater than 0");
    }

    #endregion
}
