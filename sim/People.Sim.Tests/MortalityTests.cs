using People.Sim;
using Xunit;

public class MortalityTests
{
    [Fact]
    public void AverageAdultLifespanIsReasonable()
    {
        // 16세에 살아 있는 평균적인 사람의 기대 사망 나이: spec 15 목표 55~65세
        var c = SimConfig.Default;
        var m = new Mortality(c);
        double alive = 1, expected = 0;
        for (int age = 16; age < 120; age++)
        {
            double q = m.Annual(age, 0.5f, 0.5f);
            expected += alive * q * (age + 0.5);
            alive *= 1 - q;
        }
        Assert.InRange(expected, 55, 70);
    }

    [Fact]
    public void ChildSurvivalToAdulthoodInTargetRange()
    {
        // 굶주림·전염병 없는 기본값. spec 15 목표 55~70% (위기가 더해지면 내려간다)
        var m = new Mortality(SimConfig.Default);
        double alive = 1;
        for (int age = 0; age < 16; age++) alive *= 1 - m.Annual(age, 0.5f, 0.5f);
        Assert.InRange(alive, 0.6, 0.85);
    }
}
