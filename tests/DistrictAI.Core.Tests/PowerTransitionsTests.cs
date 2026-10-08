using Xunit;

namespace DistrictAI.Core.Tests;

public sealed class PowerTransitionsTests
{
    [Fact]
    public void ASleepIsPassedOnAndTheFirstWakeAfterItOnly()
    {
        var power = new PowerTransitions();
        Assert.Equal(PowerAction.Suspend, power.Next(PowerTransitions.ApmSuspend));
        // Someone at the machine: the automatic wake, then the one for the user.
        Assert.Equal(PowerAction.Resume, power.Next(PowerTransitions.ApmResumeAutomatic));
        Assert.Equal(PowerAction.None, power.Next(PowerTransitions.ApmResumeSuspend));
    }

    [Fact]
    public void AWakeWithNobodyAtTheMachineIsStillAWake()
    {
        var power = new PowerTransitions();
        Assert.Equal(PowerAction.Suspend, power.Next(PowerTransitions.ApmSuspend));
        Assert.Equal(PowerAction.Resume, power.Next(PowerTransitions.ApmResumeAutomatic));
        // The next sleep and wake, in the other order of the two wakes.
        Assert.Equal(PowerAction.Suspend, power.Next(PowerTransitions.ApmSuspend));
        Assert.Equal(PowerAction.Resume, power.Next(PowerTransitions.ApmResumeSuspend));
        Assert.Equal(PowerAction.None, power.Next(PowerTransitions.ApmResumeAutomatic));
    }

    [Fact]
    public void AWakeWithNoSleepBeforeItIsIgnored()
    {
        var power = new PowerTransitions();
        Assert.Equal(PowerAction.None, power.Next(PowerTransitions.ApmResumeAutomatic));
        Assert.Equal(PowerAction.None, power.Next(PowerTransitions.ApmResumeSuspend));
    }

    [Fact]
    public void ASecondSleepIsPassedOnToo()
    {
        var power = new PowerTransitions();
        Assert.Equal(PowerAction.Suspend, power.Next(PowerTransitions.ApmSuspend));
        Assert.Equal(PowerAction.Suspend, power.Next(PowerTransitions.ApmSuspend));
        Assert.Equal(PowerAction.Resume, power.Next(PowerTransitions.ApmResumeAutomatic));
    }

    [Theory]
    // PBT_APMPOWERSTATUSCHANGE, PBT_POWERSETTINGCHANGE, and nothing at all.
    [InlineData(0xAu)]
    [InlineData(0x8013u)]
    [InlineData(0u)]
    public void AnythingElseIsIgnored(uint type)
    {
        var power = new PowerTransitions();
        Assert.Equal(PowerAction.None, power.Next(type));
        Assert.Equal(PowerAction.Suspend, power.Next(PowerTransitions.ApmSuspend));
        Assert.Equal(PowerAction.None, power.Next(type));
        Assert.Equal(PowerAction.Resume, power.Next(PowerTransitions.ApmResumeAutomatic));
    }

    [Fact]
    public void TheWaitIsUnderWhatWindowsAllows() =>
        Assert.InRange(PowerTransitions.SuspendWait, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2) - TimeSpan.FromMilliseconds(1));
}
