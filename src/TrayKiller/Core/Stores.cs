using System;
using System.Collections.Generic;
using System.Linq;

namespace TrayKiller.Core
{
    public sealed class ActivityStore
    {
        const int Max = 500;
        readonly object gate = new object();
        readonly List<ActivityEntry> items;

        public ActivityStore()
        {
            items = Json.Load<List<ActivityEntry>>(AppPaths.ActivityFile);
        }

        public void Add(ActivityEntry e)
        {
            lock (gate)
            {
                items.Insert(0, e);
                if (items.Count > Max) items.RemoveRange(Max, items.Count - Max);
            }
        }

        public List<ActivityEntry> Snapshot()
        {
            lock (gate) return items.ToList();
        }

        public void Clear()
        {
            lock (gate) items.Clear();
        }

        public void Save()
        {
            List<ActivityEntry> copy;
            lock (gate) copy = items.ToList();
            Json.Save(AppPaths.ActivityFile, copy);
        }
    }

    public sealed class BlockedStore
    {
        readonly object gate = new object();
        readonly List<BlockedItem> items;

        public BlockedStore()
        {
            items = Json.Load<List<BlockedItem>>(AppPaths.BlockedFile);
        }

        /// <summary>Adds the item; returns false when an item with the same key already exists.</summary>
        public bool Upsert(BlockedItem item)
        {
            lock (gate)
            {
                var existing = items.FindIndex(i => i.Key == item.Key);
                if (existing >= 0)
                {
                    // keep the original backup data (first block wins) but refresh the timestamp
                    items[existing].BlockedAt = item.BlockedAt;
                    return false;
                }
                items.Add(item);
                return true;
            }
        }

        public void Remove(BlockedItem item)
        {
            lock (gate) items.RemoveAll(i => i.Id == item.Id);
        }

        public List<BlockedItem> Snapshot()
        {
            lock (gate) return items.ToList();
        }

        public List<BlockedItem> ForRule(string ruleId)
        {
            lock (gate) return items.Where(i => i.RuleId == ruleId).ToList();
        }

        public int Count
        {
            get { lock (gate) return items.Count; }
        }

        public void Save()
        {
            List<BlockedItem> copy;
            lock (gate) copy = items.ToList();
            Json.Save(AppPaths.BlockedFile, copy);
        }
    }
}
