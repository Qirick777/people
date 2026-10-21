using People.Sim;
using Xunit;

public class FactionTests
{
    /// <summary>지배자 아래 가장들로 만든 자경단 하나와 갈래 하나(지휘자 있음), 곳간지기. 번 것은 곳간에만 쌓는다.</summary>
    private static (World w, Group g, Group c, int treasurer) WithUnits()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => TestWorlds.Free(w, hh.Head[h].Index)).ToList();
        var g = w.FoundGroup(heads[0], GroupKind.Watch);
        foreach (int i in heads.Skip(1).Take(11)) w.JoinGroup(i, g);
        g.ShareRule = ShareRule.Treasury;
        g.LastIncome = 200f;   // 번 것이 있어야 녹봉을 바란다
        var c = w.SplitUnit(g)!;
        int t = w.Members(g).First(m => p.Uid[m] != g.LeaderUid);
        Assert.True(w.AppointRole(g, 0, t));
        w.RecountOrgs();
        return (w, g, c, t);
    }

    [Fact]
    public void BudgetFollowsWeights()
    {
        var (w, g, c, _) = WithUnits();
        float need = w.UnitNeed(g) + w.UnitNeed(c);
        Assert.True(w.UnitNeed(c) > 0f && w.UnitNeed(g) > 0f);
        g.Treasury = 0.5f * need;
        w.SetBudgetWeight(c, 4f);
        w.RecountBudget();
        Assert.True(c.Fill > g.Fill);
        Assert.True(g.Treasury < 0.01f);
    }

    [Fact]
    public void UnfundedRolesGrieveAndBandTogether()
    {
        var (w, g, c, t) = WithUnits();
        var p = w.People;
        for (int y = 0; y < 6; y++) { g.Treasury = 0f; w.RecountBudget(); }
        Assert.True(w.GrievanceOf(g, c.LeaderUid) > 0.5f);
        Assert.True(w.GrievanceOf(g, p.Uid[t]) > 0.5f);
        Assert.True(c.Fill < 0.5f);
        int cmd = Enumerable.Range(0, p.HighWater).First(i => p.IsAliveIndex(i) && p.Uid[i] == c.LeaderUid);
        Social.Add(p, cmd, p.Uid[t], 60f); Social.Add(p, t, c.LeaderUid, 60f);
        w.RecountFactions();
        var f = Assert.Single(w.FactionsOf(g));
        Assert.Equal(2, f.Members.Count);
        // 갈라치기: 한 사람에게 자리를 주면 불만이 준다
        float before = w.GrievanceOf(g, p.Uid[t]);
        Assert.True(w.Placate(g, p.Uid[t]));
        Assert.Equal(before - 0.3f, w.GrievanceOf(g, p.Uid[t]), 3);
        Assert.Contains(p.Uid[t], g.Inner);
    }

    [Fact]
    public void FundedRolesCoolDown()
    {
        var (w, g, c, _) = WithUnits();
        for (int y = 0; y < 3; y++) { g.Treasury = 0f; w.RecountBudget(); }
        float hot = w.GrievanceOf(g, c.LeaderUid);
        for (int y = 0; y < 2; y++) { g.Treasury = 1000f; w.RecountBudget(); }
        Assert.True(w.GrievanceOf(g, c.LeaderUid) < hot);
        Assert.Equal(1f, c.Fill, 3);
    }
}
