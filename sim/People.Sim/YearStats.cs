using System.Globalization;
using System.Text;

namespace People.Sim;

/// <summary>해마다 모으는 통계. 한 줄씩 CSV 로 쌓는다.</summary>
public sealed class YearStats
{
    public int Year, Births, Deaths, HungerDeaths, InfantDeaths, MaternalDeaths, Stillbirths, MultipleBirths,
        Marriages, Polygynous, Splits, Dissolved, DiseaseABirths, RecessiveBirths, FloodedPlots, Migrations, NewVillages, RoadTiles, Lanes, Swaps, Evicted, HousesBuilt, HouseSales, HousesCollapsed, RegrownForest, RoadsLost, Apprentices, NewJobs, InheritedJobs, ApprenticedJobs, LeasedPlots, RentMarkets, WageMarkets, Loans, LandSales, Foreclosed, LandlessSettlers, Raids, RaidsWon, RaidDeaths, NewClients, NewSoldiers, NewCollectors, Uncollected, InheritedPlots, LoadBirths, CloseKinBirths, CloseKinLoadBirths, CousinBirths, CousinLoadBirths;
    public float MarriageKinSum, Harvest, Weather, InbreedingSum, RentRateSum, WageSum, HiredUnits, Skimmed, Tribute;
    private int _womenDied45, _womenChildren45;

    private readonly StringBuilder _csv = new();
    public string Csv => _csv.ToString();

    public static readonly string Header =
        "year,population,households,births,deaths,hunger_deaths,infant_deaths,maternal_deaths,stillbirths,multiple_births," +
        "marriages,polygynous,mean_marriage_kin,splits,dissolved,weather,harvest,food_per_capita,generated_chunks," +
        "height,strength,fertility,looks,charisma,constitution,pref_height," +
        "twinning_freq,diseaseA_freq,diseaseA_births,completed_fertility,mean_birth_inbreeding,recessive_births," +
        "villages,largest_village,near_water_households,flooded_plots,migrations,new_villages,road_tiles,lanes,swaps,evicted_fields,houses_built,house_sales,houses_collapsed,vacant_houses,regrown_forest,abandoned_fields,roads_lost,carpenters,healers,traders,literate,mean_farm_skill,mean_craft_skill,apprentices,new_jobs,inherited_jobs,apprenticed_jobs,leased_plots,rent_rate,hired_units,wage,loans,land_sales,foreclosed,landless_settlers,land_gini,owners,landlords,tenants,laborers,rulers,clients,soldiers,collectors,raids,raids_won,raid_deaths,new_clients,tribute,skimmed,uncollected,inherited_plots,load_births,close_kin_births,close_kin_load_births,cousin_births,cousin_load_births,load_freq";

    public YearStats() => _csv.AppendLine(Header);

    public void RecordWomanDeath(int age, int children)
    {
        if (age < 45) return;
        _womenDied45++;
        _womenChildren45 += children;
    }

    public void Begin(int day, float weather)
    {
        Births = Deaths = HungerDeaths = InfantDeaths = MaternalDeaths = Stillbirths = MultipleBirths = 0;
        Marriages = Polygynous = Splits = Dissolved = DiseaseABirths = RecessiveBirths = FloodedPlots = Migrations = NewVillages = RoadTiles = Lanes = Swaps = Evicted = HousesBuilt = HouseSales = HousesCollapsed = RegrownForest = RoadsLost = Apprentices = NewJobs = InheritedJobs = ApprenticedJobs = LeasedPlots = RentMarkets = WageMarkets = Loans = LandSales = Foreclosed = LandlessSettlers = Raids = RaidsWon = RaidDeaths = NewClients = NewSoldiers = NewCollectors = Uncollected = InheritedPlots = LoadBirths = CloseKinBirths = CloseKinLoadBirths = CousinBirths = CousinLoadBirths = 0;
        MarriageKinSum = Harvest = InbreedingSum = RentRateSum = WageSum = HiredUnits = Skimmed = Tribute = 0;
        Weather = weather;
        _womenDied45 = _womenChildren45 = 0;
    }

