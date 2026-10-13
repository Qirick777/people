namespace People.Sim;

public enum Season : byte { Spring, Summer, Autumn, Winter }

/// <summary>시뮬레이션 날짜. 0일 = 1년 봄 1일.</summary>
public readonly record struct Calendar(int Day, SimConfig Config)
{
    public int Year => Day / Config.DaysPerYear;
    public int DayOfYear => Day % Config.DaysPerYear;
    public Season Season => (Season)(DayOfYear / Config.DaysPerSeason);
    public int DayOfSeason => DayOfYear % Config.DaysPerSeason;

    public float AgeYears(int birthDay) => (Day - birthDay) / (float)Config.DaysPerYear;

    public override string ToString() => $"{Year + 1}년 {Season} {DayOfSeason + 1}일";
}
