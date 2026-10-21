using System.Globalization;
using System.Text;

namespace People.Sim;

/// <summary>해마다 모으는 통계. 한 줄씩 CSV 로 쌓는다.</summary>
public sealed class YearStats
{
    public int Year, Births, Deaths, HungerDeaths, InfantDeaths, MaternalDeaths, Stillbirths, MultipleBirths,
        Marriages, Polygynous, Splits, Dissolved, DiseaseABirths, RecessiveBirths, FloodedPlots, Migrations, NewVillages, RoadTiles, Lanes, Swaps, Evicted, HousesBuilt, HouseSales, HousesCollapsed, RegrownForest, RoadsLost, Apprentices, NewJobs, InheritedJobs, ApprenticedJobs, LeasedPlots, RentMarkets, WageMarkets, Loans, LandSales, Foreclosed, LandlessSettlers, Raids, RaidsWon, RaidDeaths, NewClients, NewSoldiers, NewCollectors, Uncollected, InheritedPlots, Residences, Relocated, Demolished, LordshipsInherited, ClientsLost, HiddenFatherBirths, Bastards, Chats, Gossips, Gifts, Affairs, SexTrades, SecretsExposed, NewFanatics, FanaticsBroken, FanaticsDied, FanaticsLost, FanaticCount, DisloyalLeft, Persuades, Threats, Arranged, Divorces, Rumors, Concubines, ConcubineBirths, Acknowledged, HeirsNamed, OrderedSplits, Disowned, Forced, Hushes, ContraSold, Abortions, AbortionDeaths, NewSexWorkers, LoadBirths, CloseKinBirths, CloseKinLoadBirths, CousinBirths, CousinLoadBirths;
    public float MarriageKinSum, Harvest, Weather, InbreedingSum, RentRateSum, WageSum, HiredUnits, Skimmed, Tribute, SexTradeValue, ClientLoyalty;
    public long FanaticYearsSum;   // 광신이 되기까지 섬긴 해의 누적(평균은 ÷ 광신 수)
    public int SexTradesBig, SexTradesTotal;   // 하룻밤 거래 누적(30집 넘는 마을, 전체). 마을 크기 비교용
    public int Tier1, Tier2, Tier3, Tier4, Vassals, MaxDepth, TierRises, TierFalls, NewVassals, LoopsBroken;
    public float Upshare, CharityFood;
    public int Crimes, Thefts, Assaults, Extortions, Murders, Embezzles, RaidCrimes, Reported, Caught, Escaped, Fines, Floggings, Executions, Covered, Reopened;
    public float CrimeOpinion;
    public int OrgLeaks, SecretRecruits, BornMembers, SecretOrgs, PublicOrgs;
    public float EstablishedMean;
    public int Companies, SellswordBands, Brothels;
    public int Taught, SuccessionSmooth, SuccessionForced, CellsLost, Schooled, IdleScholars;
    public int ProtectionTaken, Converts, TradeRobbed, MercContracts, Agitations, Kidnaps, Ransoms, KidnapKills, FaceLost;
    public float BasicGathered, ToolsMade, ClothMade, FineMade, BuildMade, GoodsSold, GoodsCarried, BuildShort, ToolCovMean, ClothShortMean;
    public float OrgIncome, TradeProfit, EscortPay, MercPay, BrothelCut;
    public int Plots, PlotsExposed, Purges, CoupStrikes, CoupWins, CivilWars, CoupFails, Declared, IndepSuppressed, IndepNegotiated, IndepLetGo, Independences;
    public int OrgOffers, OrgOffersTaken, OrgPledges, OrgBreaks, OrgSuppressions, OrgSuppressWins, AttachedOrgs, FreePublicOrgs;
    public int GangTries, GangNoTarget, GangWins; public float GangLoot;
    public int LandBought, Volunteered, InnerAsked, FiefsAsked, Wars, WarWins;
    public int IdentityShifts, DisciplineLeft, VacantUnits, SpiesFound, Trained;
    public int Enrolled, Graduated, GradPlaced, GradFled, Students, Studied;
    public int BelongLeft, BelongSwitched, Retaliations;
    public float FoodSold, FoodBought, FoodPriceMean, CoinMean, Minted;
    public int TitheRefused, ZealotsBroken, NewZealots, Zealots, Believers, TitheChanges, Offerings; public float TithePaid, MeanFaith, OfferingTotal;
    public int BudgetShort, Factions, FactionDemands, DemandsMet, FactionSlanders, InnerCoups, InnerCoupWins, FactionSplits, Placated, CmdRotated, Expelled; public float Stipends, MeanGrievance, MeanOrgLoyalty, MeanFame, MeanStanding, OrgFarmLabor;
    public int TaxRefused, TaxDebts, TaxSeized, TaxExpelled, TaxVoluntary;
    public int ThreatSeen, ThreatWarned, ThreatConditions, ThreatComplied, ThreatDefied, ThreatCoopt, ThreatSuppress, DoubleAgents;
    public int RationLeft, FullTimers, FacilitiesBuilt, FacilitiesDone, FacilitiesSeized, FacilitiesLost, Facilities; public float OrgProduced, Supplied, OrgGoodsSold; public float ManageLost, MeanGrip;
    public int RiseNoticed, RiseActs, Coopted, Warned, Swayed, OathsTaken, Slanders, RiseSuppressWins, Tyrants, Invested, SworeUp, OverHead, Rivals;
    public int UnitSplits, UnitMerges, LordOrgs, Enfeoffs, Revokes, RevokeRefused, OrgUnits, LordArmies;
    public int GroupsFounded, GroupsDissolved, BanditGroups, BanditRaids, Sanctioned, Suppressions, SuppressWins, Watches, Bandits, Brotherhoods, GroupMembers;
    public long PopBigYears, PopYears;         // 큰 마을 사람 수·전체 사람 수를 해마다 더한 값
    private int _womenDied45, _womenChildren45;

