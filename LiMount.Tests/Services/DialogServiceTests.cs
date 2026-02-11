using FluentAssertions;
using LiMount.WinUI.Services;
using LiMount.Core.Abstractions;
using Moq;

namespace LiMount.Tests.Services;

/// <summary>
/// Unit tests for DialogService.
/// Note: These tests verify the service contract and behavior, but actual ContentDialog display
/// cannot be tested in automated tests. In a real-world scenario, you would use UI automation testing.
/// </summary>
public class DialogServiceTests
{
    private readonly IDialogService _service;

    public DialogServiceTests()
    {
        var mockXamlRootProvider = new Mock<IXamlRootProvider>();
        _service = new DialogService(mockXamlRootProvider.Object);
    }

    [Fact]
    public void DialogService_ImplementsIDialogService()
    {
        // Assert
        _service.Should().BeAssignableTo<IDialogService>();
    }

    [Fact]
    public void DialogService_HasConfirmAsyncMethod()
    {
        // Assert
        var method = typeof(DialogService).GetMethod(nameof(IDialogService.ConfirmAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<bool>));
    }

    [Fact]
    public void DialogService_HasShowErrorAsyncMethod()
    {
        // Assert
        var method = typeof(DialogService).GetMethod(nameof(IDialogService.ShowErrorAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
    }

    [Fact]
    public void DialogService_HasShowInfoAsyncMethod()
    {
        // Assert
        var method = typeof(DialogService).GetMethod(nameof(IDialogService.ShowInfoAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
    }

    [Fact]
    public void DialogService_HasShowWarningAsyncMethod()
    {
        // Assert
        var method = typeof(DialogService).GetMethod(nameof(IDialogService.ShowWarningAsync));
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
    }

    // Note: Testing actual dialog display requires UI automation or manual testing
    // These tests verify the service contract exists and is properly structured
}
