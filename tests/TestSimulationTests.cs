using ProductionTracker;
using Xunit;

public class TestSimulationTests
{
    [Theory]
    [InlineData(3.1,80,true)]
    [InlineData(3.5,100,true)]
    [InlineData(3.09,90,false)]
    [InlineData(3.51,90,false)]
    [InlineData(3.3,79.9,false)]
    public void TestAcceptanceRanges(double voltage,double signal,bool expected)
        => Assert.Equal(expected,TestSimulation.Passed(voltage,signal));
}
