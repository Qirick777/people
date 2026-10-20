using People.Sim;
using Xunit;

/// <summary>시험용 세계. 역사가 바뀌어도 시험이 깨지지 않게, 지배자가 생길 때까지 여러 시드를 차례로 돌린다.</summary>
public static class TestWorlds
{
    public static (World w, int lord) WithLord(int minClients, int years = 300)
    {
        foreach (ulong seed in new ulong[] { 5, 3, 7, 1, 11, 13 })
        {
            var w = new World(seed, 400);
            w.SeedCouples(5);
            var hh = w.Households;
            for (int y = 0; y < years; y++)
            {
                for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
                for (int h = 0; h < hh.HighWater; h++)
                    if (hh.IsAlive(h) && w.ClientCount(h) >= minClients && w.People.IsValid(hh.Head[h])) return (w, h);
            }
        }
        Assert.Fail($"따르는 집 {minClients}곳이 넘는 지배자가 어느 시드에서도 생기지 않았다");
        return default;
    }
}
