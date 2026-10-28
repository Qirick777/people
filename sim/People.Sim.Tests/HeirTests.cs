using People.Sim;
using Xunit;

public class HeirTests
{
    [Fact]
    public void NamedHeirFromAnotherHouseTakesTheLordship()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        int lh = hh.Head[lord].Index;
        // 딴 집에 사는 혈족 가운데 하나를 고른다(제 집 가장이든 아니든)
        int t = Enumerable.Range(0, p.HighWater).FirstOrDefault(i => p.IsAliveIndex(i) && p.Household[i] != lord && w.CanNameHeir(lh, i), -1);
        if (t < 0) return;   // 딴 집에 혈족이 없는 세계
        int clients = w.ClientCount(lord);
        Assert.True(w.Interact(InteractionId.NameHeir, new InteractionContext { Actor = lh, Target = t, Third = -1 }).Accepted);
        int tu = p.Uid[t];
        w.DieNow(lh);
        int ti = Enumerable.Range(0, p.HighWater).First(i => p.IsAliveIndex(i) && p.Uid[i] == tu);
        int th = p.Household[ti];
        // 후계자가 본가의 가장이 되었거나, 후계자의 집이 지배권을 이었다
        bool headOfSeat = hh.IsAlive(lord) && hh.Head[lord].Index == ti;
        bool tookRealm = w.ClientCount(th) > 0;
        Assert.True(headOfSeat || tookRealm);
        if (clients > 0) Assert.True(w.ClientCount(th) > 0);
        // 아래 집이 이어도 섬김에 고리가 생기지 않는다
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h)) continue;
            int x = h, n = 0;
            while (x >= 0 && n < 40) { x = w.PatronOf(x); n++; }
            Assert.True(n < 40, "섬김에 고리가 생겼다");
        }
    }

    [Fact]
    public void StrangersCannotBeHeirs()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        int lh = hh.Head[lord].Index;
        int stranger = Enumerable.Range(0, p.HighWater).FirstOrDefault(i => p.IsAliveIndex(i) && p.Household[i] != lord && w.Lineage.Relatedness(p.Uid[i], p.Uid[lh]) <= 0f, -1);
        if (stranger >= 0) Assert.False(w.CanNameHeir(lh, stranger));
    }
}
