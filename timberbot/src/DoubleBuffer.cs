// DoubleBuffer.cs -- Lock-free thread safety for game state.
//
// Problem: HTTP GET requests run on a background thread, but Unity game objects
// can only be read on the main thread. We can't lock the main thread (blocks game)
// and we can't read game objects on the background thread (crashes).
//
// Solution: two lists of the same data. The main thread writes to one (Write),
// then swaps it to become the Read list. The background thread only reads from Read.
// No locks, no contention, no copying.
//
// How it works:
//   Main thread:  for each entity, update fields in Write list -> call Swap()
//   HTTP thread:  iterate Read list (safe, never modified during read)
//
// Add/Remove update BOTH lists so they always have the same entity slots.
// The two-arg Add(writeItem, readItem) lets you put different initial values
// in each buffer (e.g. separate Dictionary instances to avoid shared references).

using System;
using System.Collections.Generic;

namespace Timberbot
{
    class DoubleBuffer<T>
    {
        private List<T> _write = new List<T>();  // main thread writes here
        private List<T> _read = new List<T>();   // background thread reads here

        public List<T> Read => _read;     // safe to iterate from any thread
        public List<T> Write => _write;   // only touch from main thread
        public int Count => _write.Count;

        // Add to both buffers (same item or separate instances for reference types)
        public void Add(T item) { _write.Add(item); _read.Add(item); }
        public void Add(T writeItem, T readItem) { _write.Add(writeItem); _read.Add(readItem); }

        // Remove matching items from both buffers
        public void RemoveAll(Predicate<T> match) { _write.RemoveAll(match); _read.RemoveAll(match); }
        public void Clear() { _write.Clear(); _read.Clear(); }

        // Swap: the Write list (just updated) becomes the new Read list.
        // The old Read list (no longer being read) becomes the new Write target.
        // This is a pointer swap -- O(1), no data copying.
        public void Swap() { var tmp = _read; _read = _write; _write = tmp; }
    }
}
