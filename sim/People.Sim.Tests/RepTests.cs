using People.Sim;
using Xunit;

public class RepTests
{
    private static (World w, Group g, int leader) WithOrg(GroupKind kind)
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => TestWorlds.Free(w, hh.Head[h].Index)).ToList();
        var g = w.FoundGroup(heads[0], kind);
        foreach (int i in heads.Skip(1).Take(8)) w.JoinGroup(i, g);
        w.RecountOrgs();
        return (w, g, heads[0]);
    }

    [Fact]
    public void BadNameRaisesAlarmAndGoodNameLowersIt()
    {
        var (w, g, _) = WithOrg(GroupKind.Watch);
        g.Fame = 1f; g.Standing = 0f;
        Assert.Equal(1f, w.AlarmMult(g), 3);
        g.Standing = -1f;
        Assert.Equal(3f, w.AlarmMult(g), 3);
        g.Standing = 1f;
        Assert.Equal(0.5f, w.AlarmMult(g), 3);
    }

    [Fact]
    public void RenownScalesRecruiting()
    {
        var (w, g, _) = WithOrg(GroupKind.Watch);
        g.Fame = 0f; g.Standing = -1f;
        Assert.Equal(1f, w.RecruitMult(g), 3);   // 아무도 모르면 효과가 없다
        g.Fame = 1f;
        Assert.Equal(0.2f, w.RecruitMult(g), 3);
        g.Standing = 1f;
        Assert.Equal(1.8f, w.RecruitMult(g), 3);
    }

    [Fact]
    public void LeaderTradeGetsTheNameBonus()
    {
        var (w, g, leader) = WithOrg(GroupKind.Company);
        int other = w.Members(g).First(m => m != leader);
        var def = new InteractionDef { Category = InteractionCategory.Trade, Accept = (_, _) => 0.5f };
        var ctx = new InteractionContext { Actor = leader, Target = other, Third = -1 };
        g.Fame = 0f; g.Standing = 0f;
        Assert.Equal(0.5f, w.AcceptChance(def, ctx), 3);
        g.Fame = 1f; g.Standing = 1f;
        Assert.Equal(0.65f, w.AcceptChance(def, ctx), 3);
    }

    [Fact]
    public void FameFollowsSizeAndVisibility()
    {
        var (w, g, _) = WithOrg(GroupKind.Watch);
        g.Visibility = Visibility.Hidden;
        g.Fame = 0.9f;
        w.RecountRep();
        Assert.True(g.Fame <= 0.1f);
    }
}
