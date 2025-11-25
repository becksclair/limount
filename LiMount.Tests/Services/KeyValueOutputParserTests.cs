using FluentAssertions;
using LiMount.Core.Services;

namespace LiMount.Tests.Services;

/// <summary>
/// Unit tests for KeyValueOutputParser to verify parsing of script output.
/// </summary>
public class KeyValueOutputParserTests
{
    [Fact]
    public void Parse_ValidKeyValue_ReturnsDictionary()
    {
        // Arrange
        var output = "STATUS=OK\nDistroName=Ubuntu\nMountPath=/mnt/wsl";

        // Act
        var result = KeyValueOutputParser.Parse(output);

        // Assert
        result.Should().HaveCount(3);
        result["STATUS"].Should().Be("OK");
        result["DistroName"].Should().Be("Ubuntu");
        result["MountPath"].Should().Be("/mnt/wsl");
    }

    [Fact]
    public void Parse_MixedFormat_IgnoresInvalidLines()
    {
        // Arrange
        var output = "STATUS=OK\nThis is not a key-value line\n=EmptyKey\nValidKey=ValidValue";

        // Act
        var result = KeyValueOutputParser.Parse(output);

        // Assert
        result.Should().HaveCount(2);
        result["STATUS"].Should().Be("OK");
        result["ValidKey"].Should().Be("ValidValue");
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyDictionary()
    {
        // Act
        var result = KeyValueOutputParser.Parse("");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NullInput_ReturnsEmptyDictionary()
    {
        // Act
        var result = KeyValueOutputParser.Parse(null!);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_WhitespaceInput_ReturnsEmptyDictionary()
    {
        // Act
        var result = KeyValueOutputParser.Parse("   \n\t\n  ");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ValueContainsEquals_PreservesFullValue()
    {
        // Arrange - UNC paths often contain equals in base64 or other encodings
        var output = "Path=C:\\Users\\Test=Value";

        // Act
        var result = KeyValueOutputParser.Parse(output);

        // Assert
        result["Path"].Should().Be("C:\\Users\\Test=Value");
    }

    [Fact]
    public void Parse_CaseInsensitiveKeys_OverwritesPreviousValue()
    {
        // Arrange
        var output = "Status=OK\nSTATUS=ERROR";

        // Act
        var result = KeyValueOutputParser.Parse(output);

        // Assert - Last value wins
        result.Should().HaveCount(1);
        result["status"].Should().Be("ERROR");
    }

    [Fact]
    public void Parse_WindowsLineEndings_HandlesCorrectly()
    {
        // Arrange
        var output = "STATUS=OK\r\nDistroName=Ubuntu\r\n";

        // Act
        var result = KeyValueOutputParser.Parse(output);

        // Assert
        result.Should().HaveCount(2);
        result["STATUS"].Should().Be("OK");
        result["DistroName"].Should().Be("Ubuntu");
    }

    [Fact]
    public void Parse_EmptyValue_ReturnsEmptyString()
    {
        // Arrange
        var output = "Key=";

        // Act
        var result = KeyValueOutputParser.Parse(output);

        // Assert
        result["Key"].Should().Be("");
    }

    [Fact]
    public void Parse_WhitespaceAroundKeyValue_TrimsCorrectly()
    {
        // Arrange
        var output = "  Key  =  Value  ";

        // Act
        var result = KeyValueOutputParser.Parse(output);

        // Assert
        result["Key"].Should().Be("Value");
    }

    [Fact]
    public void IsSuccess_StatusOK_ReturnsTrue()
    {
        // Arrange
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STATUS"] = "OK"
        };

        // Act & Assert
        KeyValueOutputParser.IsSuccess(values).Should().BeTrue();
    }

    [Fact]
    public void IsSuccess_StatusOkLowercase_ReturnsTrue()
    {
        // Arrange
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STATUS"] = "ok"
        };

        // Act & Assert
        KeyValueOutputParser.IsSuccess(values).Should().BeTrue();
    }

    [Fact]
    public void IsSuccess_StatusError_ReturnsFalse()
    {
        // Arrange
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STATUS"] = "ERROR"
        };

        // Act & Assert
        KeyValueOutputParser.IsSuccess(values).Should().BeFalse();
    }

    [Fact]
    public void IsSuccess_NoStatusKey_ReturnsFalse()
    {
        // Arrange
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SomeKey"] = "SomeValue"
        };

        // Act & Assert
        KeyValueOutputParser.IsSuccess(values).Should().BeFalse();
    }

    [Fact]
    public void IsSuccess_EmptyDictionary_ReturnsFalse()
    {
        // Arrange
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Act & Assert
        KeyValueOutputParser.IsSuccess(values).Should().BeFalse();
    }

    [Fact]
    public void GetErrorMessage_HasErrorMessage_ReturnsIt()
    {
        // Arrange
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ErrorMessage"] = "Something went wrong"
        };

        // Act
        var result = KeyValueOutputParser.GetErrorMessage(values);

        // Assert
        result.Should().Be("Something went wrong");
    }

    [Fact]
    public void GetErrorMessage_NoErrorMessage_ReturnsNull()
    {
        // Arrange
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STATUS"] = "OK"
        };

        // Act
        var result = KeyValueOutputParser.GetErrorMessage(values);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetErrorMessage_EmptyDictionary_ReturnsNull()
    {
        // Arrange
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Act
        var result = KeyValueOutputParser.GetErrorMessage(values);

        // Assert
        result.Should().BeNull();
    }
}
