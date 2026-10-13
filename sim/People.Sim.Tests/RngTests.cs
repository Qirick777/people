using People.Sim;
using Xunit;

public class RngTests
{
    [Fact]
    public void SameSeedSameSequence()
    {
        var a = new Rng(42);
        var b = new Rng(42);
        for (int i = 0; i < 1000; i++) Assert.Equal(a.NextU64(), b.NextU64());
    }

    [Fact]
    public void FloatInRange()
    {
        var r = new Rng(1);
        for (int i = 0; i < 100000; i++)
        {
            float f = r.NextFloat();
            Assert.InRange(f, 0f, 0.9999999f);
        }
    }
}
