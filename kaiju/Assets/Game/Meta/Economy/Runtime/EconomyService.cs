using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core;

namespace Game.Meta.Economy
{
    /// <summary>
    /// Funds ledger, defence contracts, transactions and the Q-206 floor chassis rule (R-104 section 10.1). Whole-USD longs only.
    /// Ledger rules: Funds >= 0 always; Funds == TotalCredited - TotalDebited always (conservation); a shortfall is carried as Arrears
    /// (explicit, non-negative, collected from later income), never as negative funds. Save section economy@v1; Read validates everything first
    /// and throws FormatException, leaving the service untouched.
    /// </summary>
    public sealed class EconomyService : IEconomyService, ISaveParticipant
    {
        const string KeyTreasury = "treasury";
        sealed class Tx { public long Seq; public long Amount; public byte Reason; public long Step; }
        sealed class Contract
        {
            public long Id; public ContractState State; public List<ContractAsset> Assets = new List<ContractAsset>();
            public long Value, Advance, Payout, Loss, Step; public int Slot = -1;
        }
        sealed class State
        {
            public long Funds, Arrears, Credited, Debited, NextContractId = 1, NextTx = 1;
            public SortedDictionary<long, Contract> Contracts = new SortedDictionary<long, Contract>();
            public List<Tx> Txs = new List<Tx>();
            public SortedDictionary<int, long> Floor = new SortedDictionary<int, long>();
        }

        readonly PriceTable _prices;
        readonly IEventBus _bus;
        State _s = new State();

        public EconomyService(PriceTable prices, IEventBus bus = null, long startingFunds = 0)
        {
            _prices = prices ?? throw new ArgumentNullException(nameof(prices));
            _bus = bus;
            if (startingFunds < 0) throw new ArgumentOutOfRangeException(nameof(startingFunds));
            if (startingFunds > 0) { _s.Funds = startingFunds; _s.Credited = startingFunds; Record(startingFunds, TransactionReason.Other, 0); }
        }

        public PriceTable Prices => _prices;
        public long Funds => _s.Funds;
        public long Arrears => _s.Arrears;
        public long TotalCredited => _s.Credited;
        public long TotalDebited => _s.Debited;

        /// <summary>Last transactions (oldest first), signed amounts.</summary>
        public void CopyTransactions(List<KeyValuePair<long, long>> seqAndAmount, List<TransactionReason> reasons = null)
        {
            seqAndAmount.Clear(); reasons?.Clear();
            foreach (var t in _s.Txs) { seqAndAmount.Add(new KeyValuePair<long, long>(t.Seq, t.Amount)); reasons?.Add((TransactionReason)t.Reason); }
        }

        // ---------------------------------------------------------------- ledger

        void Record(long signedAmount, TransactionReason reason, long step)
        {
            _s.Txs.Add(new Tx { Seq = _s.NextTx++, Amount = signedAmount, Reason = (byte)reason, Step = step });
            if (_s.Txs.Count > EconomyContractVersions.TransactionRing) _s.Txs.RemoveAt(0);
        }

        void Add(long amount, TransactionReason reason, long step)
        {
            _s.Funds = checked(_s.Funds + amount); _s.Credited = checked(_s.Credited + amount); Record(amount, reason, step);
        }

        void Sub(long amount, TransactionReason reason, long step)
        {
            if (amount > _s.Funds) throw new InvalidOperationException("ledger: debit " + amount + " exceeds funds " + _s.Funds);
            _s.Funds -= amount; _s.Debited = checked(_s.Debited + amount); Record(-amount, reason, step);
        }

        public void Credit(long amountUsd, TransactionReason reason, long step)
        {
            if (amountUsd < 0) throw new ArgumentOutOfRangeException(nameof(amountUsd));
            if (amountUsd == 0) return;
            Add(amountUsd, reason, step);
            if (reason != TransactionReason.ContractAdvance && reason != TransactionReason.ArrearsCollected && _s.Arrears > 0)
            {
                long take = Math.Min(_s.Arrears, amountUsd);
                _s.Arrears -= take; Sub(take, TransactionReason.ArrearsCollected, step);
            }
        }

