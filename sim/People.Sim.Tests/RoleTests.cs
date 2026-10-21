using People.Sim;
using Xunit;

public class RoleTests
{
    private static (World w, Group g, Group c, int leader) WithUnits()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => TestWorlds.Free(w, hh.Head[h].Index)).ToList();
        var g = w.FoundGroup(heads[0], GroupKind.Watch);
        foreach (int i in heads.Skip(1).Take(11)) w.JoinGroup(i, g);
        g.ShareRule = ShareRule.Treasury;
        var c = w.SplitUnit(g)!;
        w.RecountOrgs();
        return (w, g, c, heads[0]);
    }

    [Fact]
    public void RolesComeFromTheRegistry()
    {
        var (w, g, c, leader) = WithUnits();
        var p = w.People;
        int cmd = w.Members(c).First(m => p.Uid[m] == c.LeaderUid);
        Assert.Equal(RoleKind.Commander, w.RoleOf(cmd, c)!.Kind);
        Assert.Null(w.RoleOf(leader, g));   // 맨 위 우두머리는 직책이 없다
        int other = w.Members(c).First(m => m != cmd);
        Assert.Equal(RoleKind.Member, w.RoleOf(other, c)!.Kind);
        // 새 예외 규칙 하나로 배치를 막는다
        var member = w.RoleDefOf(RoleKind.Member);
        Assert.True(w.CanPlace(other, c, member));
        w.PlacementRules.Add((ww, i, u, r) => i != other);
        Assert.False(w.CanPlace(other, c, member));
    }

    [Fact]
    public void HigherWagesRaiseOrgLoyalty()
    {
        var (w, g, c, _) = WithUnits();
        var p = w.People;
        int poor = w.Members(g).First(m => p.Uid[m] != g.LeaderUid);
        int rich = w.Members(c).First(m => p.Uid[m] != c.LeaderUid);
        w.SetWage(g, RoleKind.Member, WageLevel.None);
        w.SetWage(c, RoleKind.Member, WageLevel.Lavish);
        for (int y = 0; y < 5; y++) { g.Treasury = 1000f; w.RecountBudget(); }
        Assert.True(w.OrgLoyalty(g, p.Uid[rich]) > 0.6f);
        Assert.True(w.OrgLoyalty(g, p.Uid[poor]) < 0.35f);
        Assert.True(w.Households.Coin[p.Household[rich]] > 0f);
    }

    [Fact]
    public void WagesFromTheLeaderBuildPersonalLoyalty()
    {
        var (w, g, c, _) = WithUnits();
        var p = w.People;
        int m = w.Members(c).First(x => p.Uid[x] != c.LeaderUid);
        float before = w.PersonalLoyalty(m, g.LeaderUid, 0f);
        w.SetWage(c, RoleKind.Member, WageLevel.Lavish);
        w.SetWageSource(g, true);
        int lh = p.Household[w.Members(g).First(x => p.Uid[x] == g.LeaderUid)];
        for (int y = 0; y < 3; y++) { w.Households.Coin[lh] = 1000f; w.RecountBudget(); }
        Assert.True(w.PersonalLoyalty(m, g.LeaderUid, 0f) > before + 0.1f);
        Assert.True(w.OrgLoyalty(g, p.Uid[m]) <= 0.31f);   // 조직 충성은 오르지 않는다
    }

    [Fact]
    public void AcceptModsApplyToEveryInteraction()
    {
        var (w, g, c, leader) = WithUnits();
        int other = w.Members(c).First(m => m != leader);
        var def = w.Def(InteractionId.Chat);
        var ctx = new InteractionContext { Actor = leader, Target = other, Third = -1 };
        w.AcceptMods.Add((ww, d, cc, acc) => 0f);
        Assert.Equal(0f, w.AcceptChance(def, ctx));
    }
}
