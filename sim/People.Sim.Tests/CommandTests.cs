using People.Sim;
using Xunit;

public class CommandTests
{
    /// <summary>지배자의 아래 집 가장 하나가 세운 조직과, 거기 든 다른 가장들.</summary>
    private static (World w, Group g, int leader) WithOrg(GroupKind kind, int members)
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => hh.Head[h].Index).Where(i => w.GroupsOf(i).Count() < World.GroupSlots).ToList();
        int leader = heads[0];
        var g = w.FoundGroup(leader, kind);
        foreach (int i in heads.Skip(1).Take(members)) w.JoinGroup(i, g);
        w.RecountOrgs();
        return (w, g, leader);
    }

    [Fact]
    public void IdentityFollowsWeightsAfterTwoYears()
    {
        var (w, g, _) = WithOrg(GroupKind.Watch, 4);
        w.SetUnitWeights(g, new[] { (Verb.Train, 1f) });
        w.RecountIdentity();
        Assert.Equal(GroupKind.Watch, g.Kind);   // 한 해로는 바뀌지 않는다
        w.RecountIdentity();
        Assert.Equal(GroupKind.Drill, g.Kind);
        Assert.EndsWith("수련단", g.Name);
    }

    [Fact]
    public void MixedWeightsGiveComboName()
    {
        var (w, g, _) = WithOrg(GroupKind.Brotherhood, 4);
        w.SetUnitWeights(g, new[] { (Verb.Preach, 0.5f), (Verb.Train, 0.5f) });
        w.RecountIdentity(); w.RecountIdentity();
        Assert.EndsWith("무장 신도회", g.Name);
    }

    [Fact]
    public void OverSpanHalvesTheExtra()
    {
        var (w, g, leader) = WithOrg(GroupKind.Watch, 11);
        w.RecountCommand();
        int span = w.CommandSpan(leader);
        int n = g.Members - 1;
        if (n > span)
        {
            Assert.True(g.Over > 0f);
            Assert.Equal((span + 0.5f * (n - span)) / n, g.CmdFactor, 3);
        }
        else Assert.Equal(1f, g.CmdFactor, 3);
    }

    [Fact]
    public void TreasurerStopsManagementLoss()
    {
        var (w, g, leader) = WithOrg(GroupKind.Watch, 11);
        w.RecountOrgs();
        if (g.Total <= w.CommandSpan(leader)) return;   // 우두머리가 다 챙길 수 있으면 새지 않는다
        float bare = w.ManageLoss(g);
        Assert.True(bare >= 0.05f);
        int t = w.Members(g).First(m => m != leader);
        Assert.True(w.AppointRole(g, 0, t));
        Assert.True(w.ManageLoss(g) < bare);
    }

    [Fact]
    public void CommanderCanBeVacatedAndGripStartsAtZero()
    {
        var (w, g, leader) = WithOrg(GroupKind.Watch, 11);
        var c = w.SplitUnit(g);
        Assert.NotNull(c);
        w.RecountCommand();
        Assert.Equal(0f, c!.Grip, 3);   // 갓 맡은 지휘자
        Assert.True(w.AppointCommander(c, -1));
        Assert.True(w.NoCommander(c));
        w.RecountCommand();
        Assert.Equal(0f, c.Grip, 3);
        Assert.False(w.AppointCommander(g, leader));   // 맨 위 조직은 우두머리가 지휘한다
    }
}