    public void Close(World w)
    {
        var p = w.People;
        int adults = 0, pop = 0;
        double height = 0, strength = 0, fert = 0, looks = 0, cha = 0, con = 0, prefH = 0;
        long twinAlleles = 0, diseaseAlleles = 0, alleles = 0, loadAlleles = 0;
        var jobs = new int[(int)Job.Count];
        int literate = 0, farmN = 0, craftN = 0;
        double farmSum = 0, craftSum = 0;
        int lTwin = GenomeLayout.Locus(Marker.Twinning), lDis = GenomeLayout.Locus(Marker.DiseaseA);
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i)) continue;
            pop++;
            var g = p.Genome(i);
            twinAlleles += g[lTwin * 2] + g[lTwin * 2 + 1];
            diseaseAlleles += g[lDis * 2] + g[lDis * 2 + 1];
            loadAlleles += Genetics.LoadAlleles(g);
            alleles += 2;
            if ((w.Day - p.BirthDay[i]) / w.Config.DaysPerYear < 16) continue;
            adults++;
            var job = (Job)p.JobOf[i];
            jobs[(int)job]++;
            if (p.GetSkill(i, Skill.Letters) >= 0.3f) literate++;
            if (job == Job.Farmer) { farmSum += p.GetSkill(i, Skill.Farming); farmN++; }
            else { craftSum += p.GetSkill(i, Skills.SkillOf(job)); craftN++; }
            height += Genetics.Score(g, Trait.Height);
            strength += Genetics.Score(g, Trait.Strength);
            fert += p.Get(i, Trait.Fertility);
            looks += p.Get(i, Trait.Looks);
            cha += p.Get(i, Trait.Charisma);
            con += Genetics.Score(g, Trait.Constitution);
            prefH += p.Get(i, Trait.PrefHeight);
        }
        double a = Math.Max(1, adults);
        float food = 0;
        for (int h = 0; h < w.Households.HighWater; h++) if (w.Households.IsAlive(h)) food += w.Households.Food[h];
        // 땅 소유 불평등(지니)과 수입원 이름표별 가구 수
        var plots = new List<int>();
        var cls = new int[6];
        int clients = 0;
        for (int h = 0; h < w.Households.HighWater; h++)
        {
            if (!w.Households.IsAlive(h)) continue;
            plots.Add(w.Households.PlotCount[h]);
            cls[(int)w.ClassOf(h)]++;
            if (w.PatronOf(h) >= 0) clients++;
        }
        plots.Sort();
        double gini = 0, cum = 0, tot = plots.Sum();
        if (tot > 0)
        {
            for (int k = 0; k < plots.Count; k++) { cum += plots[k]; gini += cum; }
            gini = 1 - 2 * (gini / (tot * plots.Count)) + 1.0 / plots.Count;
        }
        var villages = w.Villages();
        var c = CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.####", c);
        _csv.AppendLine(string.Join(",",
            w.Calendar.Year + 1, pop, w.Households.AliveCount, Births, Deaths, HungerDeaths, InfantDeaths, MaternalDeaths,
            Stillbirths, MultipleBirths, Marriages, Polygynous, F(Marriages > 0 ? MarriageKinSum / Marriages : 0), Splits, Dissolved,
            F(Weather), F(Harvest), F(pop > 0 ? food / pop : 0), w.Map.GeneratedChunks,
            F(height / a), F(strength / a), F(fert / a), F(looks / a), F(cha / a), F(con / a), F(prefH / a),
            F(alleles > 0 ? twinAlleles / (double)alleles : 0), F(alleles > 0 ? diseaseAlleles / (double)alleles : 0), DiseaseABirths,
            F(_womenDied45 > 0 ? _womenChildren45 / (double)_womenDied45 : double.NaN),
            F(Births > 0 ? InbreedingSum / Births : 0), RecessiveBirths,
            villages.count, villages.largest, F(w.NearWaterFraction()), FloodedPlots, Migrations, NewVillages, RoadTiles, Lanes, Swaps, Evicted, HousesBuilt, HouseSales, HousesCollapsed, w.Vacant.Count, RegrownForest, w.Map.AbandonedCount, RoadsLost, jobs[1], jobs[2], jobs[3], literate, F(farmN > 0 ? farmSum / farmN : 0), F(craftN > 0 ? craftSum / craftN : 0), Apprentices, NewJobs, InheritedJobs, ApprenticedJobs, LeasedPlots, F(RentMarkets > 0 ? RentRateSum / RentMarkets : 0), F(HiredUnits), F(WageMarkets > 0 ? WageSum / WageMarkets : 0), Loans, LandSales, Foreclosed, LandlessSettlers, F(gini), cls[0], cls[1], cls[2], cls[3], cls[4], clients, jobs[(int)Job.Soldier], jobs[(int)Job.Collector], Raids, RaidsWon, RaidDeaths, NewClients, F(Tribute), F(Skimmed), Uncollected, InheritedPlots, LoadBirths, CloseKinBirths, CloseKinLoadBirths, CousinBirths, CousinLoadBirths, F(alleles > 0 ? loadAlleles / (double)(alleles * GenomeLayout.LoadLoci) : 0)));
    }
}
