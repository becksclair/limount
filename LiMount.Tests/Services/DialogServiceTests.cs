using FluentAssertions;
using LiMount.App.Services;

namespace LiMount.Tests.Services;

/// <summary>
/// Unit tests for DialogService.
/// Note: These tests verify the service contract and behavior, but actual MessageBox display
/// cannot be tested in automated tests. In a real-world scenario, you would mock the
/// MessageBox calls or use UI automation testing.
/// </summary>
public class DialogServiceTests
{
    private readonly IDialogService _service;

    public DialogServiceTests()
    {
        _service = new DialogService();
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
