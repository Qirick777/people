using People.Sim;
using Xunit;

public class RecruitTests
{
    private static (World w, Group g, Group unit) WithUnits()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => TestWorlds.Free(w, hh.Head[h].Index)).ToList();
        var g = w.FoundGroup(heads[0], GroupKind.Watch);
        foreach (int i in heads.Skip(1).Take(10)) w.JoinGroup(i, g);
        w.RecountOrgs();
        var unit = w.SplitUnit(g)!;
        Assert.NotNull(unit);
        unit.Tasks.Clear();
        unit.Tasks.Add(new OrgTask { Verb = Verb.Recruit, Village = unit.Village, Weight = 1f });
        return (w, g, unit);
    }

    [Fact]
    public void LabelFollowsDestination()
    {
        var (w, g, unit) = WithUnits();
        Assert.Equal("포섭조", w.RecruitLabel(unit));
        w.SetRecruit(unit, g.Id, false);
        Assert.Equal("모병단", w.RecruitLabel(unit));
        w.SetRecruit(unit, -1, false);
        Assert.Equal("모집단", w.RecruitLabel(unit));
    }

    [Fact]
    public void RecruitsIntoTheChosenUnit()
    {
        var (w, g, unit) = WithUnits();
        w.SetRecruit(unit, g.Id, false);
        w.SetWage(g, RoleKind.Member, WageLevel.Lavish);
        int before = g.Members;
        for (int k = 0; k < 10; k++) w.RecruitNow(unit, 5f);
        Assert.True(g.Members > before, $"{before} → {g.Members}");
    }

    [Fact]
    public void ConscriptsDesertWhenDisloyal()
    {
        var (w, g, _) = WithUnits();
        var p = w.People;
        var outs = Enumerable.Range(0, p.HighWater).Where(i => p.IsAliveIndex(i) && (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear >= 16 && !w.GroupsOf(i).Any()).Take(10).ToList();
        foreach (int i in outs) w.MakeConscript(i, g);
        int n = outs.Count(i => w.IsConscript(i));
        Assert.True(n > 0);
        for (int y = 0; y < 15; y++) w.RecountConscripts();
        Assert.True(outs.Count(i => w.InGroup(i, g)) < n);
    }
}
