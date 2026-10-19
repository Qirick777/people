namespace People.Sim;

/// <summary>후천 기술. 연습해야 오르고, 재능이 오를 수 있는 높이를 정한다.</summary>
public enum Skill : byte { Farming, Carpentry, Herbalism, Trade, Letters, Count }

/// <summary>
/// 하는 일. 농부가 기본이고, 나머지는 마을에 그 일을 맡길 수요가 있을 때만 생긴다.
/// 일을 맡은 사람은 밭일을 덜 한다(FarmShare).
/// </summary>
public enum Job : byte { Farmer, Carpenter, Healer, Trader, Count }

/// <summary>어릴 때 겪은 일. 유전되지 않고 성격 축을 조금 옮긴다.</summary>
[Flags]
public enum ExperienceTag : byte
{
    None = 0,
    HungryChildhood = 1,   // 어릴 때 오래 굶었다 → 욕심 +
    EarlyOrphan = 2,       // 12살 전에 부모를 잃었다 → 신중함 −, 욕심 +
    BigFamily = 4,         // 형제가 많은 집에서 자랐다 → 사교성 +
}

public static class Skills
{
    public static Skill SkillOf(Job j) => j switch
    {
        Job.Carpenter => Skill.Carpentry,
        Job.Healer => Skill.Herbalism,
        Job.Trader => Skill.Trade,
        _ => Skill.Farming,
    };

    /// <summary>타고난 재능 0~1. 기술마다 다른 특성 조합을 본다.</summary>
    public static float Talent(PersonStore p, int i, Skill s) => s switch
    {
        Skill.Farming => 0.5f * Math.Min(1f, p.Get(i, Trait.Strength)) + 0.5f * p.Get(i, Trait.Diligence),
        Skill.Carpentry => 0.7f * p.Get(i, Trait.Dexterity) + 0.3f * Math.Min(1f, p.Get(i, Trait.Strength)),
        Skill.Herbalism => 0.5f * p.Get(i, Trait.Perception) + 0.5f * p.Get(i, Trait.Intelligence),
        Skill.Trade => 0.4f * p.Get(i, Trait.Charisma) + 0.4f * p.Get(i, Trait.Intelligence) + 0.2f * p.Get(i, Trait.Sociability),
        Skill.Letters => p.Get(i, Trait.Intelligence),
        _ => 0.5f,
    };

    /// <summary>혼자 연습해서 오를 수 있는 높이. 스승에게 배우면 이보다 조금 더 오른다.</summary>
    public static float Cap(float talent) => 0.25f + 0.75f * talent;

    /// <summary>
    /// 한 해 연습 계산. 혼자서는 재능 높이(cap)까지 다가가고, 스승(teacher ≥ 0)이 더 잘하면 스승 쪽으로 다가간다.
    /// 스승에게 배우면 재능 높이를 넘어 스승의 90%까지 오를 수 있다.
    /// </summary>
    public static float LearnStep(float v, float cap, float selfMult, float teachMult, float teacher, float learnRate, float teachRate)
    {
        if (v < cap) v += learnRate * selfMult * (cap - v);
        if (teacher >= 0)
        {
            float top = MathF.Max(cap, 0.9f * teacher);
            if (teacher > v && v < top) v = MathF.Min(top, v + teachRate * teachMult * (teacher - v));
        }
        return Math.Clamp(v, 0f, 1f);
    }

    /// <summary>나이에 따른 배우는 속도. 어릴수록 빠르다.</summary>
    public static float AgeLearn(int age) => age switch { < 8 => 0f, <= 15 => 1.5f, <= 30 => 1f, <= 50 => 0.6f, _ => 0.3f };

    public static string JobName(Job j) => j switch
    {
        Job.Carpenter => "목수", Job.Healer => "약초꾼", Job.Trader => "장사꾼", _ => "농부",
    };

    public static string SkillName(Skill s) => s switch
    {
        Skill.Farming => "농사", Skill.Carpentry => "목공", Skill.Herbalism => "약초", Skill.Trade => "장사", Skill.Letters => "글",
        _ => "?",
    };

    /// <summary>
    /// 성격 이름표. 축 위치를 읽기만 하고 효과는 없다. 가운데에서 충분히 떨어진 축만 이름을 붙인다.
    /// </summary>
    public static List<string> Labels(PersonStore p, int i)
    {
        var l = new List<string>();
        float d = p.Get(i, Trait.Diligence) - 0.5f, c = p.Get(i, Trait.Caution) - 0.5f;
        const float t = 0.1f;
        if (MathF.Abs(d) > t || MathF.Abs(c) > t)
        {
            if (d >= 0 && c >= 0) l.Add("부지런함");
            else if (d >= 0) l.Add("성급함");
            else if (c >= 0) l.Add("꼼꼼함");
            else l.Add("게으름");
        }
        Axis(l, p.Get(i, Trait.Sociability), "붙임성", "외톨이");
        Axis(l, p.Get(i, Trait.Aggression), "거칢", "온순함");
        Axis(l, p.Get(i, Trait.Ambition), "욕심", "무던함");
        var e = (ExperienceTag)p.Experience[i];
        if ((e & ExperienceTag.HungryChildhood) != 0) l.Add("굶주린 어린 시절");
        if ((e & ExperienceTag.EarlyOrphan) != 0) l.Add("일찍 부모를 잃음");
        if ((e & ExperienceTag.BigFamily) != 0) l.Add("대가족");
        return l;
    }

    private static void Axis(List<string> l, float v, string hi, string lo)
    {
        if (v > 0.62f) l.Add(hi);
        else if (v < 0.38f) l.Add(lo);
    }
}
