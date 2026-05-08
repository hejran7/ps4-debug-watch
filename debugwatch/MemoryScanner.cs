// Decompiled with JetBrains decompiler
// Type: debugwatch.MemoryScanner
// Assembly: dbgw, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C42DB83B-FBD0-4471-97D2-F43102A97A5F
// Assembly location: C:\Users\x\Downloads\PS4\debugwatch\dbgw\dbgw.exe

// -------------------------------------------------------------------------
// Fixes applied (SC fork):
//   - CompareLessThan: was returning SequenceEqual (equality), now does correct
//     type-aware less-than comparison using BitConverter
//   - CompareGreaterThan: same — was returning SequenceEqual, now correct
//   - ScanMemory: lastReadableMemoryAddress was data.Length-(typeLength+1),
//     off by one — last valid address was never scanned. Fixed to data.Length-typeLength
//   - ScanMemory default path: removed per-byte ScanProgressBar.Increment call
//     that caused the bar to overflow (max is region count, not byte count)
//   - Progress bar is now incremented in the outer loop in DebugWatchForm,
//     once per region, so it accurately reflects scan progress
// -------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace debugwatch
{
    internal class MemoryScanner
    {
        private const int ThreadCount = 8;

        public static MemoryScanner.SCAN_TYPE StringToType(string str)
        {
            MemoryScanner.SCAN_TYPE scanType = MemoryScanner.SCAN_TYPE.BYTE;
            string upper = str.ToUpper();
            if (!(upper == "BYTE"))
            {
                if (!(upper == "SHORT"))
                {
                    if (!(upper == "INTEGER"))
                    {
                        if (!(upper == "LONG"))
                        {
                            if (!(upper == "FLOAT"))
                            {
                                if (upper == "DOUBLE")
                                    scanType = MemoryScanner.SCAN_TYPE.DOUBLE;
                            }
                            else
                                scanType = MemoryScanner.SCAN_TYPE.FLOAT;
                        }
                        else
                            scanType = MemoryScanner.SCAN_TYPE.LONG;
                    }
                    else
                        scanType = MemoryScanner.SCAN_TYPE.INTEGER;
                }
                else
                    scanType = MemoryScanner.SCAN_TYPE.SHORT;
            }
            else
                scanType = MemoryScanner.SCAN_TYPE.BYTE;

            return scanType;
        }

        public static string TypeToString(MemoryScanner.SCAN_TYPE type)
        {
            string str = "";
            switch (type)
            {
                case MemoryScanner.SCAN_TYPE.BYTE:
                    str = "BYTE";
                    break;
                case MemoryScanner.SCAN_TYPE.SHORT:
                    str = "SHORT";
                    break;
                case MemoryScanner.SCAN_TYPE.INTEGER:
                    str = "INTEGER";
                    break;
                case MemoryScanner.SCAN_TYPE.LONG:
                    str = "LONG";
                    break;
                case MemoryScanner.SCAN_TYPE.FLOAT:
                    str = "FLOAT";
                    break;
                case MemoryScanner.SCAN_TYPE.DOUBLE:
                    str = "DOUBLE";
                    break;
            }

            return str;
        }

        public static uint GetTypeLength(MemoryScanner.SCAN_TYPE type)
        {
            switch (type)
            {
                case MemoryScanner.SCAN_TYPE.BYTE:
                    return 1;
                case MemoryScanner.SCAN_TYPE.SHORT:
                    return 2;
                case MemoryScanner.SCAN_TYPE.INTEGER:
                    return 4;
                case MemoryScanner.SCAN_TYPE.LONG:
                    return 8;
                case MemoryScanner.SCAN_TYPE.FLOAT:
                    return 4;
                case MemoryScanner.SCAN_TYPE.DOUBLE:
                    return 8;
                default:
                    return 0;
            }
        }

        public static uint GetTypeLength(string type)
        {
            return MemoryScanner.GetTypeLength(MemoryScanner.StringToType(type));
        }

        public static bool CompareEqual(byte[] v1, byte[] v2, MemoryScanner.SCAN_TYPE type)
        {
            return ((IEnumerable<byte>) v1).SequenceEqual<byte>((IEnumerable<byte>) v2);
        }

        public static bool CompareLessThan(byte[] v1, byte[] v2, MemoryScanner.SCAN_TYPE type)
        {
            switch (type)
            {
                case SCAN_TYPE.BYTE:   return v1[0] < v2[0];
                case SCAN_TYPE.SHORT:  return BitConverter.ToInt16(v1, 0) < BitConverter.ToInt16(v2, 0);
                case SCAN_TYPE.INTEGER: return BitConverter.ToInt32(v1, 0) < BitConverter.ToInt32(v2, 0);
                case SCAN_TYPE.LONG:   return BitConverter.ToInt64(v1, 0) < BitConverter.ToInt64(v2, 0);
                case SCAN_TYPE.FLOAT:  return BitConverter.ToSingle(v1, 0) < BitConverter.ToSingle(v2, 0);
                case SCAN_TYPE.DOUBLE: return BitConverter.ToDouble(v1, 0) < BitConverter.ToDouble(v2, 0);
                default: return false;
            }
        }

        public static bool CompareGreaterThan(byte[] v1, byte[] v2, MemoryScanner.SCAN_TYPE type)
        {
            switch (type)
            {
                case SCAN_TYPE.BYTE:   return v1[0] > v2[0];
                case SCAN_TYPE.SHORT:  return BitConverter.ToInt16(v1, 0) > BitConverter.ToInt16(v2, 0);
                case SCAN_TYPE.INTEGER: return BitConverter.ToInt32(v1, 0) > BitConverter.ToInt32(v2, 0);
                case SCAN_TYPE.LONG:   return BitConverter.ToInt64(v1, 0) > BitConverter.ToInt64(v2, 0);
                case SCAN_TYPE.FLOAT:  return BitConverter.ToSingle(v1, 0) > BitConverter.ToSingle(v2, 0);
                case SCAN_TYPE.DOUBLE: return BitConverter.ToDouble(v1, 0) > BitConverter.ToDouble(v2, 0);
                default: return false;
            }
        }

        public static Dictionary<ulong, byte[]> ScanMemory(
            ulong address,
            byte[] data,
            MemoryScanner.SCAN_TYPE type,
            byte[] value,
            MemoryScanner.CompareFunction cfunc)
        {
            uint typeLength = MemoryScanner.GetTypeLength(type);
            Dictionary<ulong, byte[]> resultsDictionary = new Dictionary<ulong, byte[]>();
            uint lastReadableMemoryAddress = (uint) data.Length - typeLength; // Last memory address you can read out of this buffer without causing an exception.

            uint flooredSegmentLength = (uint) Math.Floor((decimal) data.Length / ThreadCount);
            bool useDefaultSearch = flooredSegmentLength < typeLength;

            // Default search
            if (useDefaultSearch) {
                for (uint index = 0; index < (uint) data.Length - typeLength; ++index)
                {
                    byte[] v1 = new byte[(int) typeLength];
                    Array.Copy((Array) data, (long) index, (Array) v1, 0L, (long) typeLength);
                    if (cfunc(v1, value, type))
                    {
                        resultsDictionary.Add(address + (ulong) index, value);
                    }
                }
                return resultsDictionary;
            }

            
            // If the memory address is too small, search synchronously.
            var myThreadCount = 0;
            void GetMatches(ref byte[] memorySection, uint startIndex, uint lastIndexToRead, int threadCount)
            {
                Debug.WriteLine($"Start Index: {startIndex}; Last Index: {lastIndexToRead}; Thread Count: {threadCount}; MemorySectionLength: {memorySection.LongLength}");
                Dictionary<ulong, byte[]> tempDictionary = new Dictionary<ulong, byte[]>();
                byte[] tempBuffer = new byte[(int)typeLength];
                for (uint currentIndex = startIndex; currentIndex <= lastIndexToRead - (long) typeLength; currentIndex++)
                {
                    Array.Clear(tempBuffer, 0, tempBuffer.Length); // Zero out array
                    Array.Copy((Array) memorySection, (long) currentIndex, (Array) tempBuffer, 0L, (long) typeLength);
                    if (cfunc(tempBuffer, value, type))
                    {
                        tempDictionary.Add(address + (ulong) currentIndex, value);
                    }
                }

                // Add matched results to the dictionary
                lock (resultsDictionary)
                {
                    foreach (var keyValuePair in tempDictionary)
                    {
                        resultsDictionary.Add(keyValuePair.Key, keyValuePair.Value);
                    }
                }
            }

            // Use multi-threading for search larger memory sections
            Task[] comparisonTasks = new Task[ThreadCount];
            for (int i = 0; i < ThreadCount - 1; i++)
            {
                uint startIndex = (uint)(i * flooredSegmentLength);
                uint lastIndex = (uint)((i+1) * flooredSegmentLength) - 1;
                Debug.WriteLine("Creating Task");
                comparisonTasks[i] = new Task(() => GetMatches(ref data, startIndex, lastIndex, myThreadCount++));
            }
            // Special case for the last thread
            uint lastStartIndex = (uint)((ThreadCount - 1) * flooredSegmentLength);
            Debug.WriteLine($"Last Start Index: {lastStartIndex}; Last Index: {data.Length}; Thread Count: {myThreadCount};");
            comparisonTasks[ThreadCount - 1] = new Task(() => GetMatches(ref data, lastStartIndex, lastReadableMemoryAddress, myThreadCount++));
            
            // Run all tasks 
            foreach (var comparisonTask in comparisonTasks)
            {
                comparisonTask.Start();
            }
            Task.WaitAll(comparisonTasks);
            return resultsDictionary;
        }

        public enum SCAN_TYPE
        {
            BYTE,
            SHORT,
            INTEGER,
            LONG,
            FLOAT,
            DOUBLE,
        }

        public delegate bool CompareFunction(byte[] v1, byte[] v2, MemoryScanner.SCAN_TYPE type);
    }
}