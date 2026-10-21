using People.Sim;
using Xunit;

public class CollectTests
{
    [Fact]
    public void ExpelRemovesARefusingSubject()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        int h = Enumerable.Range(0, hh.HighWater).First(x => hh.IsAlive(x) && w.PatronOf(x) == lord && w.ClientCount(x) == 0 && p.IsValid(hh.Head[x]));
        w.SetNonPay(lord, NonPay.Expel);
        p.Loyalty[hh.Head[h].Index] = 0f;
        for (int k = 0; k < 2000 && w.PatronOf(h) == lord; k++) w.TryCollect(h, lord, true, 10f);
        Assert.Equal(-1, w.PatronOf(h));
    }

    [Fact]
    public void UncoveredRefusalHasNoConsequence()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        int h = Enumerable.Range(0, hh.HighWater).First(x => hh.IsAlive(x) && w.PatronOf(x) == lord && w.ClientCount(x) == 0 && p.IsValid(hh.Head[x]));
        w.SetNonPay(lord, NonPay.Expel);
        p.Loyalty[hh.Head[h].Index] = 0f;   // 충성 0이면 스스로 내지 않는다
        for (int k = 0; k < 50; k++) Assert.False(w.TryCollect(h, lord, false, 10f));
        Assert.Equal(lord, w.PatronOf(h));   // 맡는 사람이 없으니 내쫓지도 못한다
    }

    [Fact]
    public void ForgivingRaisesLoyalSubjects()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        int h = Enumerable.Range(0, hh.HighWater).First(x => hh.IsAlive(x) && w.PatronOf(x) == lord && w.ClientCount(x) == 0 && p.IsValid(hh.Head[x]));
        int head = hh.Head[h].Index;
        p.LoyalUid[head] = p.Uid[hh.Head[lord].Index];   // 지배자를 섬긴다
        w.SetNonPay(lord, NonPay.Forgive);
        p.Loyalty[head] = 0.7f;
        hh.Food[h] = 0f; hh.Coin[h] = 0f;   // 형편이 어렵다
        bool refused = false;
        for (int k = 0; k < 2000 && !refused; k++) refused = !w.TryCollect(h, lord, true, 10f);
        Assert.True(refused, $"surplus {w.Surplus(h)} loyal {p.Loyalty[head]} dread {w.RepOfLord(lord).dread}");
        Assert.True(p.Loyalty[head] > 0.7f);
    }
}
