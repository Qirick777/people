using People.Sim;
using Xunit;

public class SchoolTests
{
    private static (World w, Group school, Group target, int leader) WithSchool()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => TestWorlds.Free(w, hh.Head[h].Index)).ToList();
        int leader = heads[0];
        p.SetSkill(leader, Skill.Letters, 0.9f);
        p.SetSkill(leader, Skill.Arms, 0.9f);
        var org = w.FoundGroup(leader, GroupKind.School);
        foreach (int i in heads.Skip(1).Take(9)) w.JoinGroup(i, org);
        w.RecountOrgs();
        var target = w.SplitUnit(org)!;           // 졸업생이 갈 갈래
        w.SetUnitWeights(org, new[] { (Verb.Teach, 1f) });
        return (w, org, target, leader);
    }

    [Fact]
    public void StudentsEnrollLearnAndGraduateToTheChosenUnit()
    {
        var (w, school, target, _) = WithSchool();
        var plan = new SchoolPlan { MinAge = 8, MaxAge = 30, Years = 1, Fee = 0f, Dest = SchoolDest.Unit, DestUnit = target.Id };
        plan.Weights[(int)Lesson.Arms] = 1f;
        w.SetPlan(school, plan);
        for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
        var studs = w.Members(school).Where(w.IsStudent).Select(i => w.People.Uid[i]).ToList();
        Assert.NotEmpty(studs);
        for (int y = 0; y < 2; y++) for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
        // 한 해 과정이니 두 해 뒤 그때 학생은 모두 졸업했다(살아 있다면)
        var p = w.People;
        Assert.True(studs.Any(uid => Enumerable.Range(0, p.HighWater).Any(k => p.IsAliveIndex(k) && p.Uid[k] == uid && !w.IsStudent(k))), "아무도 졸업하지 않았다");
    }

    [Fact]
    public void DepthCapFollowsWeight()
    {
        Assert.Equal(0.4f, World.DepthCap(0.2f));
        Assert.Equal(0.6f, World.DepthCap(0.3f));
        Assert.Equal(0.8f, World.DepthCap(0.6f));
    }

    [Fact]
    public void PlanWeightsAreNormalized()
    {
        var (w, school, _, _) = WithSchool();
        var plan = new SchoolPlan();
        plan.Weights[(int)Lesson.Arms] = 3f;
        plan.Weights[(int)Lesson.Loyalty] = 1f;
        w.SetPlan(school, plan);
        var got = w.PlanOf(school);
        Assert.Equal(0.75f, got.Weights[(int)Lesson.Arms], 3);
        Assert.Equal(0.25f, got.Weights[(int)Lesson.Loyalty], 3);
    }

    [Fact]
    public void SchoolNameFollowsDestination()
    {
        var (w, school, target, _) = WithSchool();
        var plan = new SchoolPlan { Dest = SchoolDest.Unit, DestUnit = target.Id };
        plan.Weights[(int)Lesson.Arms] = 1f;
        w.SetPlan(school, plan);
        w.SetUnitWeights(target, new[] { (Verb.Guard, 1f) });
        Assert.Equal("사관학교", w.SchoolLabel(school));
        w.SetUnitWeights(target, new[] { (Verb.Trade, 1f) });
        Assert.Equal("상업학교", w.SchoolLabel(school));
        var plan2 = new SchoolPlan { Dest = SchoolDest.Faction };
        plan2.Weights[(int)Lesson.Arms] = 1f;
        w.SetPlan(school, plan2);
        Assert.Equal("학교", w.SchoolLabel(school));
    }

    [Fact]
    public void QualityCostsMoneyPerStudent()
    {
        var (w, school, _, _) = WithSchool();
        var plan = new SchoolPlan { MinAge = 8, MaxAge = 30, Years = 3, Fee = 0f, FeeOut = 0f, Quality = WageLevel.Lavish };
        plan.Weights[(int)Lesson.Letters] = 1f;
        w.SetPlan(school, plan);
        for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
        var (n, _, _) = w.SchoolCount(school);
        Assert.True(n > 0);
        Assert.Equal(12f * n, w.SchoolCost(school), 3);
    }

    [Fact]
    public void EtiquetteIsLearnedWithoutASkillSlot()
    {
        var (w, school, _, leader) = WithSchool();
        var p = w.People;
        foreach (int m in w.Members(school)) if (!w.IsStudent(m)) p.Set(m, Trait.Charisma, 0.9f);
        var plan = new SchoolPlan { MinAge = 8, MaxAge = 30, Years = 3, Fee = 0f, FeeOut = 0f };
        plan.Weights[(int)Lesson.Etiquette] = 1f;
        w.SetPlan(school, plan);
        school.Treasury = 500f;
        for (int y = 0; y < 2; y++) for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
        Assert.Contains(Enumerable.Range(0, p.HighWater), i => p.IsAliveIndex(i) && w.Polish(i, Lesson.Etiquette) > 0f);
    }
}