    private readonly StringBuilder _csv = new();
    public string Csv => _csv.ToString();

    public static readonly string Header =
        "year,population,households,births,deaths,hunger_deaths,infant_deaths,maternal_deaths,stillbirths,multiple_births," +
        "marriages,polygynous,mean_marriage_kin,splits,dissolved,weather,harvest,food_per_capita,generated_chunks," +
        "height,strength,fertility,looks,charisma,constitution,pref_height," +
        "twinning_freq,diseaseA_freq,diseaseA_births,completed_fertility,mean_birth_inbreeding,recessive_births," +
        "villages,largest_village,near_water_households,flooded_plots,migrations,new_villages,road_tiles,lanes,swaps,evicted_fields,houses_built,house_sales,houses_collapsed,vacant_houses,regrown_forest,abandoned_fields,roads_lost,carpenters,healers,traders,literate,mean_farm_skill,mean_craft_skill,apprentices,new_jobs,inherited_jobs,apprenticed_jobs,leased_plots,rent_rate,hired_units,wage,loans,land_sales,foreclosed,landless_settlers,land_gini,owners,landlords,tenants,laborers,rulers,clients,soldiers,collectors,raids,raids_won,raid_deaths,new_clients,lordships_inherited,clients_lost,tribute,skimmed,uncollected,inherited_plots,hidden_father_births,bastards,chats,gossips,gifts,affairs,sex_trades,sex_trade_value,secrets_exposed,secrets_open,client_loyalty,fanatics,new_fanatics,fanatics_broken,fanatics_died,fanatics_lost,disloyal_left,persuades,threats,arranged,divorces,rumors,concubines,concubine_births,acknowledged,heirs_named,ordered_splits,disowned,forced,hushes,contra_sold,abortions,abortion_deaths,new_sex_workers,sex_workers,tier1,tier2,tier3,tier4,vassals,max_depth,tier_rises,tier_falls,new_vassals,upshare,watches,bandit_groups,brotherhoods,group_members,groups_founded,groups_dissolved,bandit_raids,sanctioned,suppressions,suppress_wins,charity_food,org_units,lord_armies,unit_splits,unit_merges,enfeoffs,revokes,revoke_refused,crimes,thefts,assaults,extortions,murders,embezzles,raid_crimes,reported,caught,escaped,fines,floggings,executions,covered,reopened,crime_opinion,rise_noticed,rise_acts,coopted,warned,swayed,oaths_taken,slanders,rise_suppress_wins,tyrants,invested,swore_up,over_head,rivals,org_leaks,secret_recruits,born_members,secret_orgs,public_orgs,established_mean,companies,sellswords,brothels,protection_taken,converts,trade_robbed,merc_contracts,agitations,kidnaps,ransoms,kidnap_kills,face_lost,org_income,trade_profit,escort_pay,merc_pay,brothel_cut,basic_gathered,tools_made,cloth_made,fine_made,build_made,goods_sold,goods_carried,build_short,tool_cov,cloth_short,crafters,taught,succession_smooth,succession_forced,cells_lost,schooled,idle_scholars,load_births,close_kin_births,close_kin_load_births,cousin_births,cousin_load_births,load_freq,plots,plots_exposed,purges,coup_strikes,coup_wins,civil_wars,coup_fails,declared,indep_suppressed,indep_negotiated,indep_let_go,independences,org_offers,org_offers_taken,org_pledges,org_breaks,org_suppressions,org_suppress_wins,attached_orgs,free_public_orgs,gang_tries,gang_no_target,gang_wins,gang_loot,land_bought,volunteered,inner_asked,fiefs_asked,wars,war_wins,identity_shifts,discipline_left,vacant_units,manage_lost,mean_grip,trained,enrolled,graduated,grad_placed,grad_fled,students,studied,ration_left,full_timers,facilities_built,facilities_done,facilities_seized,facilities_lost,facilities,org_produced,supplied,org_goods_sold,belong_left,belong_switched,retaliations,threat_seen,threat_warned,threat_conditions,threat_complied,threat_defied,threat_coopt,threat_suppress,spies_found,double_agents,food_sold,food_bought,food_price,coin_mean,minted,budget_short,stipends,factions,mean_grievance,faction_demands,demands_met,faction_slanders,inner_coups,inner_coup_wins,faction_splits,placated,cmd_rotated,expelled,believers,zealots,new_zealots,zealots_broken,mean_faith,tithe_paid,tithe_refused,tithe_changes,offerings,offering_total,mean_org_loyalty,mean_fame,mean_standing,org_farm_labor,tax_refused,tax_debts,tax_seized,tax_expelled,tax_voluntary";

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
        Marriages = Polygynous = Splits = Dissolved = DiseaseABirths = RecessiveBirths = FloodedPlots = Migrations = NewVillages = RoadTiles = Lanes = Swaps = Evicted = HousesBuilt = HouseSales = HousesCollapsed = RegrownForest = RoadsLost = Apprentices = NewJobs = InheritedJobs = ApprenticedJobs = LeasedPlots = RentMarkets = WageMarkets = Loans = LandSales = Foreclosed = LandlessSettlers = Raids = RaidsWon = RaidDeaths = NewClients = NewSoldiers = NewCollectors = Uncollected = InheritedPlots = Residences = Relocated = Demolished = LordshipsInherited = ClientsLost = HiddenFatherBirths = Bastards = Chats = Gossips = Gifts = Affairs = SexTrades = SecretsExposed = NewFanatics = FanaticsBroken = FanaticsDied = FanaticsLost = FanaticCount = DisloyalLeft = Persuades = Threats = Arranged = Divorces = Rumors = Concubines = ConcubineBirths = Acknowledged = HeirsNamed = OrderedSplits = Disowned = Forced = Hushes = ContraSold = Abortions = AbortionDeaths = NewSexWorkers = TierRises = TierFalls = NewVassals = GroupsFounded = GroupsDissolved = BanditGroups = BanditRaids = Sanctioned = Suppressions = SuppressWins = UnitSplits = UnitMerges = LordOrgs = Enfeoffs = Revokes = RevokeRefused = Crimes = Thefts = Assaults = Extortions = Murders = Embezzles = RaidCrimes = Reported = Caught = Escaped = Fines = Floggings = Executions = Covered = Reopened = RiseNoticed = RiseActs = Coopted = Warned = Swayed = OathsTaken = Slanders = RiseSuppressWins = Tyrants = Invested = SworeUp = OverHead = OrgLeaks = SecretRecruits = BornMembers = ProtectionTaken = Converts = TradeRobbed = MercContracts = Agitations = Kidnaps = Ransoms = KidnapKills = FaceLost = Taught = SuccessionSmooth = SuccessionForced = CellsLost = Schooled = LoadBirths = CloseKinBirths = CloseKinLoadBirths = CousinBirths = CousinLoadBirths = 0;
        MarriageKinSum = Harvest = InbreedingSum = RentRateSum = WageSum = HiredUnits = Skimmed = Tribute = SexTradeValue = Upshare = CharityFood = CrimeOpinion = OrgIncome = TradeProfit = EscortPay = MercPay = BrothelCut = BasicGathered = ToolsMade = ClothMade = FineMade = BuildMade = GoodsSold = GoodsCarried = BuildShort = 0;
        Plots = PlotsExposed = Purges = CoupStrikes = CoupWins = CivilWars = CoupFails = Declared = IndepSuppressed = IndepNegotiated = IndepLetGo = Independences = 0;
        OrgOffers = OrgOffersTaken = OrgPledges = OrgBreaks = OrgSuppressions = OrgSuppressWins = AttachedOrgs = FreePublicOrgs = GangTries = GangNoTarget = GangWins = 0; GangLoot = 0;
        LandBought = Volunteered = InnerAsked = FiefsAsked = Wars = WarWins = 0;
        IdentityShifts = DisciplineLeft = VacantUnits = SpiesFound = Trained = 0;
        Enrolled = Graduated = GradPlaced = GradFled = Students = Studied = 0;
        BelongLeft = BelongSwitched = Retaliations = 0;
        FoodSold = FoodBought = Minted = 0;
        TitheRefused = ZealotsBroken = NewZealots = Zealots = Believers = TitheChanges = Offerings = 0; TithePaid = MeanFaith = OfferingTotal = 0;
        BudgetShort = Factions = FactionDemands = DemandsMet = FactionSlanders = InnerCoups = InnerCoupWins = FactionSplits = Placated = CmdRotated = Expelled = 0; Stipends = MeanGrievance = MeanOrgLoyalty = MeanFame = MeanStanding = OrgFarmLabor = 0;
        TaxRefused = TaxDebts = TaxSeized = TaxExpelled = TaxVoluntary = 0;
        ThreatSeen = ThreatWarned = ThreatConditions = ThreatComplied = ThreatDefied = ThreatCoopt = ThreatSuppress = DoubleAgents = 0;
        RationLeft = FullTimers = FacilitiesBuilt = FacilitiesDone = FacilitiesSeized = FacilitiesLost = Facilities = 0; OrgProduced = Supplied = OrgGoodsSold = 0; ManageLost = MeanGrip = 0;
        Weather = weather;
        _womenDied45 = _womenChildren45 = 0;
    }

    public void Close(World w)
    {
        var p = w.People;
        for (int h = 0; h < w.Households.HighWater; h++)
        {
            if (!w.Households.IsAlive(h)) continue;
            int v = w.Households.Village[h], m = w.Households.Members[h];
            PopYears += m;
            if (v >= 0 && w.VillageList.Households[v] >= w.Config.TradeVillage) PopBigYears += m;
        }
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
            villages.count, villages.largest, F(w.NearWaterFraction()), FloodedPlots, Migrations, NewVillages, RoadTiles, Lanes, Swaps, Evicted, HousesBuilt, HouseSales, HousesCollapsed, w.Vacant.Count, RegrownForest, w.Map.AbandonedCount, RoadsLost, jobs[1], jobs[2], jobs[3], literate, F(farmN > 0 ? farmSum / farmN : 0), F(craftN > 0 ? craftSum / craftN : 0), Apprentices, NewJobs, InheritedJobs, ApprenticedJobs, LeasedPlots, F(RentMarkets > 0 ? RentRateSum / RentMarkets : 0), F(HiredUnits), F(WageMarkets > 0 ? WageSum / WageMarkets : 0), Loans, LandSales, Foreclosed, LandlessSettlers, F(gini), cls[0], cls[1], cls[2], cls[3], cls[4], clients, jobs[(int)Job.Soldier], jobs[(int)Job.Collector], Raids, RaidsWon, RaidDeaths, NewClients, LordshipsInherited, ClientsLost, F(Tribute), F(Skimmed), Uncollected, InheritedPlots, HiddenFatherBirths, Bastards, Chats, Gossips, Gifts, Affairs, SexTrades, F(SexTradeValue), SecretsExposed, w.Secrets.Count, F(ClientLoyalty), FanaticCount, NewFanatics, FanaticsBroken, FanaticsDied, FanaticsLost, DisloyalLeft, Persuades, Threats, Arranged, Divorces, Rumors, Concubines, ConcubineBirths, Acknowledged, HeirsNamed, OrderedSplits, Disowned, Forced, Hushes, ContraSold, Abortions, AbortionDeaths, NewSexWorkers, jobs[(int)Job.SexWorker], Tier1, Tier2, Tier3, Tier4, Vassals, MaxDepth, TierRises, TierFalls, NewVassals, F(Upshare), Watches, Bandits, Brotherhoods, GroupMembers, GroupsFounded, GroupsDissolved, BanditRaids, Sanctioned, Suppressions, SuppressWins, F(CharityFood), OrgUnits, LordArmies, UnitSplits, UnitMerges, Enfeoffs, Revokes, RevokeRefused, Crimes, Thefts, Assaults, Extortions, Murders, Embezzles, RaidCrimes, Reported, Caught, Escaped, Fines, Floggings, Executions, Covered, Reopened, F(CrimeOpinion), RiseNoticed, RiseActs, Coopted, Warned, Swayed, OathsTaken, Slanders, RiseSuppressWins, Tyrants, Invested, SworeUp, OverHead, Rivals, OrgLeaks, SecretRecruits, BornMembers, SecretOrgs, PublicOrgs, F(EstablishedMean), Companies, SellswordBands, Brothels, ProtectionTaken, Converts, TradeRobbed, MercContracts, Agitations, Kidnaps, Ransoms, KidnapKills, FaceLost, F(OrgIncome), F(TradeProfit), F(EscortPay), F(MercPay), F(BrothelCut), F(BasicGathered), F(ToolsMade), F(ClothMade), F(FineMade), F(BuildMade), F(GoodsSold), F(GoodsCarried), F(BuildShort), F(ToolCovMean), F(ClothShortMean), jobs[(int)Job.Crafter], Taught, SuccessionSmooth, SuccessionForced, CellsLost, Schooled, IdleScholars, LoadBirths, CloseKinBirths, CloseKinLoadBirths, CousinBirths, CousinLoadBirths, F(alleles > 0 ? loadAlleles / (double)(alleles * GenomeLayout.LoadLoci) : 0),
            Plots, PlotsExposed, Purges, CoupStrikes, CoupWins, CivilWars, CoupFails, Declared, IndepSuppressed, IndepNegotiated, IndepLetGo, Independences,
            OrgOffers, OrgOffersTaken, OrgPledges, OrgBreaks, OrgSuppressions, OrgSuppressWins, AttachedOrgs, FreePublicOrgs, GangTries, GangNoTarget, GangWins, F(GangLoot), LandBought, Volunteered, InnerAsked, FiefsAsked, Wars, WarWins, IdentityShifts, DisciplineLeft, VacantUnits, F(ManageLost), F(MeanGrip), Trained, Enrolled, Graduated, GradPlaced, GradFled, Students, Studied,
            RationLeft, FullTimers, FacilitiesBuilt, FacilitiesDone, FacilitiesSeized, FacilitiesLost, Facilities, F(OrgProduced), F(Supplied), F(OrgGoodsSold), BelongLeft, BelongSwitched, Retaliations,
            ThreatSeen, ThreatWarned, ThreatConditions, ThreatComplied, ThreatDefied, ThreatCoopt, ThreatSuppress, SpiesFound, DoubleAgents,
            F(FoodSold), F(FoodBought), F(FoodPriceMean), F(CoinMean), F(Minted),
            BudgetShort, F(Stipends), Factions, F(MeanGrievance), FactionDemands, DemandsMet, FactionSlanders, InnerCoups, InnerCoupWins, FactionSplits, Placated, CmdRotated, Expelled,
            Believers, Zealots, NewZealots, ZealotsBroken, F(MeanFaith), F(TithePaid), TitheRefused, TitheChanges, Offerings, F(OfferingTotal), F(MeanOrgLoyalty), F(MeanFame), F(MeanStanding), F(OrgFarmLabor), TaxRefused, TaxDebts, TaxSeized, TaxExpelled, TaxVoluntary));
    }
}