        public bool TryPurchase(long priceUsd, TransactionReason reason, long step)
        {
            if (priceUsd < 0) throw new ArgumentOutOfRangeException(nameof(priceUsd));
            if (priceUsd > _s.Funds) return false;
            if (priceUsd > 0) Sub(priceUsd, reason, step);
            return true;
        }

        public long PayUpkeep(long amountUsd, long step)
        {
            if (amountUsd < 0) throw new ArgumentOutOfRangeException(nameof(amountUsd));
            long pay = Math.Min(amountUsd, _s.Funds);
            if (pay > 0) Sub(pay, TransactionReason.Upkeep, step);
            _s.Arrears = checked(_s.Arrears + (amountUsd - pay));
            return pay;
        }

        // ---------------------------------------------------------------- contracts

        public ContractInfo GetContract(long id) => _s.Contracts.TryGetValue(id, out var c) ? View(c) : null;

        public IReadOnlyList<ContractInfo> OfferContracts(long seed, long step, IReadOnlyList<ContractAsset> pool, int count)
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in pool)
                if (string.IsNullOrEmpty(a.AssetId) || a.ReplacementUsd <= 0 || !ids.Add(a.AssetId)) throw new ArgumentException("Bad or duplicate pool asset '" + a.AssetId + "'");
            var offers = new List<List<ContractAsset>>();
            if (pool.Count > 0)
            {
                var rng = new SplitMix((ulong)seed ^ ((ulong)step * 0x9E3779B97F4A7C15UL));
                for (int i = 0; i < count; i++)
                {
                    var idx = new List<int>(pool.Count); for (int k = 0; k < pool.Count; k++) idx.Add(k);
                    int take = 1 + (int)rng.Below(Math.Min(3, pool.Count));
                    var set = new List<ContractAsset>();
                    for (int k = 0; k < take; k++)
                    {
                        int j = k + (int)rng.Below(idx.Count - k);
                        int tmp = idx[k]; idx[k] = idx[j]; idx[j] = tmp;
                        set.Add(pool[idx[k]]);
                    }
                    offers.Add(set);
                }
                bool ok = false;
                foreach (var o in offers) if (_prices.ExpectedPayout(Sum(o)) >= _prices.GuaranteedMinPayoutUsd) { ok = true; break; }
                if (!ok)
                {   // guarantee (R-104 section 7d): replace the last offer by the largest assets until the minimum payout is reachable (or the pool is used up)
                    var order = new List<ContractAsset>(pool);
                    order.Sort((x, y) => y.ReplacementUsd != x.ReplacementUsd ? y.ReplacementUsd.CompareTo(x.ReplacementUsd) : string.CompareOrdinal(x.AssetId, y.AssetId));
                    var set = new List<ContractAsset>();
                    foreach (var a in order) { set.Add(a); if (_prices.ExpectedPayout(Sum(set)) >= _prices.GuaranteedMinPayoutUsd) break; }
                    offers[offers.Count - 1] = set;
                }
            }
            foreach (var c in _s.Contracts.Values) if (c.State == ContractState.Offered) c.State = ContractState.Declined;
            var result = new List<ContractInfo>();
            foreach (var set in offers)
            {
                var c = new Contract { Id = _s.NextContractId++, State = ContractState.Offered, Step = step };
                c.Assets.AddRange(set); c.Value = Sum(set);
                _s.Contracts.Add(c.Id, c); result.Add(View(c));
            }
            return result;
        }

        static long Sum(List<ContractAsset> set) { long v = 0; foreach (var a in set) v = checked(v + a.ReplacementUsd); return v; }

        public ContractInfo Accept(long contractId, PlayerSlot slot)
        {
            if (!slot.IsValid) throw new ArgumentException("invalid slot");
            var c = Find(contractId);
            if (c.State != ContractState.Offered) throw new InvalidOperationException("contract " + contractId + " is " + c.State + ", not Offered");
            c.State = ContractState.Accepted; c.Slot = slot.Index;
            c.Advance = _prices.AdvanceUsd(c.Value);
            if (c.Advance > 0) Add(c.Advance, TransactionReason.ContractAdvance, c.Step);
            return View(c);
        }

        public void Decline(long contractId)
        {
            var c = Find(contractId);
            if (c.State != ContractState.Offered) throw new InvalidOperationException("contract " + contractId + " is " + c.State + ", not Offered");
            c.State = ContractState.Declined;
        }

        public ContractInfo Complete(long contractId, ContractOutcome outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            var c = Find(contractId);
            if (c.State != ContractState.Accepted) throw new InvalidOperationException("contract " + contractId + " is " + c.State + ", not Accepted");
            if (outcome.FinalStates.Length != c.Assets.Count) throw new ArgumentException("outcome lists " + outcome.FinalStates.Length + " states for " + c.Assets.Count + " assets");
            foreach (var s in outcome.FinalStates) if (s < AssetDamageState.None || s > AssetDamageState.Complete) throw new ArgumentException("unknown damage state " + s);
            long loss = _prices.LossUsd(c.Assets.ToArray(), outcome.FinalStates);
            long payout = _prices.Payout(c.Value, loss);
            bool failed = _prices.Failed(c.Value, loss);
            c.Loss = loss; c.Payout = payout; c.State = failed ? ContractState.Failed : ContractState.Completed; c.Step = outcome.Step;
            if (payout > 0) Credit(payout, TransactionReason.ContractPayout, outcome.Step);
            // The advance is repaid from the payout; whatever the funds cannot cover becomes Arrears (no debt, no negative funds).
            if (c.Advance > 0)
            {
                long pay = Math.Min(c.Advance, _s.Funds);
                if (pay > 0) Sub(pay, TransactionReason.AdvanceSettled, outcome.Step);
                _s.Arrears = checked(_s.Arrears + (c.Advance - pay));
            }
            var evt = new ContractCompleted { ContractId = c.Id, Slot = PlayerSlot.FromIndex(c.Slot), Outcome = (byte)(failed ? ContractResult.Failed : ContractResult.Completed), PayoutUsd = payout };
            _bus?.Publish(in evt);
            return View(c);
        }

        Contract Find(long id) => _s.Contracts.TryGetValue(id, out var c) ? c : throw new ArgumentException("unknown contract " + id);

        static ContractInfo View(Contract c) => new ContractInfo
        {
            Id = c.Id, State = c.State, Assets = c.Assets.ToArray(), ValueUsd = c.Value, AdvanceUsd = c.Advance, PayoutUsd = c.Payout, LossUsd = c.Loss,
            AcceptedBySlot = c.Slot, Step = c.Step,
        };

        // ---------------------------------------------------------------- floor chassis (Q-206)

        public FloorGrant TryGrantFloorChassis(PlayerSlot slot, int flyableMechCount, long step)
        {
            if (!slot.IsValid) throw new ArgumentException("invalid slot");
            if (flyableMechCount < 0) throw new ArgumentOutOfRangeException(nameof(flyableMechCount));
            _s.Floor.TryGetValue(slot.Index, out long n);
            long value = _prices.ChassisPriceUsd(_prices.FloorChassis);
            if (flyableMechCount > 0) return new FloorGrant(false, _prices.FloorChassis, value, n);   // never stacked
            _s.Floor[slot.Index] = ++n;
            Record(0, TransactionReason.FloorChassis, step);   // free, regardless of funds
            return new FloorGrant(true, _prices.FloorChassis, value, n);
        }

        public long FloorGrantCount(PlayerSlot slot) => _s.Floor.TryGetValue(slot.Index, out long n) ? n : 0;

        // ---------------------------------------------------------------- save: economy@v1

        public string SectionName => EconomyContractVersions.SectionName;
        public int SectionVersion => EconomyContractVersions.SaveSection;

        static string Pad(long v) => v.ToString("D12", CultureInfo.InvariantCulture);

        public void Write(SaveSection section)
        {
            section.Add(KeyTreasury).Set("funds", SaveValue.Of(_s.Funds)).Set("arrears", SaveValue.Of(_s.Arrears)).Set("credited", SaveValue.Of(_s.Credited))
                .Set("debited", SaveValue.Of(_s.Debited)).Set("next_contract_id", SaveValue.Of(_s.NextContractId)).Set("next_tx", SaveValue.Of(_s.NextTx));
            foreach (var c in _s.Contracts.Values)
            {
                section.Add("contract:" + Pad(c.Id)).Set("state", SaveValue.Of((long)c.State)).Set("value_usd", SaveValue.Of(c.Value)).Set("advance", SaveValue.Of(c.Advance))
                    .Set("payout", SaveValue.Of(c.Payout)).Set("loss", SaveValue.Of(c.Loss)).Set("slot", SaveValue.Of((long)c.Slot)).Set("step", SaveValue.Of(c.Step));
                for (int i = 0; i < c.Assets.Count; i++)
                    section.Add("asset:" + Pad(c.Id) + ":" + i.ToString("D4", CultureInfo.InvariantCulture))
                        .Set("id", SaveValue.Of(c.Assets[i].AssetId)).Set("value_usd", SaveValue.Of(c.Assets[i].ReplacementUsd));
            }
            foreach (var t in _s.Txs)
                section.Add("tx:" + Pad(t.Seq)).Set("amount", SaveValue.Of(t.Amount)).Set("reason", SaveValue.Of((long)t.Reason)).Set("step", SaveValue.Of(t.Step));
            foreach (var kv in _s.Floor)
                section.Add("floor:" + PlayerSlot.FromIndex(kv.Key)).Set("granted", SaveValue.Of(kv.Value));
        }

        public void Read(SaveSection section)
        {
            var n = new State();
            if (!section.TryGet(KeyTreasury, out var tr)) throw new FormatException("economy: no treasury record");
            n.Funds = Int(tr, "funds"); n.Arrears = Int(tr, "arrears"); n.Credited = Int(tr, "credited"); n.Debited = Int(tr, "debited");
            n.NextContractId = Int(tr, "next_contract_id"); n.NextTx = Int(tr, "next_tx");
            if (n.Funds < 0 || n.Arrears < 0 || n.Credited < 0 || n.Debited < 0) throw new FormatException("economy: negative ledger value");
            if (n.Credited - n.Debited != n.Funds) throw new FormatException("economy: funds do not equal credited minus debited");
            if (n.NextContractId < 1 || n.NextTx < 1) throw new FormatException("economy: bad counters");
            long cap = _prices.PayoutCapUsd;
            long lastTx = 0;
            foreach (var rec in section.Records)
            {
                string k = rec.Key;
                if (k == KeyTreasury) continue;
                if (k.StartsWith("contract:", StringComparison.Ordinal))
                {
                    long id = ParseId(k, 9, k.Length);
                    var c = new Contract { Id = id };
                    long st = Int(rec, "state");
                    if (st < 0 || st > (long)ContractState.Declined) throw new FormatException("economy: bad state in " + k);
                    c.State = (ContractState)st; c.Value = Int(rec, "value_usd"); c.Advance = Int(rec, "advance"); c.Payout = Int(rec, "payout");
                    c.Loss = Int(rec, "loss"); c.Step = Int(rec, "step"); long slot = Int(rec, "slot");
                    if (slot < -1 || slot >= int.MaxValue) throw new FormatException("economy: bad slot in " + k);
                    c.Slot = (int)slot;
                    if (id >= n.NextContractId || c.Value <= 0 || c.Advance < 0 || c.Payout < 0 || c.Payout > cap || c.Loss < 0 || c.Loss > c.Value)
                        throw new FormatException("economy: bad numbers in " + k);
                    bool open = c.State == ContractState.Offered || c.State == ContractState.Declined;
                    if (open != (c.Slot == -1) || (open && (c.Advance != 0 || c.Payout != 0 || c.Loss != 0))) throw new FormatException("economy: state/slot mismatch in " + k);
                    n.Contracts.Add(id, c);
                }
                else if (k.StartsWith("asset:", StringComparison.Ordinal)) { /* second pass */ }
                else if (k.StartsWith("tx:", StringComparison.Ordinal))
                {
                    long seq = ParseId(k, 3, k.Length);
                    long reason = Int(rec, "reason");
                    if (seq <= lastTx || seq >= n.NextTx || reason < 1 || reason > (long)TransactionReason.Other) throw new FormatException("economy: bad tx " + k);
                    lastTx = seq;
                    n.Txs.Add(new Tx { Seq = seq, Amount = Int(rec, "amount"), Reason = (byte)reason, Step = Int(rec, "step") });
                }
                else if (k.StartsWith("floor:", StringComparison.Ordinal))
                {
                    if (!PlayerSlot.TryParse(k.Substring(6), out var slot) || !slot.IsValid || slot.ToString() != k.Substring(6)) throw new FormatException("economy: bad floor key " + k);
                    long g = Int(rec, "granted");
                    if (g < 1) throw new FormatException("economy: bad floor count " + k);
                    n.Floor.Add(slot.Index, g);
                }
                else throw new FormatException("economy: unknown record " + k);
            }
            if (n.Txs.Count > EconomyContractVersions.TransactionRing) throw new FormatException("economy: transaction ring too long");
            foreach (var rec in section.Records)
            {
                if (!rec.Key.StartsWith("asset:", StringComparison.Ordinal)) continue;
                int cut = rec.Key.IndexOf(':', 6);
                if (cut < 0) throw new FormatException("economy: bad asset key " + rec.Key);
                long id = ParseId(rec.Key, 6, cut);
                long idx = ParseId(rec.Key, cut + 1, rec.Key.Length);
                if (!n.Contracts.TryGetValue(id, out var c)) throw new FormatException("economy: asset without contract " + rec.Key);
                if (idx != c.Assets.Count) throw new FormatException("economy: asset indices not dense in " + rec.Key);
                if (!rec.TryGet("id", out var idv) || idv.Kind != SaveValueKind.String || idv.AsString.Length == 0) throw new FormatException("economy: asset id missing in " + rec.Key);
                long val = Int(rec, "value_usd");
                if (val <= 0) throw new FormatException("economy: asset value in " + rec.Key);
                c.Assets.Add(new ContractAsset(idv.AsString, val));
            }
            foreach (var c in n.Contracts.Values)
                if (c.Assets.Count == 0 || Sum(c.Assets) != c.Value) throw new FormatException("economy: assets of contract " + c.Id + " do not sum to its value");
            _s = n;
        }

        static long ParseId(string key, int from, int to)
        {
            if (to - from < 1 || to - from > 18) throw new FormatException("economy: bad key " + key);
            long v = 0;
            for (int i = from; i < to; i++) { char ch = key[i]; if (ch < '0' || ch > '9') throw new FormatException("economy: bad key " + key); v = v * 10 + (ch - '0'); }
            return v;
        }

        static long Int(SaveRecord r, string f)
        {
            if (!r.TryGet(f, out var v) || v.Kind != SaveValueKind.Int64) throw new FormatException("economy: " + r.Key + " lacks Int64 '" + f + "'");
            return v.AsInt64;
        }

        sealed class SplitMix
        {
            ulong _s; public SplitMix(ulong seed) { _s = seed; }
            ulong Next() { unchecked { _s += 0x9E3779B97F4A7C15UL; ulong z = _s; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); } }
            public ulong Below(int n) => Next() % (ulong)n;
        }
    }

    public static class EconomyModule
    {
        /// <summary>Returns the service it registered, or null when meta.economy is off (nothing registered). The composition root registers it with SaveSystem.</summary>
        public static EconomyService Install(IServiceRegistry registry, IFeatureFlags flags, PriceTable prices, IEventBus bus = null, long startingFunds = 0)
        {
            if (registry == null || flags == null || prices == null || !flags.IsOn(EconomyFlags.Economy)) return null;
            var service = new EconomyService(prices, bus, startingFunds);
            registry.Register<IEconomyService>(service);
            return service;
        }
    }
}
