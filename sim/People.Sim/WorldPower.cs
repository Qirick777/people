namespace People.Sim;

/// <summary>
/// 5단계: 약탈과 보호. 굶고 빌릴 데도 없는 거친 집은 남의 곡식을 털러 간다.
/// 털린 마을은 위험해지고, 위험한 마을의 집들은 힘센 집에 공납을 내고 보호를 받는다.
/// 공납이 모이면 그 집은 군인을 고용하고, 혼자 챙길 수 있는 집 수(관리 한계)를 넘으면 징수관을 둔다.
/// 지배자·군인·징수관은 정해 둔 신분이 아니라 이 관계에서 생긴다.
/// </summary>
public sealed partial class World
{
    private int[] _patron = new int[256];         // 보호해 주는 가구(없으면 -1)
    private int[] _patronFounded = new int[256];  // 그 가구가 같은 가구인지 확인용
    private float[] _incTribute = new float[256]; // 올해 받은 공납
    private readonly List<float> _danger = new(); // 마을별 위험(약탈당한 횟수, 해마다 줄어든다)
    private readonly Dictionary<int, int> _employer = new();   // 군인·징수관 → 고용한 지배자 가구

    private void EnsurePowerArrays()
    {
        int n = Households.HighWater;
        if (_patron.Length < n)
        {
            int c = Math.Max(n, _patron.Length * 2);
            int old = _patron.Length;
            Array.Resize(ref _patron, c); Array.Resize(ref _patronFounded, c); Array.Resize(ref _incTribute, c);
            for (int k = old; k < c; k++) _patron[k] = -1;
        }
        while (_danger.Count < VillageList.Count) _danger.Add(0);
    }

    public int PatronOf(int h)
    {
        if (h < 0 || h >= _patron.Length) return -1;
        int l = _patron[h];
        return l >= 0 && Households.IsAlive(l) && Households.FoundedDay[l] == _patronFounded[h] ? l : -1;
    }

    // 지배자별 따르는 집 수(공납 걷을 때와 해 시작에 다시 센다)
    private readonly Dictionary<int, int> _clientsOf = new();
    public int ClientCount(int lord) => _clientsOf.GetValueOrDefault(lord);

    private void RecountClients()
    {
        _clientsOf.Clear();
        for (int h = 0; h < Households.HighWater; h++)
        {
            int l = Households.IsAlive(h) ? PatronOf(h) : -1;
            if (l >= 0) _clientsOf[l] = _clientsOf.GetValueOrDefault(l) + 1;
        }
    }

    public int RetainerCount(int lord, Job j)
    {
        int n = 0;
        foreach (var (person, l) in _employer) if (l == lord && People.IsAliveIndex(person) && People.JobOf[person] == (byte)j) n++;
        return n;
    }

    public float DangerAt(int v) => v >= 0 && v < _danger.Count ? _danger[v] : 0f;

    /// <summary>한 사람의 싸움 힘. 16~55세 남자만. 군인은 무장해서 1.5배.</summary>
    private float FightPower(int i)
    {
        var p = People;
        int age = AgeYears(i);
        if (!p.Male[i] || age < 16 || age > 55) return 0f;
        float f = 0.4f * MathF.Min(1f, p.Get(i, Trait.Strength)) + 0.3f * p.GetSkill(i, Skill.Arms)
                + 0.2f * p.Get(i, Trait.Aggression) + 0.1f * (1f - p.Get(i, Trait.Caution));
        return p.JobOf[i] == (byte)Job.Soldier ? f * 1.5f : f;
    }

    private float HouseholdPower(int h)
    {
        float s = 0;
        var p = People;
        for (int k = Households.FirstMember[h]; k >= 0; k = p.HhNext[k]) s += FightPower(k);
        return s;
    }

    /// <summary>지배자가 거느린 힘: 집안 남자 + 고용한 군인.</summary>
    private float LordPower(int lord)
    {
        float s = HouseholdPower(lord);
        foreach (var (person, l) in _employer)
            if (l == lord && People.IsAliveIndex(person) && People.JobOf[person] == (byte)Job.Soldier) s += FightPower(person) * (1f + 0.3f * WeaponCov(lord));   // 무장
        return s;
    }

