using People.Sim;
using Xunit;

public class SecedeTests
{
    private static int Age(World w, int i) => (w.Day - w.People.BirthDay[i]) / w.Config.DaysPerYear;

    private static (World w, Group g, Group unit, List<int> heads) WithSplit()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => TestWorlds.Free(w, hh.Head[h].Index)).Where(i => Age(w, i) >= 16).ToList();
        var g = w.FoundGroup(heads[0], GroupKind.Watch);
        foreach (int i in heads.Skip(1).Take(10)) w.JoinGroup(i, g);
        w.RecountOrgs();
        var unit = w.SplitUnit(g)!;
        Assert.NotNull(unit);
        return (w, g, unit, heads);
    }

    [Fact]
    public void LoyalToTheOldFactionStayBehind()
    {
        var (w, g, unit, _) = WithSplit();
        var p = w.People;
        var ms = w.Members(unit).Where(m => p.Uid[m] != unit.LeaderUid).ToList();
        int loyal = ms[0];
        w.SetOrgLoyaltyNow(g, p.Uid[loyal], 1f);
        // 갈래를 떼어 낸다
        unit.Parent = -1;
        var (stayed, _) = w.SortNow(g, new List<Group> { unit }, unit, unit.LeaderUid);
        Assert.True(stayed >= 1);
        Assert.False(w.InGroup(loyal, unit));
        Assert.True(w.InGroup(loyal, g));
    }

    [Fact]
    public void ThoseWhoLoveTheRebelCrossOver()
    {
        var (w, g, unit, heads) = WithSplit();
        var p = w.People;
        int fan = w.Members(g).First(m => p.Uid[m] != g.LeaderUid && !w.InGroup(m, unit));
        Social.Add(p, fan, unit.LeaderUid, 100f);
        Social.Add(p, fan, unit.LeaderUid, 100f);
        unit.Parent = -1;
        var (_, defected) = w.SortNow(g, new List<Group> { unit }, unit, unit.LeaderUid);
        Assert.True(defected >= 1);
        Assert.True(w.InGroup(fan, unit));
    }

    [Fact]
    public void ZealotsHoldTheirSide()
    {
        var (w, g, unit, _) = WithSplit();
        var p = w.People;
        int m = w.Members(g).First(x => p.Uid[x] != g.LeaderUid);
        Assert.InRange(w.SideLeanNow(m, unit.LeaderUid, g.LeaderUid, g), -2f, 3f);
    }

    [Fact]
    public void AbsorbDissolvesTheOldOrg()
    {
        var (w, g, _, heads) = WithSplit();
        var p = w.People;
        // 옛 조직 나무 밖의 어른 하나가 새 조직을 세운다
        int boss = TestWorlds.Free(w, Enumerable.Range(0, p.HighWater).Last(i => p.IsAliveIndex(i) && Age(w, i) >= 16 && !w.GroupsOf(i).Any(x => w.RootOf(x).Id == g.Id)));
        var mine = w.FoundGroup(boss, GroupKind.Watch);
        var olds = w.Members(g).ToList();
        w.AbsorbNow(g, mine);
        Assert.False(g.Alive);
        int moved = olds.First(m => w.InGroup(m, mine));
        Assert.Equal(0.2f, w.OrgLoyalty(mine, p.Uid[moved]), 3);
    }
}
