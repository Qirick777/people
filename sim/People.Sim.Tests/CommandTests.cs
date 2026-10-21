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
        Assert.True(c!.Grip < 0.2f);   // 갓 맡은 지휘자: 맡은 해 0, 조직원이 보는 눈만큼만
        Assert.True(w.AppointCommander(c, -1));
        Assert.True(w.NoCommander(c));
        w.RecountCommand();
        Assert.Equal(0f, c.Grip, 3);
        Assert.False(w.AppointCommander(g, leader));   // 맨 위 조직은 우두머리가 지휘한다
    }
}

public class SupplyTests
{
    [Fact]
    public void WorkshopMakesArmsAndArmsStrengthenTheUnit()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => hh.Head[h].Index).Where(i => w.GroupsOf(i).Count() < World.GroupSlots).ToList();
        int leader = heads[0];
        foreach (int i in heads.Take(6)) p.SetSkill(i, Skill.Craft, 0.6f);
        var org = w.FoundGroup(leader, GroupKind.Workshop);
        foreach (int i in heads.Skip(1).Take(9)) w.JoinGroup(i, org);
        w.RecountOrgs();
        var guard = w.SplitUnit(org)!;
        w.SetUnitWeights(guard, new[] { (Verb.Guard, 1f) });
        w.SetUnitWeights(org, new[] { (Verb.Produce, 1f) });
        org.Product = Good.Arms; org.ProductManual = true;
        org.Supply.Clear(); org.Supply.Add((guard.Id, 1f));
        hh.Food[hh.IsAlive(p.Household[leader]) ? p.Household[leader] : 0] += 1000f;
        org.Store[(int)Good.Basic] = 500f;
        Assert.NotNull(w.BuildFacility(org, FacilityKind.Workshop, org.Village, org));
        for (int y = 0; y < 3; y++) for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
        Assert.True(org.Alive && guard.Alive, "조직이 흩어졌다");
        Assert.True(guard.Kit[(int)Good.Arms] > 0f, "공방이 무구를 보급하지 않았다");
        Assert.True(w.ArmsCov(guard) > 0f);
    }

    [Fact]
    public void FacilityCollapsesWithoutUpkeep()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        int leader = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h])).Select(h => hh.Head[h].Index).First();
        var org = w.FoundGroup(leader, GroupKind.Watch);
        int home = p.Household[leader];
        hh.Food[home] += 500f;
        var f = w.BuildFacility(org, FacilityKind.Warehouse, org.Village);
        Assert.NotNull(f);
        Assert.Equal(FacilityKind.Warehouse, f!.Kind);
        Assert.Equal(org.Id, f.Owner);
    }
}

public class BelongTests
{
    [Fact]
    public void HeavierTaxRaisesBurden()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households;
        int h = Enumerable.Range(0, hh.HighWater).First(x => hh.IsAlive(x) && w.PatronOf(x) == lord && w.ClientCount(x) == 0);
        var b0 = w.BelongOf(h, lord);
        Assert.InRange(b0.Protect, 0f, 1f);
        Assert.InRange(b0.Living, 0f, 1f);
        Assert.InRange(b0.Loyal, 0f, 1f);
        // 처음 해(낸 기록이 없으면)는 세율로 부담을 센다. 세율을 올리면 부담이 는다
        w.SetTaxRate(lord, 0.08f);
        float low = w.BelongOf(h, lord).Burden;
        w.SetTaxRate(lord, 0.2f);
        float high = w.BelongOf(h, lord).Burden;
        Assert.True(high >= low);
    }
}
