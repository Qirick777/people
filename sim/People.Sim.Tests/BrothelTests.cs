using People.Sim;
using Xunit;

public class BrothelTests
{
    private static (World w, Group g, List<int> heads) WithBrothel()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => TestWorlds.Free(w, hh.Head[h].Index)).Where(i => Age(w, i) >= 16).ToList();
        var g = w.FoundGroup(heads[0], GroupKind.Brothel);
        g.Tasks.Clear();
        g.Tasks.Add(new OrgTask { Verb = Verb.Brothel, Village = g.Village, Weight = 1f });
        return (w, g, heads);
    }

    private static int Age(World w, int i) => (w.Day - w.People.BirthDay[i]) / w.Config.DaysPerYear;

    [Fact]
    public void RolesFollowTheWork()
    {
        var (w, g, heads) = WithBrothel();
        int a = heads[1];
        w.JoinGroup(a, g);
        w.People.JobOf[a] = (byte)Job.SexWorker;
        Assert.Equal(RoleKind.Worker, w.RoleOf(a, g)!.Kind);
        int b = heads[2];
        w.JoinGroup(b, g);
        Assert.True(w.RoleOf(b, g)!.Kind is RoleKind.Tout or RoleKind.Bouncer);
    }

    [Fact]
    public void MinorsAndCaptivesAreNotPlacedInBrothelWork()
    {
        var (w, g, heads) = WithBrothel();
        var p = w.People;
        int kid = Enumerable.Range(0, p.HighWater).First(k => p.IsAliveIndex(k) && Age(w, k) is >= 5 and < 16);
        Assert.False(w.CanPlace(kid, g, w.RoleDefOf(RoleKind.Member)));
        int free = heads.Skip(1).First(i => w.DebtOf(p.Household[i]) <= 0f);
        Assert.True(w.CanPlace(free, g, w.RoleDefOf(RoleKind.Member)));
    }

    [Fact]
    public void WorkersHearThings()
    {
        var (w, g, heads) = WithBrothel();
        foreach (int i in heads.Skip(1).Take(4)) { w.JoinGroup(i, g); w.People.JobOf[i] = (byte)Job.SexWorker; }
        w.SetBrothel(g, 4, 0.8f);
        for (int k = 0; k < 15; k++) w.BrothelNow(g);
        Assert.True(w.Stats.BrothelClients > 0);
        Assert.NotEmpty(w.IntelOf(g));
    }

    [Fact]
    public void RecruitsOnlyFreeAdults()
    {
        var (w, g, heads) = WithBrothel();
        // 같은 세력 갈래로 모집 갈래를 만든다
        foreach (int i in heads.Skip(1).Take(10)) w.JoinGroup(i, g);
        var rec = w.SplitUnit(g);
        Assert.NotNull(rec);
        rec!.Tasks.Clear();
        rec.Tasks.Add(new OrgTask { Verb = Verb.Recruit, Village = rec.Village, Weight = 1f });
        w.SetRecruit(rec, g.Id, false);
        Assert.Equal("일꾼 모집", w.RecruitLabel(rec));
        for (int k = 0; k < 20; k++) w.RecruitNow(rec, 5f);
        Assert.All(w.Members(g), m => Assert.True(Age(w, m) >= 16 && !w.IsCaptive(m)));
    }
}