    /// <summary>
    /// 약탈: 굶을 처지인데 빌리지도 팔지도 못한 집 중 거친 가장이 있는 집이 이웃 마을의 여유 있는 집을 턴다.
    /// 지키는 쪽은 그 집 남자들과, 보호받는 집이면 지배자의 군인이 함께 막는다. 진 쪽에서 사람이 죽을 수 있다.
    /// </summary>
    private void TryRaid(int h, float deficit, Group? gang = null)
    {
        var hh = Households;
        var p = People;
        if (gang == null)
        {
            float aggr = HeadTrait(h, Trait.Aggression);
            if (aggr < Config.RaidAggression) return;
            if (!_rng.Chance((aggr - Config.RaidAggression) / (1f - Config.RaidAggression) * 0.8f)) return;
        }
        float power = HouseholdPower(h) + (gang != null ? 0.8f * gang.OwnStrength : 0f);   // 떼는 함께 턴다(이 갈래의 힘)
        if (power < 0.3f) return;
        int days = DaysToHarvest();
        int best = -1;
        float bestScore = 0f;
        int hx = hh.X[h], hy = hh.Y[h];
        // 근처(약 1 km) 집 중 털 만한 것: 여유 식량이 많고 지킴이 약한 곳
        for (int k = 0; k < 24; k++)
        {
            int t = _rng.NextInt(hh.HighWater);
            if (!hh.IsAlive(t) || t == h || hh.Village[t] < 0) continue;
            float dx = hh.X[t] - hx, dy = hh.Y[t] - hy;
            if (dx * dx + dy * dy > Config.RaidRange * Config.RaidRange) continue;
            float spare = hh.Food[t] - hh.Need[t] * days;
            if (spare <= 10f) continue;
            float guard = DefensePower(t);
            float sc = spare / (1f + guard);
            if (sc > bestScore) { bestScore = sc; best = t; }
        }
        if (best < 0) return;
        float defense = DefensePower(best);
        float atk = power * (0.7f + 0.6f * _rng.NextFloat());
        float def = defense * (0.7f + 0.6f * _rng.NextFloat()) + 0.2f;
        int tv = hh.Village[best];
        EnsurePowerArrays();
        _danger[tv] += 1f;
        Stats.Raids++;
        if (atk > def)
        {
            float spare = MathF.Max(0f, hh.Food[best] - hh.Need[best] * days);
            float take = MathF.Min(deficit * 1.5f, spare * 0.6f);
            hh.Food[best] -= take;
            hh.Food[h] += take;
            Stats.RaidsWon++;
            if (gang != null) { Stats.BanditRaids++; RootOf(gang).Leak += 0.1f; if (_rng.Chance(0.5f)) gang.Exposed = true; }   // 털린 집이 누구 짓인지 알아본다
            HeadsRelate(best, h, -40f, 20f, 0f, MemoryType.Raided, -60);
            LootGoods(best, h);
            if (p.IsValid(hh.Head[h]) && p.IsValid(hh.Head[best])) CommitCrime(CrimeType.Raid, hh.Head[h].Index, hh.Head[best].Index, take, false);
            Casualty(best, defense);
            // 지켜 주지 못한 지배자를 원망하고, 반쯤은 떠난다(충성할수록 덜)
            int lordOf = PatronOf(best);
            if (lordOf >= 0)
            {
                _raidedHouses.Add(best);
                HeadsRelate(best, lordOf, -25f, 0f, 0f, MemoryType.Betrayed, -30);
                if (_rng.Chance(0.5f * LeaveFactor(best))) _patron[best] = -1;
            }
        }
        else
        {
            Casualty(h, power);
        }
    }

    private float DefensePower(int h)
    {
        float d = HouseholdPower(h);
        int lord = PatronOf(h);
        if (lord >= 0) d += Config.ProtectShare * LordPower(lord);
        if (Households.Village[h] >= 0) d += WatchPower(Households.Village[h]);   // 마을 자경단
        return d;
    }

