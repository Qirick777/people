namespace People.Sim;

/// <summary>나이별 연간 사망률 (spec 2.2). 하루 확률로 바꿔 미리 표로 만든다.</summary>
public sealed class Mortality
{
    private readonly float[] _dailyByAge;   // 나이(해)별 기본 하루 사망 확률
    private readonly SimConfig _c;

    public Mortality(SimConfig c, int maxAge = 120)
    {
        _c = c;
        _dailyByAge = new float[maxAge + 1];
        for (int age = 0; age <= maxAge; age++)
        {
            _dailyByAge[age] = 1f - MathF.Pow(1f - Annual(age, 0.5f, 0.5f), 1f / c.DaysPerYear);
        }
    }

    /// <summary>연간 사망률. longevity, constitution 은 표현형 0~1.</summary>
    public float Annual(int age, float longevity, float constitution)
    {
        float baseRate = age switch
        {
            <= 1 => _c.MortalityInfant,
            <= 4 => _c.MortalityToddler,
            <= 15 => _c.MortalityChild,
            < 40 => _c.MortalityAdult,
            _ => _c.MortalityAdult * MathF.Pow(_c.MortalityAgingBase, age - 40) * (1.4f - 0.8f * longevity),
        };
        return Math.Clamp(baseRate * (1.5f - constitution), 0f, 1f);
    }

    /// <summary>하루 사망 확률. 표의 기본값에 개인 보정과 상황 보정을 곱한다.</summary>
    public float Daily(int age, float longevity, float constitution, float situational = 1f)
    {
        if (age >= _dailyByAge.Length) return 1f;
        float personal = (1.5f - constitution) / 1.0f;
        if (age >= 40) personal *= (1.4f - 0.8f * longevity) / 1.0f;
        return Math.Clamp(_dailyByAge[age] * personal * situational, 0f, 1f);
    }
}
