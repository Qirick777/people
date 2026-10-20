using People.Sim;
using Xunit;

public class OrgTests
{
    private static int Years(World w, int i) => (w.Day - w.People.BirthDay[i]) / w.Config.DaysPerYear;

    [Fact]
    public void SplitMakesChildUnderParent()
    {
        var w = new World(3, 400);
        w.SeedCouples(5);
        for (int d = 0; d < 60 * w.Config.DaysPerYear; d++) w.Step();
        var p = w.People;
        var adults = Enumerable.Range(0, p.HighWater).Where(i => p.IsAliveIndex(i) && Years(w, i) >= 16 && w.GroupsOf(i).Count() < World.GroupSlots).Take(11).ToList();
        Assert.True(adults.Count >= 11);
        var g = w.FoundGroup(adults[0], GroupKind.Watch);
        foreach (int i in adults.Skip(1)) w.JoinGroup(i, g);
        var c = w.SplitUnit(g);
        Assert.NotNull(c);
        Assert.Equal(g.Id, c!.Parent);
        Assert.Same(g, w.RootOf(c));
        Assert.True(c.Members >= 4);
        Assert.Equal(11, g.Members + c.Members);
        Assert.True(w.IsLeader(adults[0], g));   // 우두머리는 위 조직에 남는다
    }

    [Fact]
    public void EnfeoffThenTakeBackOrBreakAway()
    {
        var w = new World(5, 400);
        w.SeedCouples(5);
        var hh = w.Households;
        int lord = -1;
        for (int y = 0; y < 300 && lord < 0; y++)
        {
            for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
            for (int h = 0; h < hh.HighWater; h++)
                if (hh.IsAlive(h) && w.ClientCount(h) >= 8) { lord = h; break; }
        }
        Assert.True(lord >= 0, "따르는 집 여덟이 넘는 집이 생겨야 한다");
        var mine = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord).ToList();
        int vassal = mine[0];
        int before = w.ClientCount(lord);
        int n = w.Enfeoff(lord, vassal, -1, 4);
        Assert.True(n >= 1);
        Assert.Equal(lord, w.PatronOf(vassal));
        Assert.Equal(n, w.ClientCount(vassal));
        Assert.True(w.IsVassalOf(vassal, lord));
        Assert.Equal(World.LegitName(Legitimacy.Granted), World.LegitName(w.LegitOf(vassal)));
        // 거두면 집이 돌아온다
        w.TakeBack(lord, vassal);
        Assert.Equal(0, w.ClientCount(vassal));
        Assert.Equal(before, w.ClientCount(lord));
        // 다시 맡기고 거부하면 집을 쥔 채 떨어져 나간다
        n = w.Enfeoff(lord, vassal, -1, 4);
        w.BreakAway(lord, vassal);
        Assert.Equal(-1, w.PatronOf(vassal));
        Assert.Equal(n, w.ClientCount(vassal));
    }
}

public class OrgNameTests
{
    [Fact]
    public void ChosenNameStaysAndBranchesFollow()
    {
        var w = new World(3, 400);
        w.SeedCouples(5);
        for (int d = 0; d < 60 * w.Config.DaysPerYear; d++) w.Step();
        var p = w.People;
        var adults = Enumerable.Range(0, p.HighWater).Where(i => p.IsAliveIndex(i) && (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear >= 16 && w.GroupsOf(i).Count() < World.GroupSlots).Take(11).ToList();
        var g = w.FoundGroup(adults[0], GroupKind.Watch);
        foreach (int i in adults.Skip(1)) w.JoinGroup(i, g);
        var c = w.SplitUnit(g)!;
        w.RenameGroup(g, "  북쪽 개울 형제단\n");
        Assert.Equal("북쪽 개울 형제단", g.Name);
        Assert.True(g.NameFixed);
        Assert.Equal("북쪽 개울 형제단 1갈래", c.Name);
        w.RenameGroup(g, new string('가', 40));
        Assert.Equal(20, g.Name.Length);
        for (int d = 0; d < 2 * w.Config.DaysPerYear; d++) w.Step();
        Assert.Equal(new string('가', 20), g.Name);   // 해가 바뀌어도 지은 이름은 남는다
    }
}
