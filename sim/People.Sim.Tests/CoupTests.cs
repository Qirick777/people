using People.Sim;
using Xunit;

public class CoupTests
{
    private static (World w, int lord, int vassal) WithVassal()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        w.RecountOrgs();
        w.LordOrg(lord);
        int k = Enumerable.Range(0, hh.HighWater).First(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]) && w.ClientCount(h) == 0);
        Assert.True(w.Enfeoff(lord, k, -1, 5) >= 5);
        return (w, lord, k);
    }

    private static void NoLoops(World w)
    {
        var hh = w.Households;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h)) continue;
            int x = h, n = 0;
            while (x >= 0 && n < 40) { x = w.PatronOf(x); n++; }
            Assert.True(n < 40, "섬김에 고리가 생겼다");
        }
        Assert.False(w.HasOrgLoop(), "조직 위아래에 고리가 생겼다");
    }

    [Fact]
    public void StrikeEndsOneOfThreeWays()
    {
        var (w, lord, v) = WithVassal();
        var hh = w.Households; var p = w.People;
        int vh = hh.Head[v].Index, lh = hh.Head[lord].Index;
        Assert.True(w.CoupFoothold(vh, lord));
        var r = w.Interact(InteractionId.PlotCoup, new InteractionContext { Actor = vh, Target = lh, Third = -1 });
        Assert.True(r.Accepted);
        var plot = w.Plots.Single(x => x.PlotterHouse == v);
        int below = w.ClientCount(lord);
        int w0 = w.Stats.CoupWins, c0 = w.Stats.CivilWars, f0 = w.Stats.CoupFails;
        Assert.False(w.CanStrike(plot));   // 마음먹은 해에는 일으키지 못한다
        plot.Since -= w.Config.DaysPerYear;
        w.Strike(plot);
        Assert.Empty(w.Plots.Where(x => x.PlotterHouse == v));
        // 거사는 분쟁이 되어 하루(약 보름)마다 이어진다. 반년(12번)이면 끝난다
        for (int k = 0; k < 20 && w.Conflicts.Any(c => !c.Over); k++) w.TickConflicts();
        Assert.All(w.Conflicts, c => Assert.True(c.Over));
        NoLoops(w);
        var st = w.Stats;
        Assert.Equal(1, st.CoupWins - w0 + st.CivilWars - c0 + st.CoupFails - f0);
        if (st.CoupWins > w0)
        {
            Assert.True(w.ClientCount(v) >= below - 2, "자리를 빼앗았으면 따르는 집을 넘겨받는다");
            Assert.Equal(Legitimacy.Self, w.LegitOf(v));
        }
        else if (st.CivilWars > c0)
        {
            Assert.Equal(-1, w.PatronOf(v));
            Assert.Equal(Legitimacy.Self, w.LegitOf(v));
        }
        else
        {
            // 실패: 처형되었거나 쫓겨났다
            bool dead = !p.IsAliveIndex(vh) || p.Household[vh] != v;
            Assert.True(dead || w.PatronOf(v) != lord);
        }
    }

    [Fact]
    public void DeclareEndsFreeOrStillUnder()
    {
        var (w, lord, v) = WithVassal();
        var hh = w.Households;
        int vh = hh.Head[v].Index, lh = hh.Head[lord].Index;
        w.Interact(InteractionId.Declare, new InteractionContext { Actor = vh, Target = lh, Third = -1 });
        // 주군이 진압을 고르면 분쟁이 선다(C2): 한 해 안에 끝난다
        for (int k = 0; k < 30 && w.Conflicts.Any(c => !c.Over); k++) w.TickConflicts();
        Assert.All(w.Conflicts, c => Assert.True(c.Over));
        NoLoops(w);
        // 끝나면 홀로 섰거나(정당성: 스스로 섬) 여전히 주군 아래다
        if (w.PatronOf(v) == -1) Assert.Equal(Legitimacy.Self, w.LegitOf(v));
        else Assert.Equal(lord, w.PatronOf(v));
    }

    [Fact]
    public void OnlyInsidersCanBeRecruited()
    {
        var (w, lord, v) = WithVassal();
        var hh = w.Households; var p = w.People;
        int vh = hh.Head[v].Index, lh = hh.Head[lord].Index;
        w.Interact(InteractionId.PlotCoup, new InteractionContext { Actor = vh, Target = lh, Third = -1 });
        // 세력 밖 사람(다른 꼭대기의 집 가장)에게는 쓸 수 없다
        int outsider = Enumerable.Range(0, hh.HighWater).FirstOrDefault(h => hh.IsAlive(h) && w.TopOf(h) != w.TopOf(lord) && p.IsValid(hh.Head[h]), -1);
        if (outsider >= 0)
            Assert.False(w.Def(InteractionId.PlotRecruit).Can(w, new InteractionContext { Actor = vh, Target = hh.Head[outsider].Index, Third = -1 }));
        // 지배자 자신에게도 쓸 수 없다
        Assert.False(w.Def(InteractionId.PlotRecruit).Can(w, new InteractionContext { Actor = vh, Target = lh, Third = -1 }));
    }
}

public class InspectorTests
{
    [Fact]
    public void LordWithVassalsKeepsAnInspectorUnitThatSees()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        w.RecountOrgs();
        var root = w.LordOrg(lord)!;
        int k = Enumerable.Range(0, hh.HighWater).First(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]) && w.ClientCount(h) == 0);
        Assert.True(w.Enfeoff(lord, k, -1, 5) >= 5);
        for (int y = 0; y < 3; y++) for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
        root = w.LordOrg(lord, false);
        if (root == null || w.ClientCount(k) == 0) return;   // 그 사이 세력이 바뀐 세계
        var unit = w.ChildrenOf(root).FirstOrDefault(c => c.Inspect);
        if (unit == null) return;   // 조직이 여덟 사람이 안 되는 세계
        Assert.True(w.Members(unit).Count >= 2);
        Assert.True(w.SurveyOf(root) > 0f, "감찰대가 있으면 감시 세기가 0보다 크다");
    }
}
