using People.Sim;
using Xunit;

public class PersonStoreTests
{
    [Fact]
    public void ReusedSlotInvalidatesOldId()
    {
        var s = new PersonStore(4);
        var a = s.Add(true, 0, PersonId.None, PersonId.None, 0);
        s.Remove(a.Index);
        var b = s.Add(false, 0, PersonId.None, PersonId.None, 0);
        Assert.Equal(a.Index, b.Index);
        Assert.False(s.IsValid(a));
        Assert.True(s.IsValid(b));
    }

    [Fact]
    public void GrowsPastCapacity()
    {
        var s = new PersonStore(2);
        for (int i = 0; i < 100; i++) s.Add(i % 2 == 0, 0, PersonId.None, PersonId.None, 0);
        Assert.Equal(100, s.AliveCount);
    }
}
