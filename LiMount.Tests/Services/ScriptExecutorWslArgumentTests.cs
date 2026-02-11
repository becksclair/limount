using FluentAssertions;
using LiMount.Core.Services;

namespace LiMount.Tests.Services;

public class ScriptExecutorWslArgumentTests
{
    [Fact]
    public void BuildWslArguments_ForLinuxCommand_PrependsExecuteFlag()
    {
        var arguments = ScriptExecutor.BuildWslArguments("lsblk -f -o NAME,FSTYPE -P", useExecuteFlag: true);

        arguments.Should().Be("-e lsblk -f -o NAME,FSTYPE -P");
    }

    [Fact]
    public void BuildWslArguments_ForRawArguments_DoesNotPrependExecuteFlag()
    {
        var arguments = ScriptExecutor.BuildWslArguments("-l -q", useExecuteFlag: false);

        arguments.Should().Be("-l -q");
    }

    [Fact]
    public void BuildWslArguments_WhitespaceCommand_ThrowsArgumentException()
    {
        var act = () => ScriptExecutor.BuildWslArguments("   ", useExecuteFlag: true);

        act.Should().Throw<ArgumentException>();
    }
}