    /// <summary>진 쪽 남자 하나가 다치거나 죽는다.</summary>
    private void Casualty(int h, float power)
    {
        if (!_rng.Chance(Config.RaidDeath)) return;
        var p = People;
        int victim = -1;
        float best = 0;
        for (int k = Households.FirstMember[h]; k >= 0; k = p.HhNext[k])
        {
            float f = FightPower(k) * (0.5f + _rng.NextFloat());
            if (f > best) { best = f; victim = k; }
        }
        if (victim < 0) return;
        Stats.RaidDeaths++;
        Die(victim, false);
    }

    /// <summary>
    /// 해마다: 위험한 마을의 집들은 보호해 줄 힘센 집을 찾고, 지배자는 공납으로 군인과 징수관을 쓴다.
    /// 위험이 사라지면 공납을 그만두는 집이 생긴다.
    /// </summary>
    private void ProtectionYear()
    {
        EnsurePowerArrays();
        var hh = Households;
        var p = People;
        var vl = VillageList;
        for (int v = 0; v < _danger.Count; v++) _danger[v] *= Config.DangerKeep;

        // 0) 죽거나 떠난 고용 관계 정리
        foreach (var person in _employer.Keys.ToList())
        {
            int l = _employer[person];
            if (!p.IsAliveIndex(person) || !hh.IsAlive(l) || (p.JobOf[person] != (byte)Job.Soldier && p.JobOf[person] != (byte)Job.Collector) || AgeYears(person) > 55)
            {
                if (p.IsAliveIndex(person) && (p.JobOf[person] == (byte)Job.Soldier || p.JobOf[person] == (byte)Job.Collector)) p.JobOf[person] = 0;
                _employer.Remove(person);
            }
        }

        RecountClients();
        RecountTree();

        // 1) 지배자 후보: 마을마다 힘센 집 몇
        var power = new Dictionary<int, float>();
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || hh.Village[h] < 0) continue;
            if (_patron[h] >= 0 && PatronOf(h) < 0) _patron[h] = -1;
            float pw = LordPower(h);
            // 새로 지배자가 되려면 자리 잡은 땅일수록 더 세야 한다(이미 거느린 집은 그대로)
            float need = ClientCount(h) > 0 ? Config.LordMinPower : Config.LordMinPower * (1f + 2f * Established(hh.Village[h]));
            if (pw >= need && HeadTrait(h, Trait.Ambition) >= 0.4f) power[h] = pw;
        }
        var lords = power.Keys.ToList();

        // 2) 보호받을 집 고르기
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h) || hh.Village[h] < 0) continue;
            float danger = _danger[hh.Village[h]];
            int cur = PatronOf(h);
            if (cur >= 0)
            {
                // 위험이 사라지면 공납을 그만둔다(고집 센 가장일수록 빨리)
                if (danger < 0.2f && _rng.Chance((0.1f + 0.3f * HeadTrait(h, Trait.Stubbornness)) * LeaveFactor(h))) _patron[h] = -1;
                else if (Disloyal(h)) { _patron[h] = -1; Stats.DisloyalLeft++; }
                continue;
            }
            if (power.ContainsKey(h) || danger < Config.DangerThreshold) continue;
            if (!_rng.Chance(MathF.Min(0.9f, 0.3f * danger))) continue;
            int best = -1;
            float bs = 0f;
            foreach (int l in lords)
            {
                if (l == h || (BelowOf(h) > 0 && WouldLoop(h, l)) || Restrained(l)) continue;   // 경고를 받아들인 집은 새 집을 받지 않는다
                float dx = hh.X[l] - hh.X[h], dy = hh.Y[l] - hh.Y[h];
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > Config.ProtectRange) continue;
                float sc = power[l] * (0.5f + HeadTrait(l, Trait.Charisma)) / (1f + d / 60f);
                if (sc > bs) { bs = sc; best = l; }
            }
            if (best < 0) continue;
            _patron[h] = best;
            _patronFounded[h] = hh.FoundedDay[best];
            HeadsRelate(h, best, 0f, 10f, 10f, MemoryType.Protected, 10);
            Stats.NewClients++;
        }

        // 2.5) 작은 지배자가 큰 지배자를 섬긴다(섬김 나무가 자란다)
        RecountClients();
        SeekLieges(power, lords);

        // 3) 지배자가 군인과 징수관을 쓴다. 공납 기대치(지난해 받은 것)만큼.
        RecountClients();
        RecountTree();
        foreach (var (lord, n) in _clientsOf)
        {
            float tribute = _incTribute[lord];
            int soldiersWanted = Math.Min(n / 2, (int)(tribute * 0.5f / Config.SoldierWage));
            int collectorsWanted = Math.Max(0, (int)MathF.Ceiling((n - DirectSpan(lord)) / (float)Config.CollectorSpanBase));
            Hire(lord, Job.Soldier, soldiersWanted - RetainerCount(lord, Job.Soldier));
            Hire(lord, Job.Collector, collectorsWanted - RetainerCount(lord, Job.Collector));
        }
        BuildResidences();
    }

    // ------------------------------------------------------------------ 지배자의 집

    // 집 등급: 0 보통 집, 1 영주 저택, 2 망루 달린 저택, 3 성. 넓은 집은 둘레 칸을 집터로 차지한다.
    private byte[] _residence = new byte[256];
    private readonly Dictionary<int, List<int>> _footprint = new();

    public int ResidenceOf(int h) => h >= 0 && h < _residence.Length ? _residence[h] : 0;

    private static int ResidenceRadius(int level) => level switch { >= 3 => 2, >= 1 => 1, _ => 0 };

    /// <summary>
    /// 거느린 집이 늘고 식량이 넉넉하면 지배자는 집을 고쳐 짓는다. 값은 목수에게 가고, 둘레 칸(밭이면 거둬서)을 집터로 쓴다.
    /// </summary>
    private void BuildResidences()
    {
        var hh = Households;
        if (_residence.Length < hh.HighWater) Array.Resize(ref _residence, Math.Max(hh.HighWater, _residence.Length * 2));
        foreach (var (lord, _) in _clientsOf)
        {
            if (!hh.IsAlive(lord)) continue;
            int n = BelowOf(lord);   // 아래 집 전부(봉신의 집까지)
            int want = n >= Config.CastleClients ? 3 : n >= Config.TowerClients ? 2 : n >= Config.ManorBelow ? 1 : 0;
            int cur = _residence[lord];
            if (want <= cur) continue;
            int next = cur + 1;
            float cost = next switch { 1 => Config.ManorCost, 2 => Config.TowerCost, _ => Config.CastleCost };
            if (hh.Food[lord] < cost * 1.5f) continue;
            PayCarpenter(lord, cost);
            _residence[lord] = (byte)next;
            hh.Capacity[lord] = Math.Max(hh.Capacity[lord], next switch { 1 => 12, 2 => 16, _ => 24 });
            ClaimFootprint(lord, ResidenceRadius(next));
            Stats.Residences++;
        }
    }

    /// <summary>
    /// 큰 집 터를 잡는다. 빈 칸과 밭은 바로 집터로 쓴다. 터 안의 민가는 마을 길가 다른 자리로 옮기고
    /// 지배자가 새 집 짓는 값을 물어 준다. 터 안의 빈집은 헌다. 다른 큰 집의 터는 건드리지 않는다.
    /// </summary>
    private void ClaimFootprint(int h, int r)
    {
        if (!Layout || r <= 0) return;
        var hh = Households;
        var list = _footprint.TryGetValue(h, out var l) ? l : _footprint[h] = new();
        int cx = hh.X[h], cy = hh.Y[h];
        bool Inside(int x, int y) => Math.Abs(x - cx) <= r && Math.Abs(y - cy) <= r;
        // 1) 빈 칸과 밭부터 집터로 막는다(옮겨 갈 자리로 다시 뽑히지 않게)
        var houses = new List<(int x, int y)>();
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if ((dx == 0 && dy == 0) || !Map.InBounds(x, y)) continue;
                if (Map.BuildableOrField(x, y))
                {
                    if (Map.Evict(x, y, hh) >= 0) Stats.Evicted++;
                    Map.MarkHouse(x, y, true);
                    list.Add(Map.Id(x, y));
                }
                else if ((Map.Flags(Map.Id(x, y)) & WorldMap.FlagHouse) != 0) houses.Add((x, y));
            }
        if (houses.Count == 0) return;
        // 2) 터 안의 집: 누구 집인지 찾는다
        var owners = new Dictionary<(int, int), int>();
        for (int k = 0; k < hh.HighWater; k++)
            if (hh.IsAlive(k) && k != h && Inside(hh.X[k], hh.Y[k])) owners[(hh.X[k], hh.Y[k])] = k;
        foreach (var (x, y) in houses)
        {
            if (owners.TryGetValue((x, y), out int k))
            {
                // 다른 지배자의 집이나 큰 집은 그대로 둔다
                if (ClientCount(k) > 0 || ResidenceOf(k) > 0) continue;
                var (nx, ny) = HouseSpotOnRoad(hh.Village[k]);
                if (nx < 0 || Inside(nx, ny)) continue;
                hh.X[k] = nx; hh.Y[k] = ny;
                Map.MarkHouse(nx, ny, true);
                float pay = MathF.Min(Config.HouseBuildCost, hh.Food[h]);
                hh.Food[h] -= pay;
                hh.Food[k] += pay;
                list.Add(Map.Id(x, y));     // 옛 집 자리는 성 터가 된다
                Stats.Relocated++;
                continue;
            }
            // 빈집이면 헌다(큰 빈집은 그대로)
            int vi = _vacant.FindIndex(v => v.X == x && v.Y == y);
            if (vi >= 0 && _vacant[vi].Residence == 0)
            {
                _vacant[vi] = _vacant[^1];
                _vacant.RemoveAt(_vacant.Count - 1);
                list.Add(Map.Id(x, y));
                Stats.Demolished++;
            }
        }
    }

    /// <summary>가구가 없어질 때: 큰 집은 빈집으로 남고(무너질 때 터도 함께 비운다), 등급 기록을 비운다.</summary>
    private (int level, List<int>? footprint) TakeResidence(int h)
    {
        int level = ResidenceOf(h);
        if (h < _residence.Length) _residence[h] = 0;
        _footprint.Remove(h, out var fp);
        return (level, fp);
    }

    // ------------------------------------------------------------------ 지배권 상속

    private readonly Dictionary<int, int> _deadHead = new();   // 가장이 죽은 가구 → 죽은 가장 uid(상속자 찾기용)

    /// <summary>끊긴 지배자 집의 상속자: 죽은 가장과 혈연이 가장 가까운 집. 같은 성이면 더 앞선다.</summary>
    private int FindLordHeir(int h)
    {
        if (!_deadHead.TryGetValue(h, out int dead) || dead < 0) return -1;
        var hh = Households;
        var p = People;
        int fam = Lineage.FamilyOf(dead);
        int best = -1;
        float bs = 0f;
        for (int k = 0; k < hh.HighWater; k++)
        {
            if (k == h || !hh.IsAlive(k) || !p.IsValid(hh.Head[k])) continue;
            int u = p.Uid[hh.Head[k].Index];
            bool same = Lineage.FamilyOf(u) == fam;
            float r = Lineage.Relatedness(u, dead);
            if ((!same && r < 0.06f) || Lineage.Disowned(u)) continue;
            float sc = r + (same ? 0.3f : 0f) + (p.Male[hh.Head[k].Index] ? 0.05f : 0f) - (Lineage.RightOf(u) != Birthright.Legit ? 0.15f : 0f);
            if (sc > bs) { bs = sc; best = k; }
        }
        return best;
    }

    /// <summary>
    /// 지배권을 넘긴다: 따르는 집, 군인·징수관, 땅, 식량, 집(성). 상속자가 큰 집이 없으면 성으로 옮겨 들어가고
    /// 살던 집은 빈집이 된다. 성으로 옮기면 true(끊긴 가구의 집을 빈집으로 두지 않는다).
    /// </summary>
    private bool InheritLordship(int from, int to)
    {
        EnsurePowerArrays();
        var hh = Households;
        int moved = 0;
        for (int k = 0; k < hh.HighWater; k++)
        {
            if (!hh.IsAlive(k) || PatronOf(k) != from) continue;
            if (k == to) { _patron[k] = -1; continue; }
            if (WouldLoop(k, to)) { _patron[k] = -1; continue; }   // 상속자가 그 집 아래에 있었다
            _patron[k] = to;
            _patronFounded[k] = hh.FoundedDay[to];
            moved++;
        }
        if (to < _patron.Length && PatronOf(to) == from) _patron[to] = -1;
        foreach (var person in _employer.Keys.ToList()) if (_employer[person] == from) _employer[person] = to;
        Map.Transfer(from, to, hh.PlotCount[from], hh);
        hh.Food[to] += hh.Food[from];
        hh.Food[from] = 0;
        _incTribute[to] += _incTribute[from];
        bool intoCastle = false;
        int level = ResidenceOf(from);
        if (level > 0 && ResidenceOf(to) == 0)
        {
            // 상속자 집은 빈집으로 두고 성(저택)으로 옮긴다
            if (Layout)
                _vacant.Add(new VacantHouse { X = hh.X[to], Y = hh.Y[to], Capacity = hh.Capacity[to], Village = hh.Village[to], Condition = 1f, Heir = -1, HeirFounded = -1 });
            if (hh.Village[to] != hh.Village[from]) { VillageList.Leave(hh.Village[to]); VillageList.Join(hh.Village[from]); hh.Village[to] = hh.Village[from]; }
            hh.X[to] = hh.X[from]; hh.Y[to] = hh.Y[from];
            hh.Capacity[to] = Math.Max(hh.Capacity[to], hh.Capacity[from]);
            var (lv, fp) = TakeResidence(from);
            if (_residence.Length <= to) Array.Resize(ref _residence, Math.Max(to + 1, _residence.Length * 2));
            _residence[to] = (byte)lv;
            if (fp != null) _footprint[to] = fp;
            intoCastle = true;
        }
        Stats.LordshipsInherited++;
        EnsureRealmArrays();
        _legit[to] = (byte)Legitimacy.Inherited;
        _tier[to] = Math.Max(_tier[to], _tier[from]);
        RecountClients();
        if (moved > 0) ShakeLordship(to, Config.HeirLoss);
        return intoCastle;
    }

    /// <summary>
    /// 지배자가 바뀌면 따르는 집 일부가 떠난다. 새 가장의 말솜씨가 좋을수록, 어른일수록 덜 떠난다.
    /// </summary>
    private void ShakeLordship(int lord, float baseLoss)
    {
        var hh = Households;
        var head = hh.Head[lord];
        if (!People.IsValid(head)) return;
        int i = head.Index;
        float loss = baseLoss * (1f - 0.7f * People.Get(i, Trait.Charisma)) * (AgeYears(i) < 16 ? 1.8f : 1f);
        int left = 0;
        for (int k = 0; k < hh.HighWater; k++)
        {
            if (!hh.IsAlive(k) || PatronOf(k) != lord) continue;
            if (_rng.Chance(loss * LeaveFactor(k))) { _patron[k] = -1; left++; }
        }
        Stats.ClientsLost += left;
        RecountClients();
    }

    /// <summary>지배자 혼자 챙길 수 있는 집 수. 말솜씨와 글이 넓힌다.</summary>
    private int DirectSpan(int lord)
    {
        var head = Households.Head[lord];
        if (!People.IsValid(head)) return 4;
        int i = head.Index;
        return 4 + (int)(8f * People.Get(i, Trait.Charisma) + 10f * People.GetSkill(i, Skill.Letters));
    }

    /// <summary>징수관 한 사람이 맡는 집 수. 글을 알면 늘어난다.</summary>
    private int CollectorSpan(int person) => Config.CollectorSpanBase + (int)(15f * People.GetSkill(person, Skill.Letters));

    /// <summary>
    /// 고용: 가난한 집(지배자 마을과 따르는 집들)의 사람 중 알맞은 사람을 쓴다.
    /// 군인은 16~35세 남자 중 싸움 재능, 징수관은 어른 중 글과 말솜씨.
    /// </summary>
    private void Hire(int lord, Job job, int count)
    {
        var hh = Households;
        var p = People;
        if (count < 0)
        {
            foreach (var (person, l) in _employer.ToList())
            {
                if (count >= 0) break;
                if (l != lord || p.JobOf[person] != (byte)job) continue;
                p.JobOf[person] = 0;
                _employer.Remove(person);
                count++;
            }
            return;
        }
        int lv = hh.Village[lord];
        for (int c = 0; c < count; c++)
        {
            int best = -1;
            float bs = 0f;
            for (int tries = 0; tries < 200; tries++)
            {
                int i = _rng.NextInt(p.HighWater);
                if (!p.IsAliveIndex(i) || p.JobOf[i] != 0) continue;
                int h = p.Household[i];
                if (!hh.IsAlive(h) || (hh.Village[h] != lv && PatronOf(h) != lord)) continue;
                if (ClientCount(h) > 0) continue;   // 다른 지배자 집안 사람은 쓰지 않는다
                int age = AgeYears(i);
                float sc;
                if (job == Job.Soldier)
                {
                    if (!p.Male[i] || age < 16 || age > 35) continue;
                    sc = Skills.Talent(p, i, Skill.Arms) + 0.3f * p.Get(i, Trait.Aggression) + (ClassOf(h) is LandClass.Tenant or LandClass.Laborer or LandClass.None ? 0.3f : 0f)
                       + 0.4f * p.Get(i, Trait.Ambition) * Established(lv);   // 자리 잡은 땅의 야심가는 세력 안에서 자리를 노린다
                }
                else
                {
                    if (age < 20 || age > 55) continue;
                    sc = p.GetSkill(i, Skill.Letters) + 0.5f * p.Get(i, Trait.Charisma) + 0.2f * p.Get(i, Trait.Intelligence);
                }
                if (sc > bs) { bs = sc; best = i; }
            }
            if (best < 0) return;
            p.JobOf[best] = (byte)job;
            Lineage.SetJob(p.Uid[best], job);
            _employer[best] = lord;
            if (job == Job.Soldier) Stats.NewSoldiers++; else Stats.NewCollectors++;
        }
    }

    /// <summary>
    /// 수확 뒤 공납을 걷는다. 지배자가 직접 챙길 수 있는 집과 징수관이 맡은 집만 걷힌다(관리 한계).
    /// 징수관은 욕심만큼 떼어 먹는다. 걷은 공납으로 군인 품삯과 징수관 몫을 치른다.
    /// </summary>
    private void CollectTribute()
    {
        EnsurePowerArrays();
        var hh = Households;
        var p = People;
        Array.Clear(_incTribute);
        var byLord = new Dictionary<int, List<int>>();
        for (int h = 0; h < hh.HighWater; h++)
        {
            int l = PatronOf(h);
            if (l >= 0 && hh.IsAlive(h)) (byLord.TryGetValue(l, out var list) ? list : byLord[l] = new()).Add(h);
        }
        RecountClients();
        foreach (var (lord, list) in byLord)
        {
            // 가까운 집부터 직접 챙기고, 나머지는 징수관에게
            list.Sort((a, b) => Dist2(a, lord).CompareTo(Dist2(b, lord)));
            var collectors = _employer.Where(e => e.Value == lord && p.IsAliveIndex(e.Key) && p.JobOf[e.Key] == (byte)Job.Collector).Select(e => e.Key).ToList();
            int direct = DirectSpan(lord);
            int idx = 0;
            float got = 0;
            for (; idx < list.Count && idx < direct; idx++) got += TakeTribute(list[idx], lord, 1f);
            foreach (int c in collectors)
            {
                int span = CollectorSpan(c);
                float collected = 0;
                for (int k = 0; k < span && idx < list.Count; k++, idx++) collected += TakeTribute(list[idx], lord, 0f);
                float skim = collected * (Config.CollectorCut + 0.15f * p.Get(c, Trait.Ambition)) * SkimFactor(c);
                int ch = p.Household[c];
                if (hh.IsAlive(ch)) { hh.Food[ch] += skim; _incWage[ch] += skim; }
                hh.Food[lord] += collected - skim;
                got += collected - skim;
                Stats.Skimmed += skim;
                // 몫을 넘게 떼면 빼돌리기다. 지배자 가장의 눈썰미로 알아챈다
                float excess = skim - collected * Config.CollectorCut;
                var lh = hh.Head[lord];
                if (excess > 1f && p.IsValid(lh) && hh.IsAlive(ch) && _rng.Chance(0.1f + 0.4f * p.Get(lh.Index, Trait.Perception)))
                    CommitCrime(CrimeType.Embezzle, c, lh.Index, excess, false);
            }
            Stats.Uncollected += list.Count - idx;
            _incTribute[lord] += got;
            Stats.Tribute += got;
            // 군인 품삯
            foreach (var (person, l) in _employer)
            {
                if (l != lord || !p.IsAliveIndex(person) || p.JobOf[person] != (byte)Job.Soldier) continue;
                int sh = p.Household[person];
                float pay = MathF.Min(Config.SoldierWage, MathF.Max(0f, hh.Food[lord]));
                hh.Food[lord] -= pay;
                if (hh.IsAlive(sh)) { hh.Food[sh] += pay; _incWage[sh] += pay; }
            }
        }
        PassTributeUp();
    }

    private float Dist2(int a, int b)
    {
        float dx = Households.X[a] - Households.X[b], dy = Households.Y[a] - Households.Y[b];
        return dx * dx + dy * dy;
    }

    /// <summary>한 집의 공납. toLord = 1 이면 바로 지배자에게, 0 이면 걷은 양만 돌려준다(징수관이 나눈다).</summary>
    private float TakeTribute(int h, int lord, float toLord)
    {
        var hh = Households;
        float income = MathF.Max(0f, _incOwn[h] + _incTenant[h]);
        float t = MathF.Min(income * Config.TributeRate, MathF.Max(0f, hh.Food[h]));
        hh.Food[h] -= t;
        if (toLord > 0) hh.Food[lord] += t;
        return t;
    }

    /// <summary>지배자 이름표: 따르는 집이 이만큼 넘으면.</summary>
    public bool IsRuler(int h) => TierOf(h) >= 2;

    /// <summary>
    /// 상속: 균분 가문은 분가하는 아들에게 땅을 나눠 준다. 같은 마을에 남으면 밭을 넘기고,
    /// 멀리 떠나면 그 몫의 일부를 식량으로 준다. 장자 상속 가문은 주지 않는다(맏이가 집과 땅을 지킨다).
    /// </summary>
    private void InheritOnSplit(int parent, int child, int sonsLeft)
    {
        var hh = Households;
        var head = hh.Head[parent];
        if (!People.IsValid(head)) return;
        int fam = Lineage.FamilyOf(People.Uid[head.Index]);
        if (Lineage.PolicyOf(fam) != InheritPolicy.Partible) return;
        int share = hh.PlotCount[parent] / Math.Max(2, sonsLeft + 1);
        if (share <= 0) return;
        if (hh.Village[child] == hh.Village[parent])
        {
            var mine = Map.PlotsOf(parent).Where(x => !_tiller.ContainsKey(x)).ToList();
            mine.Sort((a, b) => Dist2Plot(a, child).CompareTo(Dist2Plot(b, child)));
            for (int k = 0; k < share && k < mine.Count; k++) Map.GivePlot(mine[k], child, hh);
            Stats.InheritedPlots += Math.Min(share, mine.Count);
        }
        else
        {
            float food = MathF.Min(hh.Food[parent] * 0.3f, share * 10f);
            hh.Food[parent] -= food;
            hh.Food[child] += food;
        }
    }

    private float Dist2Plot(int plot, int h)
    {
        var (x, y) = Map.Pos(plot);
        float dx = x - Households.X[h], dy = y - Households.Y[h];
        return dx * dx + dy * dy;
    }
}
