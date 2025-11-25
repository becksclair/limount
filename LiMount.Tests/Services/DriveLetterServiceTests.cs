using FluentAssertions;
using LiMount.Core.Services;

namespace LiMount.Tests.Services;

/// <summary>
/// Unit tests for DriveLetterService.
/// Note: Some tests verify behavior with actual system state, others use
/// IsLetterAvailable with explicit usedLetters to test logic without system dependency.
/// </summary>
public class DriveLetterServiceTests
{
    private readonly DriveLetterService _service;

    public DriveLetterServiceTests()
    {
        _service = new DriveLetterService();
    }

    [Fact]
    public void GetUsedLetters_ReturnsNonEmptyList()
    {
        // Act - This depends on system state but C: should always exist on Windows
        var usedLetters = _service.GetUsedLetters();

        // Assert
        usedLetters.Should().NotBeEmpty("at least C: drive should exist on Windows");
        usedLetters.Should().Contain('C');
    }

    [Fact]
    public void GetUsedLetters_ReturnsSortedAscending()
    {
        // Act
        var usedLetters = _service.GetUsedLetters();

        // Assert
        usedLetters.Should().BeInAscendingOrder();
    }

    [Fact]
    public void GetUsedLetters_ReturnsUppercaseLetters()
    {
        // Act
        var usedLetters = _service.GetUsedLetters();

        // Assert
        usedLetters.Should().AllSatisfy(c => char.IsUpper(c).Should().BeTrue());
    }

    [Fact]
    public void GetFreeLetters_ReturnsSortedDescending()
    {
        // Act
        var freeLetters = _service.GetFreeLetters();

        // Assert
        freeLetters.Should().BeInDescendingOrder("free letters should be sorted Z→A");
    }

    [Fact]
    public void GetFreeLetters_ReturnsOnlyUnusedLetters()
    {
        // Act
        var usedLetters = new HashSet<char>(_service.GetUsedLetters());
        var freeLetters = _service.GetFreeLetters();

        // Assert - no overlap
        foreach (var letter in freeLetters)
        {
            usedLetters.Should().NotContain(letter, $"'{letter}' is marked as free but also appears in used letters");
        }
    }

    [Fact]
    public void GetFreeLetters_CombinedWithUsedCoversAllLetters()
    {
        // Act
        var usedLetters = _service.GetUsedLetters();
        var freeLetters = _service.GetFreeLetters();

        // Assert - together they should cover A-Z
        var allLetters = usedLetters.Concat(freeLetters).OrderBy(c => c).ToList();
        var expectedLetters = Enumerable.Range('A', 26).Select(i => (char)i).ToList();

        allLetters.Should().BeEquivalentTo(expectedLetters);
    }

    [Fact]
    public void IsLetterAvailable_UsedLetter_ReturnsFalse()
    {
        // Arrange - C: is always used on Windows
        var usedLetters = new List<char> { 'C', 'D' };

        // Act
        var result = _service.IsLetterAvailable('C', usedLetters);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsLetterAvailable_FreeLetter_ReturnsTrue()
    {
        // Arrange - assume Z is free
        var usedLetters = new List<char> { 'C', 'D' };

        // Act
        var result = _service.IsLetterAvailable('Z', usedLetters);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsLetterAvailable_LowercaseLetter_WorksCaseInsensitive()
    {
        // Arrange
        var usedLetters = new List<char> { 'C', 'D' };

        // Act
        var resultUsed = _service.IsLetterAvailable('c', usedLetters);
        var resultFree = _service.IsLetterAvailable('z', usedLetters);

        // Assert
        resultUsed.Should().BeFalse("'c' should match 'C' in used letters");
        resultFree.Should().BeTrue("'z' should be free");
    }

    [Fact]
    public void IsLetterAvailable_InvalidCharacter_ReturnsFalse()
    {
        // Arrange
        var usedLetters = new List<char> { 'C' };

        // Act & Assert
        _service.IsLetterAvailable('1', usedLetters).Should().BeFalse("digits are not valid drive letters");
        _service.IsLetterAvailable('@', usedLetters).Should().BeFalse("symbols are not valid drive letters");
        _service.IsLetterAvailable(' ', usedLetters).Should().BeFalse("space is not a valid drive letter");
    }

    [Fact]
    public void IsLetterAvailable_BoundaryLetters_AreValid()
    {
        // Arrange
        var usedLetters = new List<char>();

        // Act & Assert
        _service.IsLetterAvailable('A', usedLetters).Should().BeTrue("'A' is a valid drive letter");
        _service.IsLetterAvailable('Z', usedLetters).Should().BeTrue("'Z' is a valid drive letter");
    }

    [Fact]
    public void IsLetterAvailable_OutOfRangeLetters_ReturnsFalse()
    {
        // Arrange
        var usedLetters = new List<char>();

        // Act & Assert - characters just outside A-Z range
        _service.IsLetterAvailable((char)('A' - 1), usedLetters).Should().BeFalse();
        _service.IsLetterAvailable((char)('Z' + 1), usedLetters).Should().BeFalse();
    }

    [Fact]
    public void IsLetterAvailable_NullUsedLetters_CallsGetUsedLetters()
    {
        // Act - when usedLetters is null, it should call GetUsedLetters() internally
        var result = _service.IsLetterAvailable('C', null);

        // Assert - C: should be in use on Windows
        result.Should().BeFalse("C: is always used on Windows");
    }

    [Fact]
    public void IsLetterAvailable_EmptyUsedLetters_AllLettersAvailable()
    {
        // Arrange
        var usedLetters = new List<char>();

        // Act & Assert
        for (char c = 'A'; c <= 'Z'; c++)
        {
            _service.IsLetterAvailable(c, usedLetters).Should().BeTrue($"'{c}' should be available when no letters are used");
        }
    }
}
